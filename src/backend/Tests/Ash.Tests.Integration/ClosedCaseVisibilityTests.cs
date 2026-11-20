using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.Tests.Integration.Infrastructure;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 054d (blocker: 2026-625265/1 "disappeared" after closing) — closing never removes a case from anyone's view:
/// everyone who had it while open keeps it, the Protokoll keeps the cases it registered, the header search finds
/// closed and archived cases, and "Vazhdo" finds a dossier by the code of any of its levels. Uses <see cref="CaseSeed"/>'s
/// ACTIVE case: avokat1 primary, avokat2 secondary, ndihmes assistant, jurist, the ministry linked (institucion),
/// registered by protokoll, office TR (kryetar).
/// </summary>
public class ClosedCaseVisibilityTests(MySqlFixture db) : IntegrationTest(db)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync(Ct);
        return value is DBNull ? null : value;
    }

    private async Task<Guid> PublicIdOf(string table, long id) =>
        Ash.SharedKernel.Ids.PublicId.FromBytes((byte[])(await Scalar($"SELECT public_id FROM {table} WHERE id = {id}"))!);

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<List<string>> Codes(HttpClient client, string query) =>
        (await Json(await client.GetAsync($"/api/cases?{query}&limit=100", Ct))).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("caseCode").GetString()!).ToList();

    private async Task CloseAsync(CaseSeed s) =>
        await Scalar($"UPDATE cases SET lifecycle_status = 'CLOSED', closed_at = UTC_TIMESTAMP(3), closed_by = {s.User("avokat1").Id} WHERE id = {s.ActiveCaseId}");

    [Theory]
    [InlineData("avokat1@ash.local", "mine")]
    [InlineData("avokat2@ash.local", "mine")]
    [InlineData("ndihmes@ash.local", "mine")]
    [InlineData("jurist@ash.local", "mine")]
    [InlineData("institucion@ash.local", "institution")]
    [InlineData("kryetar@ash.local", "office")]
    [InlineData("monitor@ash.local", "all")]
    [InlineData("protokoll@ash.local", "registered")]
    public async Task After_closing_every_role_on_the_case_still_lists_it(string user, string tab)
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var code = (string)(await Scalar($"SELECT case_code FROM cases WHERE id = {s.ActiveCaseId}"))!;
        await CloseAsync(s);
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(user);

        (await Codes(client, $"tab={tab}&status=CLOSED")).ShouldContain(code, user);
        // the header search too
        var search = await Json(await client.GetAsync($"/api/search?q={Uri.EscapeDataString(code)}", Ct));
        if (tab != "registered")
        {
            search.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("caseCode").GetString()).ShouldContain(code, user);
        }
    }

    [Fact]
    public async Task An_archived_case_is_still_listed_and_found_by_the_header_search()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var code = (string)(await Scalar($"SELECT case_code FROM cases WHERE id = {s.ActiveCaseId}"))!;
        await CloseAsync(s);
        await Scalar($"UPDATE dossiers SET archived_at = UTC_TIMESTAMP(3), archived_by = {s.User("avokat1").Id} WHERE id = {s.ActiveDossierId}");
        try
        {
            await using var factory = new AshApiFactory(Db);
            using var lawyer = factory.CreateClient().AsUser("avokat1@ash.local");
            (await Codes(lawyer, "tab=mine&status=CLOSED")).ShouldContain(code);
            var search = await Json(await lawyer.GetAsync($"/api/search?q={Uri.EscapeDataString(code)}", Ct));
            var hit = search.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("caseCode").GetString() == code);
            hit.GetProperty("isArchived").GetBoolean().ShouldBeTrue();
        }
        finally
        {
            await Scalar($"UPDATE dossiers SET archived_at = NULL, archived_by = NULL WHERE id = {s.ActiveDossierId}");
        }
    }

    [Fact]
    public async Task The_Protokoll_keeps_its_open_cases_after_assignment_and_opens_them_at_registration_level()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var active = (string)(await Scalar($"SELECT case_code FROM cases WHERE id = {s.ActiveCaseId}"))!;
        var registered = (string)(await Scalar($"SELECT case_code FROM cases WHERE id = {s.RegisteredCaseId}"))!;
        await using var factory = new AshApiFactory(Db);
        using var protokoll = factory.CreateClient().AsUser("protokoll@ash.local");

        // "Të regjistruarat nga unë" = the open ones: REGISTERED and ACTIVE (assigned by the Kryetar)
        var mine = await Codes(protokoll, "tab=registered");
        mine.ShouldContain(active);
        mine.ShouldContain(registered);
        // the ACTIVE one is not a case view for the Protokoll (CaseAccessService unchanged) ...
        var caseId = await PublicIdOf("cases", s.ActiveCaseId);
        (await protokoll.GetAsync($"/api/cases/{caseId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        // ... but its registration summary is
        var summary = await Json(await protokoll.GetAsync($"/api/cases/{caseId}/registration-summary", Ct));
        summary.GetProperty("caseCode").GetString().ShouldBe(active);
        summary.GetProperty("status").GetString().ShouldBe("ACTIVE");
        // not for someone else's case, and not for a user without case.create
        using var lawyer = factory.CreateClient().AsUser("avokat1@ash.local");
        (await lawyer.GetAsync($"/api/cases/{caseId}/registration-summary", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var otherOne = await s.AddOfficeUserAsync("ProtokollTjeter", s.OfficeTrId, "PROTOCOL");
        using var other = factory.CreateClient().AsUser(otherOne);
        (await other.GetAsync($"/api/cases/{caseId}/registration-summary", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Vazhdo_finds_the_dossier_by_the_code_of_an_older_level_and_shows_the_latest_level()
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var x1 = (string)(await Scalar($"SELECT case_code FROM cases WHERE id = {s.ActiveCaseId}"))!;
        await CloseAsync(s);
        // X/2, open, in the same dossier
        await Scalar($"""
            INSERT INTO cases (dossier_id, level_seq, case_code, office_id, summons_number, act_number, received_date,
                               court_level_id, court_id, object_text, lifecycle_status, registered_by)
            SELECT dossier_id, 2, CONCAT(LEFT(case_code, CHAR_LENGTH(case_code) - 2), '/2'), office_id, 'FL-X2', 'AKT-X2',
                   received_date, court_level_id, court_id, object_text, 'ACTIVE', registered_by
              FROM cases WHERE id = {s.ActiveCaseId}
            """);
        var x2 = x1[..^2] + "/2";
        try
        {
            await using var factory = new AshApiFactory(Db);
            using var protokoll = factory.CreateClient().AsUser("protokoll@ash.local");

            // no status chosen: a search looks in every status — the dossier, at X/2 (open)
            var found = await Json(await protokoll.GetAsync($"/api/cases/registration-search?code={Uri.EscapeDataString(x1)}", Ct));
            found.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("caseCode").GetString()).ShouldBe([x2]);
            // "Mund të vazhdohen" chosen explicitly: X/2 is open, nothing to continue
            var continuable = await Json(await protokoll.GetAsync($"/api/cases/registration-search?code={Uri.EscapeDataString(x1)}&status=CONTINUABLE", Ct));
            continuable.GetProperty("items").GetArrayLength().ShouldBe(0);
        }
        finally
        {
            await Scalar($"DELETE FROM cases WHERE dossier_id = {s.ActiveDossierId} AND level_seq = 2");
        }
    }
}
