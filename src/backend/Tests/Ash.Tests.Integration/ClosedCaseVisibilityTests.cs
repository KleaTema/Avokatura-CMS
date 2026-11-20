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

        // task 054e: in "Të mbyllura", not in the open tab any more
        (await Codes(client, "tab=closed")).ShouldContain(code, user);
        (await Codes(client, $"tab={tab}")).ShouldNotContain(code, user);
        // the old explicit status still answers (links)
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
            // "Të mbyllura": the badge, and its two choices split by the archive
            var closed = await Json(await lawyer.GetAsync("/api/cases?tab=closed&limit=100", Ct));
            closed.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("caseCode").GetString() == code)
                .GetProperty("isArchived").GetBoolean().ShouldBeTrue();
            (await Codes(lawyer, "tab=closed&status=ARCHIVED")).ShouldContain(code);
            (await Codes(lawyer, "tab=closed&status=CLOSED")).ShouldNotContain(code);
            var search = await Json(await lawyer.GetAsync($"/api/search?q={Uri.EscapeDataString(code)}", Ct));
            var hit = search.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("caseCode").GetString() == code);
            hit.GetProperty("isArchived").GetBoolean().ShouldBeTrue();
        }
        finally
        {
            await Scalar($"UPDATE dossiers SET archived_at = NULL, archived_by = NULL WHERE id = {s.ActiveDossierId}");
        }
    }

    [Theory]
    [InlineData("avokat1@ash.local", "mine,closed")]
    [InlineData("avokat2@ash.local", "mine,closed")]
    [InlineData("ndihmes@ash.local", "mine,closed")]
    [InlineData("jurist@ash.local", "mine,closed")]
    [InlineData("institucion@ash.local", "institution,closed")]
    [InlineData("kryetar@ash.local", "unassigned,office,supervised,closed")]
    [InlineData("monitor@ash.local", "all,closed")]
    [InlineData("protokoll@ash.local", "registered,closed")]
    public async Task Every_role_gets_Te_mbyllura_after_its_lists_with_a_count_newest_closed_first(string user, string expected)
    {
        await using var s = await CaseSeed.CreateAsync(Db.AppConnectionString);
        await CloseAsync(s);
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(user);
        var tabs = (await Json(await client.GetAsync("/api/cases/tabs", Ct))).GetProperty("tabs").EnumerateArray().ToList();
        string.Join(',', tabs.Select(t => t.GetProperty("tab").GetString())).ShouldBe(expected, user);

        // the count = what the tab lists; sorted by closing date, newest first
        var count = tabs.Single(t => t.GetProperty("tab").GetString() == "closed").GetProperty("count").GetInt32();
        var page = await Json(await client.GetAsync("/api/cases?tab=closed&limit=100", Ct));
        page.GetProperty("total").GetInt32().ShouldBe(count);
        var closedAt = page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("closedAt").GetDateTime()).ToList();
        closedAt.ShouldBe(closedAt.OrderByDescending(x => x).ToList());
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
