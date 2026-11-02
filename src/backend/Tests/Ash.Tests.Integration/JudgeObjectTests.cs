using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.Tests.Integration.Infrastructure;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 028 — change the judge and the object, the old value kept in the history. Uses <see cref="CaseSeed"/>: an
/// ACTIVE case at Gjykata e Shkallës së Parë Tiranë (avokat1 primary, avokat2 secondary) and a REGISTERED case
/// registered by protokoll. Judges from LocalScripts/903.
/// </summary>
public class JudgeObjectTests(MySqlFixture db) : IntegrationTest(db)
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

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private async Task<Guid> PublicIdOf(string table, long id) =>
        Ash.SharedKernel.Ids.PublicId.FromBytes((byte[])(await Scalar($"SELECT public_id FROM {table} WHERE id = {id}"))!);

    private async Task<uint> RowVersion(long caseId) => Convert.ToUInt32(await Scalar($"SELECT row_version FROM cases WHERE id = {caseId}"));

    private async Task<long> JudgeId(string first, string last) =>
        Convert.ToInt64(await Scalar($"SELECT id FROM judges WHERE first_name = '{first}' AND last_name = '{last}'"));

    private async Task<HttpResponseMessage> ChangeJudge(HttpClient client, Guid id, long caseId, long judgeId, uint? rowVersion = null) =>
        await client.PatchAsJsonAsync($"/api/cases/{id}/judge", new { judgeId, rowVersion = rowVersion ?? await RowVersion(caseId) }, Ct);

    private async Task<HttpResponseMessage> ChangeObject(HttpClient client, Guid id, long caseId, string? objectText, uint? rowVersion = null) =>
        await client.PatchAsJsonAsync($"/api/cases/{id}/object", new { objectText, rowVersion = rowVersion ?? await RowVersion(caseId) }, Ct);

    [Fact]
    public async Task Primary_changes_the_judge_and_the_history_keeps_both_names()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var caseId = seed.ActiveCaseId;
        var id = await PublicIdOf("cases", caseId);
        await Scalar($"UPDATE cases SET judge_id = {await JudgeId("Ilir", "Gjyqtari")} WHERE id = {caseId}");
        await using var factory = new AshApiFactory(Db);
        using var primary = factory.CreateClient().AsUser("avokat1@ash.local");
        var rowVersion = await RowVersion(caseId);

        var response = await ChangeJudge(primary, id, caseId, await JudgeId("Klodiana", "Çela"), rowVersion);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var body = await Json(response);
        body.GetProperty("rowVersion").GetUInt32().ShouldBe(rowVersion + 1);
        body.GetProperty("judge").GetProperty("name").GetString().ShouldBe("Klodiana Çela");
        body.GetProperty("warnings").GetArrayLength().ShouldBe(0);
        var history = $"FROM case_history_events WHERE case_id = {caseId} AND event_type = 'JUDGE_CHANGED'";
        (await Scalar($"SELECT summary {history}")).ShouldBe("Gjyqtari u ndryshua: Ilir Gjyqtari → Klodiana Çela.");
        (await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.old.name')) {history}")).ShouldBe("Ilir Gjyqtari");
        (await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.new.name')) {history}")).ShouldBe("Klodiana Çela");
        // only changed fields are stored: both judges sit at the same court
        (await Scalar($"SELECT JSON_CONTAINS_PATH(details, 'one', '$.new.courtName') {history}")).ShouldBe(0L);
        (await Json(await primary.GetAsync($"/api/cases/{id}", Ct))).GetProperty("data").GetProperty("judgeName").GetString()
            .ShouldBe("Klodiana Çela");
        (await Scalar($"SELECT first_data_entry_at FROM cases WHERE id = {caseId}")).ShouldNotBeNull(); // the team's entry

        // the same judge again; a stale row version
        var same = await ChangeJudge(primary, id, caseId, await JudgeId("Klodiana", "Çela"));
        same.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(same)).GetProperty("title").GetString().ShouldBe("Nuk ka ndryshime.");
        var stale = await ChangeJudge(primary, id, caseId, await JudgeId("Arbër", "Dëshira"), await RowVersion(caseId) + 1);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Json(stale)).GetProperty("code").GetString().ShouldBe("case.changed");
    }

    [Fact]
    public async Task A_judge_of_another_court_is_allowed_with_a_warning_an_inactive_one_is_refused()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var caseId = seed.ActiveCaseId;
        var id = await PublicIdOf("cases", caseId);
        var courtId = await Scalar($"SELECT court_id FROM cases WHERE id = {caseId}");
        await Scalar($"INSERT INTO judges (first_name, last_name, court_id, is_active) VALUES ('Joaktiv', 'Test028', {courtId}, 0)");
        var inactive = await JudgeId("Joaktiv", "Test028");
        try
        {
            await using var factory = new AshApiFactory(Db);
            using var primary = factory.CreateClient().AsUser("avokat1@ash.local");

            var other = await ChangeJudge(primary, id, caseId, await JudgeId("Genc", "Muçaj")); // Gjykata e Apelit Tiranë

            other.StatusCode.ShouldBe(HttpStatusCode.OK);
            var warning = (await Json(other)).GetProperty("warnings").EnumerateArray().Single();
            warning.GetProperty("code").GetString().ShouldBe("judge.court_mismatch");
            warning.GetProperty("message").GetString()!.ShouldContain("Genc Muçaj");
            (await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.new.courtName')) FROM case_history_events WHERE case_id = {caseId} AND event_type = 'JUDGE_CHANGED'"))
                .ShouldBe("Gjykata e Apelit Tiranë");

            var refused = await ChangeJudge(primary, id, caseId, inactive);
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await Json(refused)).GetProperty("errors").TryGetProperty("judgeId", out _).ShouldBeTrue();
            (await ChangeJudge(primary, id, caseId, 999_999)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
        finally
        {
            await Scalar($"DELETE FROM judges WHERE id = {inactive}");
        }
    }

    [Fact]
    public async Task Secondary_changes_the_object_but_not_the_judge()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var caseId = seed.ActiveCaseId;
        var id = await PublicIdOf("cases", caseId);
        await using var factory = new AshApiFactory(Db);
        using var secondary = factory.CreateClient().AsUser("avokat2@ash.local");

        (await ChangeJudge(secondary, id, caseId, await JudgeId("Klodiana", "Çela"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var changed = await ChangeObject(secondary, id, caseId, "  Kërkesë për shpërblim dëmi\nnga shpronësimi  ");

        changed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Json(changed)).GetProperty("objectText").GetString().ShouldBe("Kërkesë për shpërblim dëmi\nnga shpronësimi");
        var history = $"FROM case_history_events WHERE case_id = {caseId} AND event_type = 'OBJECT_CHANGED'";
        (await Scalar($"SELECT summary {history}")).ShouldBe("Objekti u ndryshua.");
        (await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.old.objectText')) {history}")).ShouldBe("Test");
        (await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.new.objectText')) {history}"))
            .ShouldBe("Kërkesë për shpërblim dëmi\nnga shpronësimi");

        // unchanged (after trimming), empty, too long
        (await ChangeObject(secondary, id, caseId, "Kërkesë për shpërblim dëmi\nnga shpronësimi ")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ChangeObject(secondary, id, caseId, "   ")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ChangeObject(secondary, id, caseId, null)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var tooLong = await ChangeObject(secondary, id, caseId, new string('a', 4001));
        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(tooLong)).GetProperty("errors").TryGetProperty("objectText", out _).ShouldBeTrue();
        (await ChangeObject(secondary, id, caseId, new string('a', 4000))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Protokoll_fixes_judge_and_object_while_registered_only()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var caseId = seed.RegisteredCaseId;
        var id = await PublicIdOf("cases", caseId);
        await using var factory = new AshApiFactory(Db);
        using var protokoll = factory.CreateClient().AsUser("protokoll@ash.local");

        (await ChangeJudge(protokoll, id, caseId, await JudgeId("Klodiana", "Çela"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ChangeObject(protokoll, id, caseId, "Objekti i korrigjuar")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Scalar($"SELECT GROUP_CONCAT(event_type ORDER BY id) FROM case_history_events WHERE case_id = {caseId}"))
            .ShouldBe("JUDGE_CHANGED,OBJECT_CHANGED");
        // the registration desk is not the team: no first data entry
        (await Scalar($"SELECT first_data_entry_at FROM cases WHERE id = {caseId}")).ShouldBeNull();

        // once a lawyer has it, the case is no longer Protokoll's
        await Scalar($"UPDATE cases SET lifecycle_status = 'ACTIVE' WHERE id = {caseId}");
        (await ChangeJudge(protokoll, id, caseId, await JudgeId("Arbër", "Dëshira"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ChangeObject(protokoll, id, caseId, "Edhe një herë")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Closed_case_is_refused()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var caseId = seed.ActiveCaseId;
        var id = await PublicIdOf("cases", caseId);
        await Scalar($"UPDATE cases SET lifecycle_status = 'CLOSED', closed_at = UTC_TIMESTAMP(3), closed_by = {seed.User("avokat1").Id} WHERE id = {caseId}");
        await using var factory = new AshApiFactory(Db);
        using var primary = factory.CreateClient().AsUser("avokat1@ash.local");

        var judge = await ChangeJudge(primary, id, caseId, await JudgeId("Klodiana", "Çela"));
        var obj = await ChangeObject(primary, id, caseId, "Tjetër objekt");

        judge.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Json(judge)).GetProperty("code").GetString().ShouldBe("case.closed");
        obj.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Scalar($"SELECT COUNT(*) FROM case_history_events WHERE case_id = {caseId}")).ShouldBe(0L);
    }
}
