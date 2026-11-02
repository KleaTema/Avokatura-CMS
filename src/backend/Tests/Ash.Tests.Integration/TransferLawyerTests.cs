using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.Modules.Cases.Contracts.Events;
using Ash.SharedKernel.Events;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 024 — the Kryetar transfers the primary lawyer and changes the secondary. Uses <see cref="CaseSeed"/>'s ACTIVE
/// case of office TR (avokat1 primary, avokat2 secondary, ndihmes legal assistant, the ministry's jurist) plus a third
/// STATE_LAWYER of TR ("Avokat Tre").
/// </summary>
public class TransferLawyerTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string Kryetar = "kryetar@ash.local";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class TransferRecorder
    {
        public ConcurrentQueue<LawyerTransferred> Events { get; } = new();
    }

    private sealed class TransferHandler(TransferRecorder recorder) : IEventHandler<LawyerTransferred>
    {
        public Task HandleAsync(LawyerTransferred @event, CancellationToken cancellationToken = default)
        {
            recorder.Events.Enqueue(@event);
            return Task.CompletedTask;
        }
    }

    private sealed record Scenario(CaseSeed Seed, Guid Case, long CaseId, string CaseCode, long ThirdId, string ThirdEmail)
    {
        public long Id(string key) => Seed.User(key).Id;
    }

    private async Task<Scenario> ScenarioAsync(CaseSeed s)
    {
        await using var seed = await Seed.CreateAsync(Db.AppConnectionString);
        var third = await seed.UserAsync("av3", "Avokat", "Tre", s.OfficeTrId, null, "STATE_LAWYER");
        return new Scenario(s, await PublicIdOf("cases", s.ActiveCaseId), s.ActiveCaseId,
            (string)(await Scalar($"SELECT case_code FROM cases WHERE id = {s.ActiveCaseId}"))!, third, $"av3-{seed.Suffix}@ash.test");
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

    private static string TransferPath(Guid casePublicId) => $"/api/cases/{casePublicId}/transfer";

    /// <summary>"email:ROLE" of the current rows, ordered by role.</summary>
    private Task<object?> Current(long caseId) => Scalar($"""
        SELECT GROUP_CONCAT(CONCAT(SUBSTRING_INDEX(u.email, '-', 1), ':', a.case_role) ORDER BY a.case_role)
        FROM case_assignments a JOIN users u ON u.id = a.user_id WHERE a.case_id = {caseId} AND a.ended_at IS NULL
        """);

    /// <summary>"email:ROLE:REASON" of the ended rows, ordered by role.</summary>
    private Task<object?> Ended(long caseId) => Scalar($"""
        SELECT GROUP_CONCAT(CONCAT(SUBSTRING_INDEX(u.email, '-', 1), ':', a.case_role, ':', a.end_reason) ORDER BY a.case_role)
        FROM case_assignments a JOIN users u ON u.id = a.user_id WHERE a.case_id = {caseId} AND a.ended_at IS NOT NULL
        """);

    private Task<object?> Events(long caseId) =>
        Scalar($"SELECT GROUP_CONCAT(event_type ORDER BY id) FROM case_history_events WHERE case_id = {caseId}");

    private async Task<HttpResponseMessage> Transfer(HttpClient client, Scenario s, object body) =>
        await client.PostAsJsonAsync(TransferPath(s.Case), body, Ct);

    private static async Task<List<string>> TabCodes(HttpClient client, string tab)
    {
        var response = await client.GetAsync($"/api/cases?tab={tab}&limit=100", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, tab);
        return (await Json(response)).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("caseCode").GetString()!).ToList();
    }

    // ---------------------------------------------------------------- replace the primary

    [Fact]
    public async Task Replacing_the_primary_moves_access_and_keeps_secondary_and_assistant()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var s = await ScenarioAsync(seed);
        // done before the transfer: the new lawyer must see it
        await Scalar($"""
            INSERT INTO case_history_events (dossier_id, case_id, event_type, actor_user_id, summary)
            VALUES ({seed.ActiveDossierId}, {s.CaseId}, 'HEARING_ADDED', {s.Id("avokat1")}, 'Seanca e parë u shtua.')
            """);
        var recorder = new TransferRecorder();
        await using var factory = new AshApiFactory(Db, configureServices: services =>
        {
            services.AddSingleton(recorder);
            services.AddScoped<IEventHandler<LawyerTransferred>, TransferHandler>();
        });
        using var kryetar = factory.CreateClient().AsUser(Kryetar);
        using var oldLawyer = factory.CreateClient().AsUser("avokat1@ash.local");
        using var newLawyer = factory.CreateClient().AsUser(s.ThirdEmail);
        (await TabCodes(oldLawyer, "mine")).ShouldContain(s.CaseCode);
        var third = await PublicIdOf("users", s.ThirdId);
        var rowVersion = await RowVersion(s.CaseId);

        var response = await Transfer(kryetar, s, new { newPrimaryUserPublicId = third, reason = "  Ngarkesë pune  ", rowVersion });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await Json(response);
        JsonAssert.NoNumericIds(result);
        result.GetProperty("primary").GetProperty("name").GetString().ShouldBe("Avokat Tre");
        result.GetProperty("secondary").GetProperty("name").GetString().ShouldBe("Avokat2 Dev");
        result.GetProperty("rowVersion").GetUInt32().ShouldBe(rowVersion + 1);

        (await Current(s.CaseId)).ShouldBe("jurist@ash.local:INSTITUTION_JURIST,ndihmes@ash.local:LEGAL_ASSISTANT,av3:PRIMARY_LAWYER,avokat2@ash.local:SECONDARY_LAWYER");
        // the 'Transferuar' statistic: exactly one ended PRIMARY row, TRANSFERRED, ended by the Kryetar
        (await Ended(s.CaseId)).ShouldBe("avokat1@ash.local:PRIMARY_LAWYER:TRANSFERRED");
        (await Scalar($"SELECT ended_by FROM case_assignments WHERE case_id = {s.CaseId} AND end_reason = 'TRANSFERRED'"))
            .ShouldBe((uint)s.Id("kryetar"));
        ((DateTime)(await Scalar($"SELECT assigned_at FROM cases WHERE id = {s.CaseId}"))!).ShouldBe(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        (await Scalar($"SELECT row_version FROM cases WHERE id = {s.CaseId}")).ShouldBe(rowVersion + 1);

        var history = $"FROM case_history_events WHERE case_id = {s.CaseId} AND event_type = 'LAWYER_TRANSFERRED'";
        (await Events(s.CaseId)).ShouldBe("HEARING_ADDED,LAWYER_TRANSFERRED");
        (await Scalar($"SELECT summary {history}")).ShouldBe("Avokati primar u ndryshua: Avokat1 Dev → Avokat Tre.");
        (await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.old.name')) {history}")).ShouldBe("Avokat1 Dev");
        (await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.old.userPublicId')) {history}"))
            .ShouldBe((await PublicIdOf("users", s.Id("avokat1"))).ToString());
        (await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.new.name')) {history}")).ShouldBe("Avokat Tre");
        (await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.new.userPublicId')) {history}")).ShouldBe(third.ToString());
        (await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.new.reason')) {history}")).ShouldBe("Ngarkesë pune");

        var announced = recorder.Events.ShouldHaveSingleItem();
        announced.OldPrimaryUserPublicId.ShouldBe(await PublicIdOf("users", s.Id("avokat1")));
        announced.NewPrimaryUserPublicId.ShouldBe(third);
        announced.OldSecondaryUserPublicId.ShouldBe(announced.NewSecondaryUserPublicId);

        // the old lawyer loses access at once; the new one sees the case and its whole history
        (await oldLawyer.GetAsync($"/api/cases/{s.Case}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TabCodes(oldLawyer, "mine")).ShouldNotContain(s.CaseCode);
        (await newLawyer.GetAsync($"/api/cases/{s.Case}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TabCodes(newLawyer, "mine")).ShouldContain(s.CaseCode);
        var seen = (await Json(await newLawyer.GetAsync($"/api/cases/{s.Case}/history?limit=50", Ct)))
            .GetProperty("items").EnumerateArray().Select(h => h.GetProperty("eventType").GetString()).ToList();
        seen.ShouldBe(["LAWYER_TRANSFERRED", "HEARING_ADDED"]);
        // the assistant stays on the case
        using var assistant = factory.CreateClient().AsUser("ndihmes@ash.local");
        (await assistant.GetAsync($"/api/cases/{s.Case}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---------------------------------------------------------------- combinations

    [Fact]
    public async Task Secondary_moves_up_to_primary_and_the_slot_is_empty()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var s = await ScenarioAsync(seed);
        await using var factory = new AshApiFactory(Db);
        using var kryetar = factory.CreateClient().AsUser(Kryetar);

        var response = await Transfer(kryetar, s, new
        {
            newPrimaryUserPublicId = await PublicIdOf("users", s.Id("avokat2")),
            rowVersion = await RowVersion(s.CaseId)
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Json(response)).GetProperty("secondary").ValueKind.ShouldBe(JsonValueKind.Null);
        (await Current(s.CaseId)).ShouldBe("jurist@ash.local:INSTITUTION_JURIST,ndihmes@ash.local:LEGAL_ASSISTANT,avokat2@ash.local:PRIMARY_LAWYER");
        (await Ended(s.CaseId)).ShouldBe("avokat1@ash.local:PRIMARY_LAWYER:TRANSFERRED,avokat2@ash.local:SECONDARY_LAWYER:REMOVED");
        (await Events(s.CaseId)).ShouldBe("LAWYER_TRANSFERRED,SECONDARY_REMOVED");
        (await Scalar($"SELECT summary FROM case_history_events WHERE case_id = {s.CaseId} AND event_type = 'SECONDARY_REMOVED'"))
            .ShouldBe("Avokat2 Dev nuk është më avokat sekondar: u bë avokat primar.");
    }

    [Fact]
    public async Task Primary_and_secondary_swap_places()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var s = await ScenarioAsync(seed);
        await using var factory = new AshApiFactory(Db);
        using var kryetar = factory.CreateClient().AsUser(Kryetar);

        // the secondary becomes primary and the old primary becomes the secondary
        var response = await Transfer(kryetar, s, new
        {
            newPrimaryUserPublicId = await PublicIdOf("users", s.Id("avokat2")),
            secondary = new { action = "set", userPublicId = await PublicIdOf("users", s.Id("avokat1")) },
            rowVersion = await RowVersion(s.CaseId)
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Current(s.CaseId)).ShouldBe(
            "jurist@ash.local:INSTITUTION_JURIST,ndihmes@ash.local:LEGAL_ASSISTANT,avokat2@ash.local:PRIMARY_LAWYER,avokat1@ash.local:SECONDARY_LAWYER");
        (await Ended(s.CaseId)).ShouldBe("avokat1@ash.local:PRIMARY_LAWYER:TRANSFERRED,avokat2@ash.local:SECONDARY_LAWYER:REMOVED");
        (await Events(s.CaseId)).ShouldBe("LAWYER_TRANSFERRED,SECONDARY_ASSIGNED");
        (await Scalar($"SELECT summary FROM case_history_events WHERE case_id = {s.CaseId} AND event_type = 'SECONDARY_ASSIGNED'"))
            .ShouldBe("Avokati sekondar u ndryshua: Avokat2 Dev → Avokat1 Dev.");
        // the old primary keeps access as the secondary
        using var oldPrimary = factory.CreateClient().AsUser("avokat1@ash.local");
        (await oldPrimary.GetAsync($"/api/cases/{s.Case}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Secondary_is_removed_and_set_again_without_touching_the_primary()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var s = await ScenarioAsync(seed);
        await using var factory = new AshApiFactory(Db);
        using var kryetar = factory.CreateClient().AsUser(Kryetar);
        var assignedAt = await Scalar($"SELECT assigned_at FROM cases WHERE id = {s.CaseId}");

        var removed = await Transfer(kryetar, s, new { secondary = new { action = "remove" }, rowVersion = await RowVersion(s.CaseId) });

        removed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Current(s.CaseId)).ShouldBe("jurist@ash.local:INSTITUTION_JURIST,ndihmes@ash.local:LEGAL_ASSISTANT,avokat1@ash.local:PRIMARY_LAWYER");
        (await Ended(s.CaseId)).ShouldBe("avokat2@ash.local:SECONDARY_LAWYER:REMOVED");
        (await Events(s.CaseId)).ShouldBe("SECONDARY_REMOVED");
        (await Scalar($"SELECT assigned_at FROM cases WHERE id = {s.CaseId}")).ShouldBe(assignedAt); // the primary did not change
        using var avokat2 = factory.CreateClient().AsUser("avokat2@ash.local");
        (await avokat2.GetAsync($"/api/cases/{s.Case}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // a case without a secondary gets one
        var set = await Transfer(kryetar, s, new
        {
            secondary = new { action = "set", userPublicId = await PublicIdOf("users", s.ThirdId) },
            rowVersion = await RowVersion(s.CaseId)
        });

        set.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Json(set)).GetProperty("secondary").GetProperty("name").GetString().ShouldBe("Avokat Tre");
        (await Current(s.CaseId)).ShouldBe(
            "jurist@ash.local:INSTITUTION_JURIST,ndihmes@ash.local:LEGAL_ASSISTANT,avokat1@ash.local:PRIMARY_LAWYER,av3:SECONDARY_LAWYER");
        (await Events(s.CaseId)).ShouldBe("SECONDARY_REMOVED,SECONDARY_ASSIGNED");
        (await Scalar($"SELECT summary FROM case_history_events WHERE case_id = {s.CaseId} AND event_type = 'SECONDARY_ASSIGNED'"))
            .ShouldBe("Avokati sekondar u caktua: Avokat Tre.");
        (await Scalar($"SELECT COUNT(*) FROM case_assignments WHERE case_id = {s.CaseId} AND end_reason = 'TRANSFERRED'")).ShouldBe(0L);
    }

    // ---------------------------------------------------------------- refusals

    [Fact]
    public async Task Stale_row_version_registered_and_closed_cases_are_409()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var s = await ScenarioAsync(seed);
        await using var factory = new AshApiFactory(Db);
        using var kryetar = factory.CreateClient().AsUser(Kryetar);
        var third = await PublicIdOf("users", s.ThirdId);

        var stale = await Transfer(kryetar, s, new { newPrimaryUserPublicId = third, rowVersion = await RowVersion(s.CaseId) + 1 });
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await Json(stale);
        problem.GetProperty("code").GetString().ShouldBe("case.changed");
        problem.GetProperty("title").GetString().ShouldBe("Të dhënat u ndryshuan nga një përdorues tjetër.");
        (await Ended(s.CaseId)).ShouldBeNull();

        var registered = await kryetar.PostAsJsonAsync(TransferPath(await PublicIdOf("cases", seed.RegisteredCaseId)),
            new { newPrimaryUserPublicId = third, rowVersion = await RowVersion(seed.RegisteredCaseId) }, Ct);
        registered.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var notAssigned = await Json(registered);
        notAssigned.GetProperty("code").GetString().ShouldBe("case.not_assigned");
        notAssigned.GetProperty("title").GetString().ShouldBe("Çështja nuk ka ende avokat. Përdorni caktimin e avokatit.");

        await Scalar($"UPDATE cases SET lifecycle_status = 'CLOSED', closed_at = UTC_TIMESTAMP(3), closed_by = {s.Id("kryetar")} WHERE id = {s.CaseId}");
        var closed = await Transfer(kryetar, s, new { newPrimaryUserPublicId = third, rowVersion = await RowVersion(s.CaseId) });
        closed.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Json(closed)).GetProperty("code").GetString().ShouldBe("case.closed");
        (await Ended(s.CaseId)).ShouldBeNull();
        (await Events(s.CaseId)).ShouldBeNull();
    }

    [Fact]
    public async Task Invalid_transfers_are_400_and_change_nothing()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var s = await ScenarioAsync(seed);
        await using var factory = new AshApiFactory(Db);
        using var kryetar = factory.CreateClient().AsUser(Kryetar);
        var rowVersion = await RowVersion(s.CaseId);
        var avokat1 = await PublicIdOf("users", s.Id("avokat1"));
        var third = await PublicIdOf("users", s.ThirdId);

        async Task<JsonElement> BadRequest(object body)
        {
            var response = await Transfer(kryetar, s, body);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            return await Json(response);
        }

        // no change at all: nothing sent, keep, or the secondary set to who it already is
        (await BadRequest(new { rowVersion })).GetProperty("code").GetString().ShouldBe("transfer.no_change");
        (await BadRequest(new { secondary = new { action = "keep" }, rowVersion })).GetProperty("code").GetString().ShouldBe("transfer.no_change");
        (await BadRequest(new { secondary = new { action = "set", userPublicId = await PublicIdOf("users", s.Id("avokat2")) }, rowVersion }))
            .GetProperty("code").GetString().ShouldBe("transfer.no_change");

        // the same primary
        (await BadRequest(new { newPrimaryUserPublicId = avokat1, rowVersion })).GetProperty("errors")
            .GetProperty("newPrimaryUserPublicId")[0].GetString().ShouldBe("Ky është avokati primar aktual. Zgjidhni një avokat tjetër.");

        // final primary = final secondary: in the request, and the kept primary set as secondary
        (await BadRequest(new { newPrimaryUserPublicId = third, secondary = new { action = "set", userPublicId = third }, rowVersion }))
            .GetProperty("errors").TryGetProperty("secondary.userPublicId", out _).ShouldBeTrue();
        (await BadRequest(new { secondary = new { action = "set", userPublicId = avokat1 }, rowVersion }))
            .GetProperty("errors").GetProperty("secondary.userPublicId")[0].GetString()
            .ShouldBe("Avokati sekondar duhet të jetë i ndryshëm nga avokati primar.");

        // people not offered for this case: another office, not a lawyer
        (await BadRequest(new { newPrimaryUserPublicId = await PublicIdOf("users", s.Id("drlawyer")), rowVersion }))
            .GetProperty("errors").TryGetProperty("newPrimaryUserPublicId", out _).ShouldBeTrue();
        (await BadRequest(new { secondary = new { action = "set", userPublicId = await PublicIdOf("users", s.Id("ndihmes")) }, rowVersion }))
            .GetProperty("errors").TryGetProperty("secondary.userPublicId", out _).ShouldBeTrue();

        // malformed: unknown action, set without a user, reason too long, no rowVersion
        var malformed = (await BadRequest(new
        {
            newPrimaryUserPublicId = third,
            secondary = new { action = "swap" },
            reason = new string('x', 501)
        })).GetProperty("errors");
        malformed.TryGetProperty("secondary.action", out _).ShouldBeTrue();
        malformed.TryGetProperty("reason", out _).ShouldBeTrue();
        malformed.TryGetProperty("rowVersion", out _).ShouldBeTrue();
        (await BadRequest(new { secondary = new { action = "set" }, rowVersion }))
            .GetProperty("errors").TryGetProperty("secondary.userPublicId", out _).ShouldBeTrue();

        (await Ended(s.CaseId)).ShouldBeNull();
        (await Events(s.CaseId)).ShouldBeNull();
        (await RowVersion(s.CaseId)).ShouldBe(rowVersion);
    }

    [Fact]
    public async Task Lawyer_gets_403_and_kryetar_of_another_office_404()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var s = await ScenarioAsync(seed);
        await using var other = await Seed.CreateAsync(Db.AppConnectionString);
        await other.UserAsync("drkryetar", "Kryetar", "Durrës", seed.OfficeDrId, null, "OFFICE_HEAD");
        await using var factory = new AshApiFactory(Db);
        var body = new { newPrimaryUserPublicId = await PublicIdOf("users", s.ThirdId), rowVersion = await RowVersion(s.CaseId) };

        using var lawyer = factory.CreateClient().AsUser("avokat1@ash.local");
        (await Transfer(lawyer, s, body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var drKryetar = factory.CreateClient().AsUser($"drkryetar-{other.Suffix}@ash.test");
        (await Transfer(drKryetar, s, body)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await Ended(s.CaseId)).ShouldBeNull();
    }

    [Fact]
    public async Task Two_parallel_transfers_give_one_200_and_one_409()
    {
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var s = await ScenarioAsync(seed);
        await using var factory = new AshApiFactory(Db);
        var rowVersion = await RowVersion(s.CaseId);
        var bodies = new[]
        {
            new { newPrimaryUserPublicId = await PublicIdOf("users", s.ThirdId), rowVersion },
            new { newPrimaryUserPublicId = await PublicIdOf("users", s.Id("kryetar")), rowVersion }
        };
        using var a = factory.CreateClient().AsUser(Kryetar);
        using var b = factory.CreateClient().AsUser(Kryetar);

        var responses = await Task.WhenAll(Transfer(a, s, bodies[0]), Transfer(b, s, bodies[1]));

        responses.Select(r => r.StatusCode).OrderBy(x => x).ShouldBe([HttpStatusCode.OK, HttpStatusCode.Conflict]);
        (await Json(responses.Single(r => r.StatusCode == HttpStatusCode.Conflict))).GetProperty("code").GetString()
            .ShouldBe("case.changed");
        (await Scalar($"SELECT COUNT(*) FROM case_assignments WHERE case_id = {s.CaseId} AND case_role = 'PRIMARY_LAWYER' AND ended_at IS NULL"))
            .ShouldBe(1L);
        (await Ended(s.CaseId)).ShouldBe("avokat1@ash.local:PRIMARY_LAWYER:TRANSFERRED");
        (await Events(s.CaseId)).ShouldBe("LAWYER_TRANSFERRED");
        (await RowVersion(s.CaseId)).ShouldBe(rowVersion + 1);
    }
}
