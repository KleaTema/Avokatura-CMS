using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.Infrastructure.Jobs;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 053e — "Ndihmësit ligjorë" (client decision C, default D4): the Kryetar attaches a legal assistant to lawyers of
/// the office; AssistantSync gives the assistant every case where such a lawyer is the CURRENT primary. Uses
/// <see cref="CaseSeed"/>'s ACTIVE case (avokat1 primary) plus two more avokat1 cases and one of avokat2 (office TR),
/// and a new assistant of TR.
/// </summary>
public class AssistantTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string Kryetar = "kryetar@ash.local";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Scenario(
        CaseSeed Seed, AshApiFactory Factory, long AssistantId, Guid Assistant, string AssistantEmail,
        Guid Avokat1, Guid Avokat2, List<string> Avokat1Cases, string Avokat2Case) : IAsyncDisposable
    {
        public HttpClient As(string email) => Factory.CreateClient().AsUser(email);

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            await Seed.DisposeAsync();
        }
    }

    private async Task<Scenario> ScenarioAsync()
    {
        var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await using var seed = await Seed.CreateAsync(Db.AppConnectionString);
        var assistantId = await seed.UserAsync("asst2", "Ndihmës", "Dy", s.OfficeTrId, null, "LEGAL_ASSISTANT");
        var a1 = s.User("avokat1").Id;
        var extra1 = await s.AddOfficeCaseAsync(s.OfficeTrId, "AS1", a1);
        var extra2 = await s.AddOfficeCaseAsync(s.OfficeTrId, "AS2", a1);
        var other = await s.AddOfficeCaseAsync(s.OfficeTrId, "AS3", s.User("avokat2").Id);
        var seedCase = (string)(await Scalar($"SELECT case_code FROM cases WHERE id = {s.ActiveCaseId}"))!;
        return new Scenario(s, new AshApiFactory(Db), assistantId, await PublicIdOf("users", assistantId), $"asst2-{seed.Suffix}@ash.test",
            await PublicIdOf("users", a1), await PublicIdOf("users", s.User("avokat2").Id),
            [seedCase, extra1.Code, extra2.Code], other.Code);
    }

    /// <summary>As root: the migration test also reads ash_ops.migration_reports (the app account has no rights there).</summary>
    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new MySqlConnection(Db.RootConnectionString);
        await connection.OpenAsync(Ct);
        await connection.ChangeDatabaseAsync("ash_core", Ct);
        await using var command = new MySqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync(Ct);
        return value is DBNull ? null : value;
    }

    private async Task<long> Count(string sql) => Convert.ToInt64(await Scalar($"SELECT COUNT(*) {sql}"));

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.IsSuccessStatusCode.ShouldBeTrue(text);
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    private async Task<Guid> PublicIdOf(string table, long id) =>
        Ash.SharedKernel.Ids.PublicId.FromBytes((byte[])(await Scalar($"SELECT public_id FROM {table} WHERE id = {id}"))!);

    /// <summary>The codes of the user's "Çështjet e mia" (any status).</summary>
    private static async Task<List<string>> Mine(HttpClient client) =>
        (await Json(await client.GetAsync("/api/cases?tab=mine&status=ALL&limit=100", Ct))).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("caseCode").GetString()!).Order().ToList();

    private static async Task<Guid> LinkAsync(HttpClient kryetar, Guid assistant, Guid lawyer) =>
        (await Json(await kryetar.PostAsJsonAsync("/api/assistant-links",
            new { assistantUserPublicId = assistant, lawyerUserPublicId = lawyer }, Ct))).GetProperty("linkPublicId").GetGuid();

    // ---------------------------------------------------------------- link → the lawyer's primary cases

    [Fact]
    public async Task A_linked_assistant_gets_every_primary_case_of_the_lawyer_now_and_later_told_once()
    {
        await using var s = await ScenarioAsync();
        using var kryetar = s.As(Kryetar);
        using var assistant = s.As(s.AssistantEmail);
        (await Mine(assistant)).ShouldBeEmpty();

        var link = await Json(await kryetar.PostAsJsonAsync("/api/assistant-links",
            new { assistantUserPublicId = s.Assistant, lawyerUserPublicId = s.Avokat1 }, Ct));
        link.GetProperty("cases").GetInt32().ShouldBe(3);

        (await Mine(assistant)).ShouldBe(s.Avokat1Cases.Order().ToList()); // not avokat2's case
        (await Count($"FROM case_assignments WHERE user_id = {s.AssistantId} AND ended_at IS NULL AND case_role = 'LEGAL_ASSISTANT' AND assistant_link_id IS NOT NULL"))
            .ShouldBe(3L);
        (await Count($"FROM case_history_events h JOIN case_assignments a ON a.case_id = h.case_id AND a.user_id = {s.AssistantId} WHERE h.event_type = 'ASSISTANT_ADDED'"))
            .ShouldBe(3L);
        // told once for the link, not per case
        (await Count($"FROM notifications WHERE user_id = {s.AssistantId} AND notification_type = 'ASSISTANT_ASSIGNED'")).ShouldBe(1L);

        // a case assigned to avokat1 later joins at once (LawyerAssigned)
        var registered = await PublicIdOf("cases", s.Seed.RegisteredCaseId);
        var rowVersion = Convert.ToUInt32(await Scalar($"SELECT row_version FROM cases WHERE id = {s.Seed.RegisteredCaseId}"));
        await Json(await kryetar.PostAsJsonAsync($"/api/cases/{registered}/assignment",
            new { primaryUserPublicId = s.Avokat1, rowVersion }, Ct));
        (await Mine(assistant)).Count.ShouldBe(4);

        // the Kryetar's screen lists the link
        var screen = await Json(await kryetar.GetAsync("/api/assistant-links", Ct));
        var row = screen.GetProperty("assistants").EnumerateArray().Single(a => a.GetProperty("publicId").GetGuid() == s.Assistant);
        row.GetProperty("lawyers").EnumerateArray().Single().GetProperty("cases").GetInt32().ShouldBe(4);
    }

    [Fact]
    public async Task A_case_transferred_to_another_lawyer_leaves_the_assistant_and_follows_the_new_lawyers_assistant()
    {
        await using var s = await ScenarioAsync();
        using var kryetar = s.As(Kryetar);
        await LinkAsync(kryetar, s.Assistant, s.Avokat1);
        // ndihmes is linked to avokat2
        var ndihmes = await PublicIdOf("users", s.Seed.User("ndihmes").Id);
        await LinkAsync(kryetar, ndihmes, s.Avokat2);

        // transfer an avokat1 case to avokat2
        var moved = s.Avokat1Cases[1];
        var movedId = Convert.ToInt64(await Scalar($"SELECT id FROM cases WHERE case_code = '{moved}'"));
        var rowVersion = Convert.ToUInt32(await Scalar($"SELECT row_version FROM cases WHERE id = {movedId}"));
        await Json(await kryetar.PostAsJsonAsync($"/api/cases/{await PublicIdOf("cases", movedId)}/transfer",
            new { newPrimaryUserPublicId = s.Avokat2, reason = "Ngarkesë pune", rowVersion }, Ct));

        using var assistant = s.As(s.AssistantEmail);
        (await Mine(assistant)).ShouldNotContain(moved);
        (await Scalar($"SELECT end_reason FROM case_assignments WHERE case_id = {movedId} AND user_id = {s.AssistantId}")).ShouldBe("REMOVED");
        (await Count($"FROM case_assignments WHERE case_id = {movedId} AND user_id = {s.Seed.User("ndihmes").Id} AND ended_at IS NULL AND assistant_link_id IS NOT NULL"))
            .ShouldBe(1L);
    }

    [Fact]
    public async Task Unlinking_removes_every_case_of_that_lawyer_and_tells_the_assistant_once()
    {
        await using var s = await ScenarioAsync();
        using var kryetar = s.As(Kryetar);
        var link = await LinkAsync(kryetar, s.Assistant, s.Avokat1);

        (await kryetar.DeleteAsync($"/api/assistant-links/{link}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var assistant = s.As(s.AssistantEmail);
        (await Mine(assistant)).ShouldBeEmpty();
        (await Count($"FROM case_assignments WHERE user_id = {s.AssistantId} AND ended_at IS NULL")).ShouldBe(0L);
        (await Count($"FROM notifications WHERE user_id = {s.AssistantId} AND notification_type = 'ASSISTANT_REMOVED'")).ShouldBe(1L);
        (await kryetar.DeleteAsync($"/api/assistant-links/{link}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task The_nightly_reconcile_fixes_drift_and_changes_nothing_twice()
    {
        await using var s = await ScenarioAsync();
        using var kryetar = s.As(Kryetar);
        await LinkAsync(kryetar, s.Assistant, s.Avokat1);
        var dropped = s.Avokat1Cases[0];
        // drift: one row ended behind the sync's back, and a row left on avokat2's case (not a primary case of avokat1)
        await Scalar($"""
            UPDATE case_assignments SET ended_at = UTC_TIMESTAMP(3), end_reason = 'REMOVED'
             WHERE user_id = {s.AssistantId} AND case_id = (SELECT id FROM cases WHERE case_code = '{dropped}');
            INSERT INTO case_assignments (case_id, user_id, case_role, assigned_by, assistant_link_id)
            SELECT c.id, {s.AssistantId}, 'LEGAL_ASSISTANT', {s.Seed.User("kryetar").Id},
                   (SELECT id FROM assistant_lawyers WHERE assistant_user_id = {s.AssistantId} AND ended_at IS NULL)
              FROM cases c WHERE c.case_code = '{s.Avokat2Case}';
            """);
        using var assistant = s.As(s.AssistantEmail);
        (await Mine(assistant)).ShouldContain(s.Avokat2Case);

        var runner = s.Factory.Services.GetRequiredService<JobRunner>();
        await runner.RunAsync("assistant-reconcile", DateTime.UtcNow, Ct);

        (await Mine(assistant)).ShouldBe(s.Avokat1Cases.Order().ToList());
        var rows = await Count("FROM case_assignments");
        var history = await Count("FROM case_history_events");
        await runner.RunAsync("assistant-reconcile", DateTime.UtcNow, Ct);
        (await Count("FROM case_assignments")).ShouldBe(rows);
        (await Count("FROM case_history_events")).ShouldBe(history);
    }

    // ---------------------------------------------------------------- who may

    [Fact]
    public async Task Only_the_Kryetar_links_within_the_office_and_the_per_case_endpoints_are_gone()
    {
        await using var s = await ScenarioAsync();
        using var kryetar = s.As(Kryetar);
        using var primary = s.As("avokat1@ash.local");

        (await primary.GetAsync("/api/assistant-links", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await primary.PostAsJsonAsync("/api/assistant-links", new { assistantUserPublicId = s.Assistant, lawyerUserPublicId = s.Avokat1 }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        // task 026's per-case endpoints no longer exist; the primary lawyer no longer holds assistant.manage
        var caseId = await PublicIdOf("cases", s.Seed.ActiveCaseId);
        (await primary.PostAsJsonAsync($"/api/cases/{caseId}/assistants", new { userPublicId = s.Assistant }, Ct))
            .StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        (await Json(await primary.GetAsync($"/api/cases/{caseId}", Ct))).GetProperty("permissions").EnumerateArray()
            .Select(p => p.GetString()).ShouldNotContain("assistant.manage");

        // a lawyer of another office → 400; twice → 409
        var drLawyer = await PublicIdOf("users", s.Seed.User("drlawyer").Id);
        (await kryetar.PostAsJsonAsync("/api/assistant-links", new { assistantUserPublicId = s.Assistant, lawyerUserPublicId = drLawyer }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await LinkAsync(kryetar, s.Assistant, s.Avokat1);
        (await kryetar.PostAsJsonAsync("/api/assistant-links", new { assistantUserPublicId = s.Assistant, lawyerUserPublicId = s.Avokat1 }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    // ---------------------------------------------------------------- the migration of task 026's rows (script 024)

    [Fact]
    public async Task The_migration_links_same_office_rows_ends_the_others_reports_and_is_idempotent()
    {
        await using var s = await ScenarioAsync();
        // the seed's ndihmes row on the ACTIVE case is a task-026 row (no link): same office as avokat1 → kept, linked
        // + a Durrës assistant on the same case (another office) → ended; + ndihmes on the REGISTERED case (no primary) → ended
        await using var seed = await Seed.CreateAsync(Db.AppConnectionString);
        var drAssistant = await seed.UserAsync("drasst", "Dr", "Ndihmës", s.Seed.OfficeDrId, null, "LEGAL_ASSISTANT");
        var kryetarId = s.Seed.User("kryetar").Id;
        await Scalar($"""
            INSERT INTO case_assignments (case_id, user_id, case_role, assigned_by)
            VALUES ({s.Seed.ActiveCaseId}, {drAssistant}, 'LEGAL_ASSISTANT', {kryetarId}),
                   ({s.Seed.RegisteredCaseId}, {s.Seed.User("ndihmes").Id}, 'LEGAL_ASSISTANT', {kryetarId});
            """);

        await RunMigrationAsync();

        var ndihmes = s.Seed.User("ndihmes").Id;
        var link = await Scalar($"SELECT id FROM assistant_lawyers WHERE assistant_user_id = {ndihmes} AND lawyer_user_id = {s.Seed.User("avokat1").Id} AND ended_at IS NULL");
        link.ShouldNotBeNull();
        (await Scalar($"SELECT assistant_link_id FROM case_assignments WHERE case_id = {s.Seed.ActiveCaseId} AND user_id = {ndihmes} AND ended_at IS NULL"))
            .ShouldBe(link);
        (await Scalar($"SELECT end_reason FROM case_assignments WHERE case_id = {s.Seed.ActiveCaseId} AND user_id = {drAssistant}")).ShouldBe("REMOVED");
        (await Scalar($"SELECT end_reason FROM case_assignments WHERE case_id = {s.Seed.RegisteredCaseId} AND user_id = {ndihmes}")).ShouldBe("REMOVED");
        (await Count($"FROM case_history_events WHERE case_id = {s.Seed.ActiveCaseId} AND summary = 'Ndihmësi u kalua në lidhje me avokatin.'")).ShouldBe(1L);
        (await Count($"FROM case_history_events WHERE case_id = {s.Seed.ActiveCaseId} AND summary LIKE '%Migrim 053e: zyrë tjetër%'")).ShouldBe(1L);
        (await Count($"FROM case_history_events WHERE case_id = {s.Seed.RegisteredCaseId} AND summary LIKE '%Migrim 053e: pa avokat primar%'")).ShouldBe(1L);
        const string report = "FROM ash_ops.migration_reports WHERE script = '024_assistant_links_migration.sql'";
        (await Count($"{report} AND kind = 'LINK_CREATED'")).ShouldBe(1L);
        (await Count($"{report} AND kind = 'ROW_KEPT'")).ShouldBe(1L);
        (await Count($"{report} AND kind = 'ROW_ENDED'")).ShouldBe(2L);

        // twice: nothing changes
        var before = (await Count("FROM case_assignments WHERE ended_at IS NULL"), await Count("FROM case_history_events"),
            await Count("FROM assistant_lawyers"), await Count(report));
        await RunMigrationAsync();
        (await Count("FROM case_assignments WHERE ended_at IS NULL"), await Count("FROM case_history_events"),
            await Count("FROM assistant_lawyers"), await Count(report)).ShouldBe(before);
        await using var root = new MySqlConnection(Db.RootConnectionString);
        await root.OpenAsync(Ct);
        await using var clean = new MySqlCommand($"DELETE {report}", root);
        await clean.ExecuteNonQueryAsync(Ct);
    }

    /// <summary>Script 024 as the migrator runs it (the embedded resource), on the test database.</summary>
    private async Task RunMigrationAsync()
    {
        var assembly = typeof(Ash.Database.DatabaseUpgrader).Assembly;
        await using var stream = assembly.GetManifestResourceStream("Ash.Database.Scripts.024_assistant_links_migration.sql")!;
        using var reader = new StreamReader(stream);
        var sql = await reader.ReadToEndAsync(Ct);
        await using var connection = new MySqlConnection(Db.RootConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }
}
