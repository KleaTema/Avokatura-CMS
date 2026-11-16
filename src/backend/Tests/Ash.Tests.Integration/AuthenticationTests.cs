using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Ash.Modules.Identity.Infrastructure;
using Ash.SharedKernel.Identity;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Runs against the test container, where the migrator seeded the @ash.local users. Task 051: the dev header only in
/// Development; production tokens from one or more providers (test providers "b2c" and "entra" signed with a local test
/// key — <see cref="TestIdentity"/>), the scheme chosen by the issuer, first-login linking by e-mail, then by subject.
/// </summary>
public class AuthenticationTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string WhoAmIPath = "/api/test/whoami";
    private const string EndpointBuilderKey = "__EndpointRouteBuilder";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private AshApiFactory CreateFactory(
        string environment, TestIdentity.Providers? providers = null, IReadOnlyDictionary<string, string?>? variables = null) =>
        new(Db, environment, services => services.AddSingleton<IStartupFilter, WhoAmIStartupFilter>(),
            providers: providers, environmentVariables: variables);

    private static HttpRequestMessage Request(string? devUser = null, string? token = null, string path = WhoAmIPath)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (devUser is not null)
        {
            request.Headers.Add("X-Dev-User", devUser);
        }

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    private static async Task<string?> WhoAmI(HttpClient client, string token)
    {
        var response = await client.SendAsync(Request(token: token), Ct);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return null;
        }

        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("email").GetString();
    }

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(Ct);
    }

    // ---------------------------------------------------------------- Development: the dev header

    [Fact]
    public async Task No_header_returns_401()
    {
        await using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        (await client.SendAsync(Request(), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Seeded_admin_returns_200_with_email_and_role()
    {
        await using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Request("admin@ash.local"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("email").GetString().ShouldBe("admin@ash.local");
        body.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ShouldContain("ADMIN");
    }

    [Fact]
    public async Task Unknown_email_returns_401()
    {
        await using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        (await client.SendAsync(Request("nobody@ash.local"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Inactive_user_returns_401()
    {
        const string email = "inactive-test@ash.local";
        await Scalar($"INSERT INTO users (email, first_name, last_name, is_active) VALUES ('{email}', 'Inactive', 'Test', 0)");
        try
        {
            await using var factory = CreateFactory("Development");
            using var client = factory.CreateClient();

            (await client.SendAsync(Request(email), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
        finally
        {
            await Scalar($"DELETE FROM users WHERE email = '{email}'");
        }
    }

    // ---------------------------------------------------------------- dev features outside Development

    [Fact]
    public async Task Production_ignores_dev_header_and_has_no_dev_routes()
    {
        await using var factory = CreateFactory("Production");
        using var client = factory.CreateClient();

        (await client.SendAsync(Request("admin@ash.local"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // the dev-only routes do not exist (404 even for a valid user — never 401/403 that would reveal them)
        var admin = TestIdentity.B2C($"oid-{Guid.NewGuid():N}", "admin@ash.local");
        var run = new HttpRequestMessage(HttpMethod.Post, "/api/admin/jobs/hearing-reminders/run");
        run.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        (await client.SendAsync(run, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.SendAsync(Request(token: admin, path: "/openapi/v1.json"), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // the login page's configuration: no dev login
        var config = await client.GetFromJsonAsync<JsonElement>("/api/auth/config", Ct);
        config.GetProperty("mode").GetString().ShouldBe("oidc");
        config.GetProperty("providers").GetArrayLength().ShouldBe(2);
    }

    [Theory]
    [InlineData("DevFeatures__HeaderAuth", "true")]
    [InlineData("DevFeatures__JobRunEndpoint", "true")]
    [InlineData("Scanner__Mode", "NoOp")]
    public void Production_with_a_dev_feature_switched_on_refuses_to_start(string variable, string value)
    {
        try
        {
            using var factory = CreateFactory("Production", variables: new Dictionary<string, string?> { [variable] = value });
            Should.Throw<InvalidOperationException>(() => factory.CreateClient()).Message.ShouldContain("Development-only");
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void Production_without_an_enabled_provider_fails_at_startup()
    {
        using var factory = CreateFactory("Production", TestIdentity.Providers.None);

        Should.Throw<InvalidOperationException>(() => factory.CreateClient()).Message.ShouldContain("Auth:Providers");
    }

    // ---------------------------------------------------------------- production tokens

    [Fact]
    public async Task Each_provider_is_accepted_when_enabled_and_refused_when_disabled()
    {
        var b2c = TestIdentity.B2C($"oid-{Guid.NewGuid():N}", "kryetar@ash.local");
        var entra = TestIdentity.Entra($"oid-{Guid.NewGuid():N}", "kryetar@ash.local");

        await using (var both = CreateFactory("Production"))
        {
            using var client = both.CreateClient();
            (await WhoAmI(client, b2c)).ShouldBe("kryetar@ash.local");
            (await WhoAmI(client, entra)).ShouldBe("kryetar@ash.local"); // the same user at two providers
        }

        await using (var onlyEntra = CreateFactory("Production", new TestIdentity.Providers(B2C: false, Entra: true)))
        {
            using var client = onlyEntra.CreateClient();
            (await WhoAmI(client, b2c)).ShouldBeNull();
            (await WhoAmI(client, entra)).ShouldBe("kryetar@ash.local");
        }

        (await Scalar("SELECT COUNT(*) FROM user_identities i JOIN users u ON u.id = i.user_id WHERE u.email = 'kryetar@ash.local'"))
            .ShouldBe(2L);
    }

    [Fact]
    public async Task Wrong_issuer_audience_tenant_signature_or_an_expired_token_is_401()
    {
        await using var factory = CreateFactory("Production");
        using var client = factory.CreateClient();
        var oid = $"oid-{Guid.NewGuid():N}";
        Claim[] b2cClaims = [new("oid", oid), new("emails", "kryetar@ash.local")];

        (await WhoAmI(client, TestIdentity.B2C(oid, "kryetar@ash.local"))).ShouldBe("kryetar@ash.local"); // the control

        (await WhoAmI(client, TestIdentity.Token("https://evil.example/v2.0", TestIdentity.B2CAudience, b2cClaims))).ShouldBeNull();
        (await WhoAmI(client, TestIdentity.Token(TestIdentity.B2CIssuer, "api://someone-else", b2cClaims))).ShouldBeNull();
        (await WhoAmI(client, TestIdentity.Token(TestIdentity.B2CIssuer, TestIdentity.B2CAudience, b2cClaims, key: TestIdentity.ForeignKey))).ShouldBeNull();
        // expired beyond the 2-minute clock skew
        (await WhoAmI(client, TestIdentity.B2C(oid, "kryetar@ash.local", DateTime.UtcNow.AddMinutes(-3)))).ShouldBeNull();
        // Entra ID: another tenant is never accepted
        (await WhoAmI(client, TestIdentity.Entra($"oid-{Guid.NewGuid():N}", "kryetar@ash.local", "22222222-2222-2222-2222-222222222222"))).ShouldBeNull();
        // not a JWT at all
        (await WhoAmI(client, "not-a-token")).ShouldBeNull();
    }

    [Fact]
    public async Task First_login_links_by_email_later_logins_by_subject_and_an_unknown_user_is_never_created()
    {
        await using var factory = CreateFactory("Production");
        using var client = factory.CreateClient();
        var oid = $"oid-{Guid.NewGuid():N}";

        // first login: the e-mail (any case) finds the active user the admin created; the identity is linked and audited
        (await WhoAmI(client, TestIdentity.B2C(oid, "Avokat1@ASH.local"))).ShouldBe("avokat1@ash.local");
        (await Scalar($"SELECT provider_name FROM user_identities WHERE subject = '{oid}'")).ShouldBe("b2c");

        // the e-mail changed at the provider: the subject still finds the same user
        (await WhoAmI(client, TestIdentity.B2C(oid, "new-address@example.test"))).ShouldBe("avokat1@ash.local");

        // nobody with this e-mail: 401, and no user is created
        var users = await Scalar("SELECT COUNT(*) FROM users");
        (await WhoAmI(client, TestIdentity.B2C($"oid-{Guid.NewGuid():N}", "stranger@example.test"))).ShouldBeNull();
        (await Scalar("SELECT COUNT(*) FROM users")).ShouldBe(users);

        // a second account of the same provider with the same e-mail is refused (one identity per provider)
        (await WhoAmI(client, TestIdentity.B2C($"oid-{Guid.NewGuid():N}", "avokat1@ash.local"))).ShouldBeNull();

        // IDENTITY_LINKED in the audit log (written in the background: wait, with a limit)
        var deadline = DateTime.UtcNow.AddSeconds(10);
        long linked = 0;
        while (linked == 0 && DateTime.UtcNow < deadline)
        {
            await using var connection = new MySqlConnection(Db.AuditConnectionString);
            await connection.OpenAsync(Ct);
            await using var command = new MySqlCommand(
                "SELECT COUNT(*) FROM ash_audit.audit_log WHERE action = 'IDENTITY_LINKED' AND occurred_at > UTC_TIMESTAMP(3) - INTERVAL 5 MINUTE", connection);
            linked = Convert.ToInt64(await command.ExecuteScalarAsync(Ct));
            if (linked == 0)
            {
                await Task.Delay(100, Ct);
            }
        }

        linked.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task The_admin_lists_and_unlinks_identities_and_the_next_login_links_again_by_email()
    {
        await using var factory = CreateFactory("Production");
        using var client = factory.CreateClient();
        var oid = $"oid-{Guid.NewGuid():N}";
        (await WhoAmI(client, TestIdentity.B2C(oid, "avokat2@ash.local"))).ShouldBe("avokat2@ash.local");
        var userId = Ash.SharedKernel.Ids.PublicId.FromBytes((byte[])(await Scalar("SELECT public_id FROM users WHERE email = 'avokat2@ash.local'"))!);
        var admin = TestIdentity.Entra($"oid-{Guid.NewGuid():N}", "admin@ash.local");

        var list = new HttpRequestMessage(HttpMethod.Get, $"/api/identity/admin/users/{userId}/identities");
        list.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        var identities = await (await client.SendAsync(list, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        var identity = identities.EnumerateArray().ShouldHaveSingleItem();
        identity.GetProperty("provider").GetString().ShouldBe("b2c");
        identity.TryGetProperty("subject", out _).ShouldBeFalse(); // never shown

        var unlink = new HttpRequestMessage(HttpMethod.Delete, $"/api/identity/admin/users/{userId}/identities/{identity.GetProperty("publicId").GetGuid()}");
        unlink.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        (await client.SendAsync(unlink, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Scalar($"SELECT COUNT(*) FROM user_identities WHERE subject = '{oid}'")).ShouldBe(0L);

        // the same account signs in again: linked again by e-mail (a new row)
        (await WhoAmI(client, TestIdentity.B2C(oid, "avokat2@ash.local"))).ShouldBe("avokat2@ash.local");
    }

    [Fact]
    public async Task A_query_string_token_is_accepted_on_the_hubs_only()
    {
        await using var factory = CreateFactory("Production");
        using var client = factory.CreateClient();
        var token = TestIdentity.B2C($"oid-{Guid.NewGuid():N}", "avokat1@ash.local");

        (await client.SendAsync(Request(path: $"{WhoAmIPath}?access_token={token}"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PostAsync($"/hubs/notifications/negotiate?negotiateVersion=1&access_token={token}", null, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>Maps the test-only endpoint into the host's global route builder, so it runs through the real pipeline.</summary>
    private sealed class WhoAmIStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.UseRouting();
            var routes = (IEndpointRouteBuilder)app.Properties[EndpointBuilderKey]!;
            routes.MapGet(WhoAmIPath, async (ICurrentUser user, IdentityDbContext db, CancellationToken ct) =>
            {
                var publicId = user.PublicId;
                var email = await db.Users.AsNoTracking()
                    .Where(u => u.PublicId == publicId)
                    .Select(u => u.Email)
                    .SingleAsync(ct);
                return Results.Ok(new { email, roles = user.Roles });
            }).RequireAuthorization();
            next(app);
        };
    }
}
