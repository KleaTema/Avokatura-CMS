using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 051, items 5–9: rate limits, request limits, CSV injection, the immediate effect of access changes, safe errors
/// and logs without secrets.
/// </summary>
public class HardeningTests(MySqlFixture db) : IntegrationTest(db)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(Ct);
    }

    private async Task<Guid> PublicIdOf(string table, long id) =>
        Ash.SharedKernel.Ids.PublicId.FromBytes((byte[])(await Scalar($"SELECT public_id FROM {table} WHERE id = {id}"))!);

    // ---------------------------------------------------------------- 5. rate limits

    [Fact]
    public async Task Over_the_limit_a_user_gets_429_with_retry_after_and_another_user_is_not_affected()
    {
        await using var factory = new AshApiFactory(Db, environmentVariables: new Dictionary<string, string?>
        {
            ["RateLimits__search__PermitLimit"] = "3",
            ["RateLimits__search__WindowSeconds"] = "60",
            ["RateLimits__crossOffice__PermitLimit"] = "2",
            ["RateLimits__crossOffice__WindowSeconds"] = "60",
        });
        try
        {
            using var monitor = factory.CreateClient().AsUser("monitor@ash.local");
            for (var i = 0; i < 3; i++)
            {
                (await monitor.GetAsync("/api/search?q=test", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
            }

            var limited = await monitor.GetAsync("/api/search?q=test", Ct);
            limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
            limited.Headers.RetryAfter.ShouldNotBeNull();
            var problem = await limited.Content.ReadFromJsonAsync<JsonElement>(Ct);
            problem.GetProperty("title").GetString().ShouldBe("Shumë kërkesa. Provoni pas pak.");
            // the case list's text search counts on the same limit; the list without a text does not
            (await monitor.GetAsync("/api/cases?tab=all&q=test", Ct)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
            (await monitor.GetAsync("/api/cases?tab=all", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

            // per user: someone else still searches
            using var kryetar = factory.CreateClient().AsUser("kryetar@ash.local");
            (await kryetar.GetAsync("/api/search?q=test", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

            // the registration desk's cross-office search has its own, stricter limit
            using var protokoll = factory.CreateClient().AsUser("protokoll@ash.local");
            for (var i = 0; i < 2; i++)
            {
                (await protokoll.GetAsync("/api/cases/registration-search?q=test", Ct)).StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
            }

            (await protokoll.GetAsync("/api/cases/registration-search?q=test", Ct)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        }
        finally
        {
            foreach (var name in new[] { "RateLimits__search__PermitLimit", "RateLimits__search__WindowSeconds", "RateLimits__crossOffice__PermitLimit", "RateLimits__crossOffice__WindowSeconds" })
            {
                Environment.SetEnvironmentVariable(name, null);
            }
        }
    }

    // ---------------------------------------------------------------- 6. request limits

    [Fact]
    public async Task Bodies_over_1_MB_are_413_deep_json_and_other_content_types_are_refused()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser("avokat1@ash.local");
        var casePath = $"/api/cases/{await PublicIdOf("cases", s.ActiveCaseId)}";

        // 1 MB + 1 byte of JSON → 413 before anything is read
        var big = new StringContent("{\"objectText\":\"" + new string('x', 1024 * 1024) + "\"}", Encoding.UTF8, "application/json");
        var tooLarge = await client.PatchAsync($"{casePath}/object", big, Ct);
        tooLarge.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        (await tooLarge.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("title").GetString().ShouldBe("Kërkesa është shumë e madhe.");

        // JSON nested deeper than 32 levels → 400, never a stack overflow
        var deep = new string('[', 40) + new string(']', 40);
        (await client.PostAsync("/api/reports/saved", new StringContent($"{{\"name\":\"x\",\"table\":1,\"filters\":{{\"a\":{deep}}},\"columns\":[]}}", Encoding.UTF8, "application/json"), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // a JSON endpoint given another content type → 415
        (await client.PostAsync("/api/reports/saved", new StringContent("name=x", Encoding.UTF8, "application/x-www-form-urlencoded"), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);

        // the document upload keeps its own, larger limit (task 033): 1 MB + 1 byte is not refused for its size
        var upload = new MultipartFormDataContent { { new ByteArrayContent(new byte[1024 * 1024 + 1]), "file", "a.pdf" } };
        (await client.PostAsync($"{casePath}/documents", upload, Ct)).StatusCode.ShouldNotBe(HttpStatusCode.RequestEntityTooLarge);

        // a multipart boundary longer than 70 characters → 400
        // (written by hand: HttpClient itself refuses such a boundary)
        var boundary = new string('b', 71);
        var raw = new StringContent($"--{boundary}\r\nContent-Disposition: form-data; name=\"title\"\r\n\r\nx\r\n--{boundary}--\r\n");
        raw.Headers.ContentType = MediaTypeHeaderValue.Parse($"multipart/form-data; boundary={boundary}");
        (await client.PostAsync($"{casePath}/documents", raw, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---------------------------------------------------------------- 7. CSV injection

    [Fact]
    public async Task A_formula_in_a_party_name_is_exported_as_text()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await Scalar($"""
            INSERT INTO case_parties (case_id, party_role_id, party_kind, display_name, created_by)
            SELECT {s.ActiveCaseId}, id, 'NATURAL', '=HYPERLINK("http://x","y")', {s.User("protokoll").Id} FROM party_roles WHERE code = 'PADITES'
            """);
        var code = (string)(await Scalar($"SELECT case_code FROM cases WHERE id = {s.ActiveCaseId}"))!;
        await using var factory = new AshApiFactory(Db);
        using var kryetar = factory.CreateClient().AsUser("kryetar@ash.local");

        var filters = Uri.EscapeDataString(JsonSerializer.Serialize(new { caseCode = code }));
        var csv = await kryetar.GetAsync($"/api/reports/builder/1/export?format=csv&columns=caseCode,plaintiffs&filters={filters}", Ct);
        csv.StatusCode.ShouldBe(HttpStatusCode.OK);
        var text = Encoding.UTF8.GetString((await csv.Content.ReadAsByteArrayAsync(Ct))[3..]);
        var row = text.Split("\r\n")[1];
        // a leading quote: Excel shows the text and never runs it (the field is quoted for its own '"')
        row.ShouldContain("\"'=HYPERLINK(\"\"http://x\"\",\"\"y\"\")\"");
        row.ShouldNotContain(";=HYPERLINK");
    }

    // ---------------------------------------------------------------- 8. immediate effect

    [Fact]
    public async Task Deactivation_role_removal_and_an_ended_assistant_link_count_from_the_next_request()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var factory = new AshApiFactory(Db);
        using var admin = factory.CreateClient().AsUser("admin@ash.local");
        var casePath = $"/api/cases/{await PublicIdOf("cases", s.ActiveCaseId)}";

        // a role: the Kryetar's office view goes with OFFICE_HEAD at once (not after the 60-second cache)
        var extra = await s.AddOfficeUserAsync("Kryetar2", s.OfficeTrId, "OFFICE_HEAD");
        using var head = factory.CreateClient().AsUser(extra);
        (await head.GetAsync(casePath, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK); // cached now
        var headId = await PublicIdOf("users", Convert.ToInt64(await Scalar($"SELECT id FROM users WHERE email = '{extra}'")));
        (await admin.DeleteAsync($"/api/identity/admin/users/{headId}/roles/OFFICE_HEAD", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await head.GetAsync(casePath, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // deactivation: 401 from the next request
        using var lawyer = factory.CreateClient().AsUser("avokat2@ash.local");
        (await lawyer.GetAsync(casePath, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var lawyerId = await PublicIdOf("users", s.User("avokat2").Id);
        try
        {
            (await admin.PostAsync($"/api/identity/admin/users/{lawyerId}/deactivate", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await lawyer.GetAsync(casePath, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
        finally
        {
            await Scalar($"UPDATE users SET is_active = 1 WHERE id = {s.User("avokat2").Id}");
        }

        // task 053q: the primary lawyer removes the assistant from the case → the access goes with the next request
        using var assistant = factory.CreateClient().AsUser("ndihmes@ash.local");
        using var primary = factory.CreateClient().AsUser("avokat1@ash.local");
        (await assistant.GetAsync(casePath, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var rowVersion = Convert.ToUInt32(await Scalar($"SELECT row_version FROM cases WHERE id = {s.ActiveCaseId}"));
        var removed = await primary.DeleteAsync(
            $"{casePath}/assistants/{await PublicIdOf("users", s.User("ndihmes").Id)}?rowVersion={rowVersion}", Ct);
        removed.IsSuccessStatusCode.ShouldBeTrue(await removed.Content.ReadAsStringAsync(Ct));
        (await assistant.GetAsync(casePath, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_blocked_sign_in_account_is_401_at_once_other_accounts_work_and_allow_lets_it_link_again()
    {
        await using var factory = new AshApiFactory(Db, environment: "Production");
        using var client = factory.CreateClient();
        var b2cOid = $"oid-{Guid.NewGuid():N}";
        var entraOid = $"oid-{Guid.NewGuid():N}";

        async Task<HttpStatusCode> Me(string token)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/identity/me");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return (await client.SendAsync(request, Ct)).StatusCode;
        }

        (await Me(TestIdentity.B2C(b2cOid, "avokat1@ash.local"))).ShouldBe(HttpStatusCode.OK);
        (await Me(TestIdentity.Entra(entraOid, "avokat1@ash.local"))).ShouldBe(HttpStatusCode.OK);

        var userId = Ash.SharedKernel.Ids.PublicId.FromBytes((byte[])(await Scalar("SELECT public_id FROM users WHERE email = 'avokat1@ash.local'"))!);
        var identityId = Ash.SharedKernel.Ids.PublicId.FromBytes((byte[])(await Scalar($"SELECT public_id FROM user_identities WHERE subject = '{b2cOid}'"))!);
        var admin = TestIdentity.Entra($"oid-{Guid.NewGuid():N}", "admin@ash.local");
        HttpRequestMessage AsAdmin(HttpMethod method, string path, object? body = null)
        {
            var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);
            return request;
        }

        (await client.SendAsync(AsAdmin(HttpMethod.Delete, $"/api/identity/admin/users/{userId}/identities/{identityId}"), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // blocked from the next request, and the same account is never re-linked by e-mail; the other account works
        (await Me(TestIdentity.B2C(b2cOid, "avokat1@ash.local"))).ShouldBe(HttpStatusCode.Unauthorized);
        (await Me(TestIdentity.Entra(entraOid, "avokat1@ash.local"))).ShouldBe(HttpStatusCode.OK);
        (await Scalar($"SELECT blocked_at IS NOT NULL FROM user_identities WHERE subject = '{b2cOid}'")).ShouldBe(1L);

        // "Lejo sërish": a reason is required; then the next login links again by e-mail
        (await client.SendAsync(AsAdmin(HttpMethod.Post, $"/api/identity/admin/users/{userId}/identities/{identityId}/allow", new { reason = "" }), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.SendAsync(AsAdmin(HttpMethod.Post, $"/api/identity/admin/users/{userId}/identities/{identityId}/allow", new { reason = "U shkëput gabimisht" }), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Me(TestIdentity.B2C(b2cOid, "avokat1@ash.local"))).ShouldBe(HttpStatusCode.OK);
    }

    // ---------------------------------------------------------------- 9. errors and logs

    [Fact]
    public async Task A_production_500_has_no_exception_text_only_a_generic_title_and_a_trace_id()
    {
        await using var factory = new AshApiFactory(Db, environment: "Production",
            configureServices: services => services.AddSingleton<IStartupFilter, BoomStartupFilter>());
        using var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, BoomStartupFilter.Path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestIdentity.B2C($"oid-{Guid.NewGuid():N}", "monitor@ash.local"));

        var response = await client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync(Ct);
        body.ShouldNotContain(BoomStartupFilter.Secret);
        body.ShouldNotContain("at Ash.");
        body.ShouldNotContain("InvalidOperationException");
        var problem = JsonDocument.Parse(body).RootElement;
        problem.GetProperty("title").GetString().ShouldBe("Ndodhi një gabim i papritur.");
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task The_logs_of_the_whole_run_hold_no_tokens_dev_users_passwords_connection_strings_or_search_text()
    {
        // representative traffic of its own (the scan also covers everything the suite logged before)
        await using (var development = new AshApiFactory(Db))
        {
            using var client = development.CreateClient().AsUser("monitor@ash.local");
            await client.GetAsync("/api/search?q=SekretiKerkimit", Ct);
            await client.GetAsync("/api/cases?tab=all&q=SekretiKerkimit", Ct);
            await client.GetAsync("/hubs/notifications/negotiate?dev_user=monitor@ash.local", Ct);
            using var stranger = development.CreateClient().AsUser("nobody@ash.local");
            await stranger.GetAsync("/api/identity/me", Ct);
        }

        await using (var production = new AshApiFactory(Db, environment: "Production"))
        {
            using var client = production.CreateClient();
            var token = TestIdentity.B2C($"oid-{Guid.NewGuid():N}", "stranger@example.test");
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/identity/me");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            await client.SendAsync(request, Ct);
            await client.PostAsync($"/hubs/notifications/negotiate?negotiateVersion=1&access_token={token}", null, Ct);
        }

        Regex[] secrets =
        [
            new(@"eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\."),                    // a JWT
            new(@"X-Dev-User", RegexOptions.IgnoreCase),                         // the dev header
            new(@"[?&]dev_user=(?!\*\*\*)", RegexOptions.IgnoreCase),             // the hubs' dev user
            new(@"[?&]access_token=(?!\*\*\*)", RegexOptions.IgnoreCase),         // the hubs' token
            new(@"[?&]q=(?!\*\*\*)", RegexOptions.IgnoreCase),                    // the search text
            new(@"SekretiKerkimit"),
            new(@"(?i)(password|pwd)\s*=\s*[^;*\s]"),                             // a password
            new(@"(?i)server=[^;]+;.*(user|uid)\s*=", RegexOptions.IgnoreCase),   // a connection string
            new(@"(?i)secretkey\s*[=:]"),
        ];
        var lines = SuiteLogSink.Instance.Lines.ToList();
        lines.Count.ShouldBeGreaterThan(0);
        var found = lines.SelectMany(line => secrets.Where(r => r.IsMatch(line)).Select(r => $"{r}: {line[..Math.Min(line.Length, 300)]}"))
            .Distinct()
            .Take(20)
            .ToList();
        found.ShouldBeEmpty();
    }

    /// <summary>A test-only endpoint that throws with a secret in its message.</summary>
    private sealed class BoomStartupFilter : IStartupFilter
    {
        public const string Path = "/api/test/boom";
        public const string Secret = "geheim-detail-7f3a";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.UseRouting();
            var routes = (IEndpointRouteBuilder)app.Properties["__EndpointRouteBuilder"]!;
            routes.MapGet(Path, IResult () => throw new InvalidOperationException(Secret)).RequireAuthorization();
            next(app);
        };
    }
}
