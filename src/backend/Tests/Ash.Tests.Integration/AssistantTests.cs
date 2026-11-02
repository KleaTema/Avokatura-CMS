using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.Tests.Integration.Infrastructure;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 026 — the primary lawyer adds and removes legal assistants. Uses <see cref="CaseSeed"/>'s ACTIVE case (avokat1
/// primary, avokat2 secondary, ndihmes already an assistant) plus two more LEGAL_ASSISTANTs of office TR.
/// </summary>
public class AssistantTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string Primary = "avokat1@ash.local";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Scenario(CaseSeed Seed, Guid Case, long CaseId, string CaseCode, Guid Asst2, string Asst2Email, Guid Asst3);

    private async Task<Scenario> ScenarioAsync(CaseSeed s)
    {
        await using var seed = await Seed.CreateAsync(Db.AppConnectionString);
        var a2 = await seed.UserAsync("asst2", "Ndihmës", "Dy", s.OfficeTrId, null, "LEGAL_ASSISTANT");
        var a3 = await seed.UserAsync("asst3", "Ndihmës", "Tre", s.OfficeTrId, null, "LEGAL_ASSISTANT");
        return new Scenario(s, await PublicIdOf("cases", s.ActiveCaseId), s.ActiveCaseId,
            (string)(await Scalar($"SELECT case_code FROM cases WHERE id = {s.ActiveCaseId}"))!,
            await PublicIdOf("users", a2), $"asst2-{seed.Suffix}@ash.test", await PublicIdOf("users", a3));
    }

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

    private async Task<HttpResponseMessage> Add(HttpClient client, Scenario s, Guid user, uint? rowVersion = null) =>
        await client.PostAsJsonAsync($"/api/cases/{s.Case}/assistants",
            new { userPublicId = user, rowVersion = rowVersion ?? await RowVersion(s.CaseId) }, Ct);

    private async Task<HttpResponseMessage> Remove(HttpClient client, Scenario s, Guid user, uint? rowVersion = null) =>
        await client.DeleteAsync($"/api/cases/{s.Case}/assistants/{user}?rowVersion={rowVersion ?? await RowVersion(s.CaseId)}", Ct);

    private Task<object?> CurrentAssistants(long caseId) => Scalar($"""
        SELECT COUNT(*) FROM case_assignments WHERE case_id = {caseId} AND case_role = 'LEGAL_ASSISTANT' AND ended_at IS NULL
        """);

    private static async Task<List<string>> Mine(HttpClient client)
    {
        var response = await client.GetAsync("/api/cases?tab=mine&limit=100", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await Json(response)).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("caseCode").GetString()!).ToList();
    }

    [Fact]
    public async Task Added_assistant_sees_the_case_and_loses_it_when_removed()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var s = await ScenarioAsync(seed);
        await using var factory = new AshApiFactory(Db);
        using var primary = factory.CreateClient().AsUser(Primary);
        using var assistant = factory.CreateClient().AsUser(s.Asst2Email);
        var rowVersion = await RowVersion(s.CaseId);

        var added = await Add(primary, s, s.Asst2, rowVersion);

        added.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await Json(added);
        JsonAssert.NoNumericIds(body);
        body.GetProperty("rowVersion").GetUInt32().ShouldBe(rowVersion + 1);
        body.GetProperty("assistant").GetProperty("name").GetString().ShouldBe("Ndihmës Dy");
        (await Mine(assistant)).ShouldContain(s.CaseCode);
        (await assistant.GetAsync($"/api/cases/{s.Case}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var history = $"FROM case_history_events WHERE case_id = {s.CaseId}";
        (await Scalar($"SELECT summary {history} AND event_type = 'ASSISTANT_ADDED'")).ShouldBe("Ndihmësi ligjor u shtua: Ndihmës Dy.");
        (await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.new.userPublicId')) {history} AND event_type = 'ASSISTANT_ADDED'"))
            .ShouldBe(s.Asst2.ToString());

        // chained with the returned row version
        var removed = await Remove(primary, s, s.Asst2, body.GetProperty("rowVersion").GetUInt32());

        removed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Json(removed)).GetProperty("rowVersion").GetUInt32().ShouldBe(rowVersion + 2);
        (await Scalar($"""
            SELECT CONCAT(a.end_reason, ':', a.ended_by = {seed.User("avokat1").Id})
            FROM case_assignments a JOIN users u ON u.id = a.user_id
            WHERE a.case_id = {s.CaseId} AND u.public_id = UUID_TO_BIN('{s.Asst2}', 1)
            """)).ShouldBe("REMOVED:1");
        (await assistant.GetAsync($"/api/cases/{s.Case}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Mine(assistant)).ShouldNotContain(s.CaseCode);
        (await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.old.name')) {history} AND event_type = 'ASSISTANT_REMOVED'"))
            .ShouldBe("Ndihmës Dy");

        // organising the team is not a data entry
        (await Scalar($"SELECT first_data_entry_at FROM cases WHERE id = {s.CaseId}")).ShouldBeNull();
        // removing someone who is not an assistant here
        (await Remove(primary, s, s.Asst2)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Several_assistants_are_allowed_and_the_picker_shows_who_can_be_added()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var s = await ScenarioAsync(seed);
        await using var factory = new AshApiFactory(Db);
        using var primary = factory.CreateClient().AsUser(Primary);

        var picker = await Json(await primary.GetAsync($"/api/cases/{s.Case}/assignable-assistants", Ct));
        var offered = picker.EnumerateArray().ToDictionary(x => x.GetProperty("publicId").GetGuid(), x => x.GetProperty("activeAssignments").GetInt32());
        offered.ShouldContainKey(s.Asst2);
        offered.ShouldContainKey(s.Asst3);
        offered.ShouldNotContainKey(await PublicIdOf("users", seed.User("ndihmes").Id)); // already on the case
        offered[s.Asst2].ShouldBe(0);

        (await Add(primary, s, s.Asst2)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Add(primary, s, s.Asst3)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await CurrentAssistants(s.CaseId)).ShouldBe(3L); // ndihmes + two
        var after = (await Json(await primary.GetAsync($"/api/cases/{s.Case}/assignable-assistants", Ct))).EnumerateArray()
            .Select(x => x.GetProperty("publicId").GetGuid()).ToList();
        after.ShouldNotContain(s.Asst2);
        after.ShouldNotContain(s.Asst3);
    }

    [Fact]
    public async Task A_user_with_a_role_on_the_case_cannot_be_added_and_others_are_refused()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var s = await ScenarioAsync(seed);
        await using var factory = new AshApiFactory(Db);
        using var primary = factory.CreateClient().AsUser(Primary);

        // the secondary lawyer, and the assistant already on the case
        foreach (var key in new[] { "avokat2", "ndihmes" })
        {
            var refused = await Add(primary, s, await PublicIdOf("users", seed.User(key).Id));
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, key);
            (await Json(refused)).GetProperty("errors").GetProperty("userPublicId")[0].GetString()
                .ShouldBe("Ky përdorues ka tashmë një rol në këtë çështje.");
        }

        // not a legal assistant of this office
        (await Add(primary, s, await PublicIdOf("users", seed.User("drlawyer").Id))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Add(primary, s, await PublicIdOf("users", seed.User("kryetar").Id))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // stale row version, on add and on remove
        var stale = await Add(primary, s, s.Asst2, await RowVersion(s.CaseId) + 1);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Json(stale)).GetProperty("code").GetString().ShouldBe("case.changed");
        var ndihmes = await PublicIdOf("users", seed.User("ndihmes").Id);
        (await Remove(primary, s, ndihmes, await RowVersion(s.CaseId) + 1)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        // no rowVersion at all
        (await primary.DeleteAsync($"/api/cases/{s.Case}/assistants/{ndihmes}", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await CurrentAssistants(s.CaseId)).ShouldBe(1L);
        (await Scalar($"SELECT COUNT(*) FROM case_history_events WHERE case_id = {s.CaseId}")).ShouldBe(0L);
    }

    [Fact]
    public async Task Only_the_primary_manages_assistants()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var s = await ScenarioAsync(seed);
        await using var factory = new AshApiFactory(Db);
        var ndihmes = await PublicIdOf("users", seed.User("ndihmes").Id);

        foreach (var email in new[] { "avokat2@ash.local", "kryetar@ash.local", "ndihmes@ash.local" })
        {
            using var client = factory.CreateClient().AsUser(email);
            (await Add(client, s, s.Asst2)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, email);
            (await Remove(client, s, ndihmes)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, email);
            (await client.GetAsync($"/api/cases/{s.Case}/assignable-assistants", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, email);
        }

        using var drLawyer = factory.CreateClient().AsUser((string)(await Scalar($"SELECT email FROM users WHERE id = {seed.User("drlawyer").Id}"))!);
        (await Add(drLawyer, s, s.Asst2)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await Scalar($"UPDATE cases SET lifecycle_status = 'CLOSED', closed_at = UTC_TIMESTAMP(3), closed_by = {seed.User("avokat1").Id} WHERE id = {s.CaseId}");
        using var primary = factory.CreateClient().AsUser(Primary);
        var closed = await Add(primary, s, s.Asst2);
        closed.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Json(closed)).GetProperty("code").GetString().ShouldBe("case.closed");

        (await CurrentAssistants(s.CaseId)).ShouldBe(1L);
    }
}
