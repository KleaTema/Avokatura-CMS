using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 051, item 3 — authorization over EVERY endpoint, read from the host's EndpointDataSource (a new endpoint is
/// covered without touching this file):
/// <list type="bullet">
/// <item>every endpoint requires authorization except an explicit allow-list;</item>
/// <item>every endpoint with an object id in its route (case, dossier, document, hearing, party, conversation, message,
/// notification, handoff, assistant link, saved report), called by users of office TR with the id of an object of office
/// DR that they cannot see, answers 404 — never 2xx, never a 403 that reveals the object (a 403 is accepted only when
/// the caller lacks the endpoint's own system permission, which says nothing about the object);</item>
/// <item>every mutating endpoint has a system permission or goes through the case access (route) — or is on the
/// documented allow-list.</item>
/// </list>
/// </summary>
public class EndpointAuthorizationTests(MySqlFixture db) : IntegrationTest(db)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly ITestOutputHelper _output = TestContext.Current.TestOutputHelper!;

    /// <summary>Anonymous by design: the health check, the login page's configuration, the API description in Development.</summary>
    private static readonly string[] AnonymousAllowList =
    [
        "/health", "/api/auth/config", "/openapi/{documentName}.json",
        "/scalar/{documentName?}", "/scalar/favicon.svg", "/scalar/scalar.aspnetcore.js", "/scalar/scalar.js",
    ];

    /// <summary>
    /// Object endpoints answered by a system permission whose holders see EVERY object of that kind (administration,
    /// the Monitor's supervisions): there is no object of another office to hide from them.
    /// </summary>
    private static readonly string[] GlobalScopePrefixes = ["/api/identity/admin/", "/api/reference/admin/", "/api/supervisions/"];

    /// <summary>
    /// Client decision Q1a: the Protokoll continues a dossier of ANY office (a next-level summons may arrive at another
    /// office), so case.link_previous holders see these endpoints across offices by design.
    /// </summary>
    private static readonly string[] CrossOfficeByDesign = ["/api/cases/{previousCasePublicId:guid}/"];

    /// <summary>
    /// Mutating endpoints without a system permission and without a case / document / dossier id in the route — each
    /// with why that is safe (checked: no entry may go stale, every such endpoint must be here).
    /// </summary>
    private static readonly Dictionary<string, string> MutatingAllowList = new()
    {
        ["POST /api/chat/direct"] = "always 403 under the CaseOnly chat policy",
        ["DELETE /api/chat/messages/{id:guid}"] = "own message only (404 otherwise)",
        ["PUT /api/chat/messages/{id:guid}"] = "own message only (404 otherwise)",
        ["POST /api/chat/conversations/{id:guid}/messages"] = "current participants only (404 otherwise)",
        ["POST /api/chat/conversations/{id:guid}/read"] = "current participants only (404 otherwise)",
        ["POST /api/notifications/{id:guid}/read"] = "own notification only (404 otherwise)",
        ["POST /api/notifications/read-all"] = "own notifications only",
        ["POST /api/reports/saved/"] = "own saved report (report.* checked)",
        ["PUT /api/reports/saved/{id:guid}"] = "own saved report only (404 otherwise)",
        ["DELETE /api/reports/saved/{id:guid}"] = "own saved report only (404 otherwise)",
    };

    private sealed record Objects(
        Guid Case, Guid Dossier, Guid Document, Guid Party, Guid Hearing, Guid Conversation, Guid Message,
        Guid Notification, Guid SavedReport, Guid AssistantLink);

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(Ct);
    }

    private async Task<Guid> PublicId(string sql) =>
        Ash.SharedKernel.Ids.PublicId.FromBytes((byte[])(await Scalar(sql))!);

    private static List<RouteEndpoint> Endpoints(AshApiFactory factory) =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

    private static string MethodOf(RouteEndpoint e) =>
        e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.FirstOrDefault() ?? "GET";

    private static IReadOnlyList<string> PermissionsOf(RouteEndpoint e) =>
        e.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(a => a.Policy)
            .OfType<string>()
            .Where(p => p.StartsWith("perm:", StringComparison.Ordinal))
            .Select(p => p["perm:".Length..])
            .ToList();

    // ---------------------------------------------------------------- every endpoint requires authorization

    [Fact]
    public async Task Every_endpoint_requires_authorization_except_the_allow_list()
    {
        await using var factory = new AshApiFactory(Db);
        using var _ = factory.CreateClient();

        var anonymous = Endpoints(factory)
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(e => e.RoutePattern.RawText ?? string.Empty)
            .Distinct()
            .Order()
            .ToList();

        anonymous.ShouldBe(AnonymousAllowList.Order().ToList(), ignoreOrder: true);
        // the fallback policy (an authenticated user) covers every other endpoint
        using var client = factory.CreateClient();
        foreach (var e in Endpoints(factory).Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null && !(e.RoutePattern.RawText ?? "").Contains('{')))
        {
            var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(MethodOf(e)), e.RoutePattern.RawText), Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{MethodOf(e)} {e.RoutePattern.RawText}");
        }
    }

    // ---------------------------------------------------------------- mutating endpoints

    [Fact]
    public async Task Every_mutating_endpoint_has_a_permission_or_the_case_access_or_a_documented_reason()
    {
        await using var factory = new AshApiFactory(Db);
        using var _ = factory.CreateClient();
        string[] caseRoutes = ["/api/cases/{id", "/api/cases/{caseId", "/api/cases/{previousCasePublicId", "/api/documents/{docId", "/api/dossiers/{dossierPublicId", "/api/chat/cases/{caseId"];

        var unexplained = new List<string>();
        var used = new HashSet<string>();
        foreach (var e in Endpoints(factory).Where(e => MethodOf(e) is "POST" or "PUT" or "PATCH" or "DELETE"))
        {
            var route = e.RoutePattern.RawText ?? string.Empty;
            var key = $"{MethodOf(e)} {route}";
            if (PermissionsOf(e).Count > 0 || caseRoutes.Any(route.StartsWith))
            {
                continue;
            }

            if (MutatingAllowList.ContainsKey(key))
            {
                used.Add(key);
                continue;
            }

            unexplained.Add(key);
        }

        unexplained.ShouldBeEmpty("a mutating endpoint without a permission, a case route or a documented reason");
        MutatingAllowList.Keys.Except(used).ShouldBeEmpty("stale entries of the allow-list");
    }

    // ---------------------------------------------------------------- objects of another office → 404

    private async Task<Objects> SeedOfficeDrAsync(CaseSeed s, AshApiFactory factory)
    {
        var hearingAt = DateTime.UtcNow.AddDays(10);
        var (caseId, _) = await s.AddOfficeCaseAsync(s.OfficeDrId, "AUTHZ", s.User("drlawyer").Id, hearingAt);
        var protokoll = s.User("protokoll").Id;
        // registered in DR: the registering Protokoll stays in the case chat (053g), so it must not be TR's
        await Scalar($"UPDATE cases SET registered_by = {s.User("drlawyer").Id} WHERE id = {caseId}");
        await Scalar($"""
            INSERT INTO case_parties (case_id, party_role_id, party_kind, display_name, created_by)
            SELECT {caseId}, id, 'NATURAL', 'Pala DR', {protokoll} FROM party_roles WHERE code = 'PADITES';
            INSERT INTO documents (dossier_id, case_id, document_type_id, title, original_file_name, content_type, size_bytes,
                                   sha256, storage_bucket, storage_key, scan_status, uploaded_by)
            SELECT c.dossier_id, c.id, t.id, 'Dok DR', 'dr.pdf', 'application/pdf', 10, UNHEX(SHA2(UUID(), 256)), 'b', UUID(), 'CLEAN', {protokoll}
              FROM cases c JOIN document_types t ON t.code = 'VENDIM' WHERE c.id = {caseId};
            INSERT INTO notifications (user_id, notification_type, case_id, title, created_at)
            VALUES ({s.User("drlawyer").Id}, 'CASE_CLOSED', {caseId}, 'DR', UTC_TIMESTAMP(3));
            """);
        _output.WriteLine("seed: case, party, document, notification");
        var assistant = await s.AddOfficeUserAsync("DrAssistant", s.OfficeDrId, "LEGAL_ASSISTANT");
        await Scalar($"""
            INSERT INTO assistant_lawyers (assistant_user_id, lawyer_user_id, office_id, created_by)
            SELECT u.id, {s.User("drlawyer").Id}, {s.OfficeDrId}, {protokoll} FROM users u WHERE u.email = '{assistant}'
            """);

        // the DR lawyer's case chat and one message (through the API, as he would)
        _output.WriteLine("seed: assistant link");
        var drEmail = (string)(await Scalar($"SELECT email FROM users WHERE id = {s.User("drlawyer").Id}"))!;
        var casePublic = await PublicId($"SELECT public_id FROM cases WHERE id = {caseId}");
        using var dr = factory.CreateClient().AsUser(drEmail);
        var conversation = (await (await dr.PostAsync($"/api/chat/cases/{casePublic}/conversation", null, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        var sent = await dr.PostAsJsonAsync($"/api/chat/conversations/{conversation}/messages", new { body = "DR" }, Ct);
        var message = (await sent.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        // the Monitor's saved report (another user's)
        _output.WriteLine("seed: chat");
        using var monitor = factory.CreateClient().AsUser("monitor@ash.local");
        var saved = await monitor.PostAsJsonAsync("/api/reports/saved", new { name = $"M {Guid.NewGuid():N}", table = 1, filters = new { }, columns = Array.Empty<string>() }, Ct);
        var savedId = (await saved.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("publicId").GetGuid();

        return new Objects(
            casePublic,
            await PublicId($"SELECT d.public_id FROM dossiers d JOIN cases c ON c.dossier_id = d.id WHERE c.id = {caseId}"),
            await PublicId($"SELECT public_id FROM documents WHERE case_id = {caseId}"),
            await PublicId($"SELECT public_id FROM case_parties WHERE case_id = {caseId}"),
            await PublicId($"SELECT public_id FROM case_hearings WHERE case_id = {caseId}"),
            conversation, message,
            await PublicId($"SELECT public_id FROM notifications WHERE case_id = {caseId}"),
            savedId,
            await PublicId($"SELECT public_id FROM assistant_lawyers WHERE office_id = {s.OfficeDrId}"));
    }

    /// <summary>The DR object for a route parameter, by the parameter's name and the route it is in; null = not an object id.</summary>
    private static Guid? ObjectFor(string route, string parameter, Objects o) => parameter switch
    {
        "caseId" or "previousCasePublicId" => o.Case,
        "id" when route.StartsWith("/api/cases/") => o.Case,
        "id" when route.StartsWith("/api/chat/conversations/") => o.Conversation,
        "id" when route.StartsWith("/api/chat/messages/") => o.Message,
        "id" when route.StartsWith("/api/notifications/") => o.Notification,
        "id" when route.StartsWith("/api/reports/saved/") => o.SavedReport,
        "docId" => o.Document,
        "partyId" => o.Party,
        "hearingId" => o.Hearing,
        "dossierPublicId" => o.Dossier,
        "linkPublicId" => o.AssistantLink,
        _ => null,
    };

    [Fact]
    public async Task Every_object_endpoint_answers_404_for_an_object_the_user_cannot_see()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var factory = new AshApiFactory(Db);
        var objects = await SeedOfficeDrAsync(s, factory);
        string[] callers = ["kryetar", "protokoll", "avokat1", "ndihmes", "jurist", "institucion"];
        var clients = callers.ToDictionary(c => c, c => factory.CreateClient().AsUser($"{c}@ash.local"));

        var failures = new List<string>();
        var covered = new List<string>();
        foreach (var e in Endpoints(factory).OrderBy(e => e.RoutePattern.RawText))
        {
            var route = e.RoutePattern.RawText ?? string.Empty;
            var parameters = e.RoutePattern.Parameters.Select(p => p.Name).ToList();
            if (parameters.Count == 0 || GlobalScopePrefixes.Any(route.StartsWith)
                || parameters.All(p => ObjectFor(route, p, objects) is null))
            {
                continue;
            }

            var method = MethodOf(e);
            string PathWith(Func<string, Guid> id) => parameters.Aggregate(route, (current, p) =>
                System.Text.RegularExpressions.Regex.Replace(current, $@"\{{{p}(:[^}}]*)?\??\}}", id(p).ToString()));
            var path = PathWith(p => ObjectFor(route, p, objects) ?? Guid.NewGuid());
            // the same call with ids that exist nowhere: an answer equal to that one reveals nothing about the object
            var nowhere = PathWith(_ => Guid.NewGuid());
            var permissions = PermissionsOf(e);
            var results = new List<string>();
            var notFound = false;
            foreach (var caller in callers)
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = BodyFor(e) };
                // every call has its own limit: one stuck endpoint fails with its name instead of stalling the run
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(Ct);
                limit.CancelAfter(TimeSpan.FromSeconds(10));
                HttpResponseMessage response;
                try
                {
                    response = await clients[caller].SendAsync(request, limit.Token);
                }
                catch (OperationCanceledException) when (!Ct.IsCancellationRequested)
                {
                    failures.Add($"{method} {route}: {caller} — no answer within 10 s");
                    results.Add($"{caller}=timeout");
                    continue;
                }

                _output.WriteLine($"{method} {route} {caller} → {(int)response.StatusCode}");
                var status = (int)response.StatusCode;
                results.Add($"{caller}={status}");
                var lacksPermission = permissions.Any(p => !s.User(caller).Permissions.Contains(p));
                if (status == 404)
                {
                    notFound = true;
                }
                else if (status == 403 && lacksPermission)
                {
                    // the endpoint's own permission: says nothing about the object
                }
                else if (CrossOfficeByDesign.Any(route.StartsWith) && s.User(caller).Permissions.Contains("case.link_previous"))
                {
                    // Q1a: the Protokoll's next-level endpoints work across offices
                }
                else if (status is >= 400 and < 500 && await SameAsNowhereAsync(clients[caller], method, nowhere, e, status, response))
                {
                    // the body is refused before the object is looked at (the same answer for an id that does not
                    // exist): nothing about the object is revealed
                    results[^1] += "(body)";
                }
                else
                {
                    failures.Add($"{method} {route}: {caller} got {status} — {await response.Content.ReadAsStringAsync(Ct)}");
                }
            }

            if (!notFound && !results.Any(r => r.EndsWith("(body)", StringComparison.Ordinal))
                && !CrossOfficeByDesign.Any(route.StartsWith))
            {
                failures.Add($"{method} {route}: nobody got 404 ({string.Join(", ", results)})");
            }

            covered.Add($"{method} {route} → {string.Join(", ", results)}");
        }

        _output.WriteLine($"Covered {covered.Count} object endpoints:");
        foreach (var line in covered)
        {
            _output.WriteLine("  " + line);
        }

        covered.Count.ShouldBeGreaterThan(40);
        failures.ShouldBeEmpty();
        foreach (var client in clients.Values)
        {
            client.Dispose();
        }
    }

    /// <summary>The same request with ids that exist nowhere gives the same status and the same problem code.</summary>
    private static async Task<bool> SameAsNowhereAsync(
        HttpClient client, string method, string nowhere, RouteEndpoint e, int status, HttpResponseMessage response)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), nowhere) { Content = BodyFor(e) };
        var other = await client.SendAsync(request, Ct);
        return (int)other.StatusCode == status && CodeOf(await other.Content.ReadAsStringAsync(Ct)) == CodeOf(await response.Content.ReadAsStringAsync(Ct));
    }

    private static string? CodeOf(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.TryGetProperty("code", out var code) ? code.GetString() : json.RootElement.GetProperty("title").GetString();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return body;
        }
    }

    /// <summary>
    /// A valid-looking body for the endpoint's request type (every property filled: text, ids, numbers, dates), so the
    /// call reaches the access check instead of stopping at a missing field. Multipart endpoints get an empty form.
    /// </summary>
    private static HttpContent? BodyFor(RouteEndpoint e)
    {
        var accepts = e.Metadata.GetMetadata<IAcceptsMetadata>();
        if (accepts is null)
        {
            return null;
        }

        if (accepts.ContentTypes.Any(t => t.StartsWith("multipart/", StringComparison.Ordinal)))
        {
            return new MultipartFormDataContent { { new StringContent("x"), "title" } };
        }

        return accepts.RequestType is { } type
            ? new StringContent(JsonSerializer.Serialize(Sample(type, 0), new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json")
            : new StringContent("{}", Encoding.UTF8, "application/json");
    }

    private static object? Sample(Type type, int depth)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (depth > 3) return null;
        if (t == typeof(string)) return "Test";
        if (t == typeof(Guid)) return Guid.NewGuid();
        if (t == typeof(bool)) return false;
        if (t == typeof(int) || t == typeof(long) || t == typeof(uint) || t == typeof(short) || t == typeof(byte) || t == typeof(ushort)) return 1;
        if (t == typeof(decimal) || t == typeof(double)) return 1;
        if (t == typeof(DateOnly)) return DateOnly.FromDateTime(DateTime.UtcNow);
        if (t == typeof(DateTime)) return DateTime.UtcNow.AddDays(1);
        if (t == typeof(TimeOnly)) return new TimeOnly(10, 0);
        if (t == typeof(JsonElement)) return new { };
        if (t.IsEnum) return Enum.GetValues(t).GetValue(0);
        if (t != typeof(string) && typeof(System.Collections.IEnumerable).IsAssignableFrom(t)) return Array.Empty<object>();
        var values = new Dictionary<string, object?>();
        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead))
        {
            values[JsonNamingPolicy.CamelCase.ConvertName(p.Name)] = Sample(p.PropertyType, depth + 1);
        }

        return values;
    }
}
