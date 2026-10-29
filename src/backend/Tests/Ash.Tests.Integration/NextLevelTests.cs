using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.Modules.Cases.Contracts.Events;
using Ash.SharedKernel.Events;
using Ash.SharedKernel.Time;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 018 — continue a closed case at the next level, and the registration search. Each test registers X/1
/// through the 016 endpoint and closes it with SQL (closing a case comes in task 036).
/// </summary>
public class NextLevelTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string Protokoll = "protokoll@ash.local";
    private const string Ministry = "Ministria e Financave dhe Ekonomisë";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class NotClosedRecorder
    {
        public ConcurrentQueue<PreviousLevelNotClosed> Events { get; } = new();
    }

    private sealed class NotClosedHandler(NotClosedRecorder recorder) : IEventHandler<PreviousLevelNotClosed>
    {
        public Task HandleAsync(PreviousLevelNotClosed @event, CancellationToken cancellationToken = default)
        {
            recorder.Events.Enqueue(@event);
            return Task.CompletedTask;
        }
    }

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(Ct);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private static string Hearing(int days) => DateTime.UtcNow.AddDays(days).ToString("yyyy-MM-ddTHH:mm:ssZ");

    private sealed record FirstLevel(Guid PublicId, long Id, string CaseCode, string UniqueCode, string ActNumber);

    /// <summary>Registers X/1 (natural party, legal party with NIPT, the seeded ministry) and optionally closes it.</summary>
    private async Task<FirstLevel> RegisterFirstLevel(HttpClient client, bool close = true)
    {
        await using var seed = await Seed.CreateAsync(Db.AppConnectionString);
        var ministry = Ash.SharedKernel.Ids.PublicId.FromBytes(
            (byte[])(await Scalar($"SELECT public_id FROM institutions WHERE name = '{Ministry}'"))!);
        var actNumber = $"AKT1-{Guid.NewGuid():N}"[..20];
        var body = new Dictionary<string, object?>
        {
            ["summonsNumber"] = $"FL1-{Guid.NewGuid():N}"[..20],
            ["actNumber"] = actNumber,
            ["actDate"] = TiraneTime.Today(new SystemClock()).AddDays(-10).ToString("yyyy-MM-dd"),
            ["receivedDate"] = TiraneTime.Today(new SystemClock()).ToString("yyyy-MM-dd"),
            ["courtLevelCode"] = "FIRST_INSTANCE",
            ["courtId"] = await seed.CourtAsync("Gjykata e Shkallës së Parë Tiranë"),
            ["judgeId"] = await seed.JudgeAsync("Ilir", "Gjyqtari"),
            ["objectText"] = "Kërkesë për shpërblim dëmi — objekti i shkallës së parë",
            ["firstHearing"] = new { at = Hearing(14) },
            ["parties"] = new object[]
            {
                new { partyRoleCode = "PADITES", kind = "NATURAL", displayName = "Agim Hoxha" },
                new { partyRoleCode = "PERSON_I_TRETE", kind = "LEGAL", displayName = "Shoqëria Alfa sh.p.k.", identifier = "K12345678A" },
                new { partyRoleCode = "I_PADITUR", kind = "INSTITUTION", institutionPublicId = ministry }
            }
        };
        var response = await client.PostAsJsonAsync("/api/cases/register", body, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await Json(response);
        var code = created.GetProperty("caseCode").GetString()!;
        var id = Convert.ToInt64(await Scalar($"SELECT id FROM cases WHERE case_code = '{code}'"));
        if (close)
        {
            await Scalar($"""
                UPDATE cases SET lifecycle_status = 'CLOSED', closed_at = UTC_TIMESTAMP(3),
                       closed_by = (SELECT id FROM users WHERE email = 'kryetar@ash.local')
                WHERE id = {id}
                """);
        }

        return new FirstLevel(created.GetProperty("casePublicId").GetGuid(), id, code, code[..^2], actNumber);
    }

    private async Task<Dictionary<string, object?>> NextLevelBody(string court = "Gjykata e Apelit Tiranë", string level = "APPEAL",
        string judgeFirst = "Genc", string judgeLast = "Muçaj")
    {
        await using var seed = await Seed.CreateAsync(Db.AppConnectionString);
        return new Dictionary<string, object?>
        {
            ["summonsNumber"] = $"FL2-{Guid.NewGuid():N}"[..20],
            ["actNumber"] = $"AKT2-{Guid.NewGuid():N}"[..20],
            ["receivedDate"] = TiraneTime.Today(new SystemClock()).ToString("yyyy-MM-dd"),
            ["courtLevelCode"] = level,
            ["courtId"] = await seed.CourtAsync(court),
            ["judgeId"] = await seed.JudgeAsync(judgeFirst, judgeLast),
            ["firstHearing"] = new { at = Hearing(30) }
        };
    }

    private static string NextLevelPath(Guid previous) => $"/api/cases/{previous}/next-level";

    [Fact]
    public async Task Closed_first_level_continues_at_level_two_with_parties_and_links_and_stays_unchanged()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Protokoll);
        var first = await RegisterFirstLevel(client);
        var before = await Scalar($"SELECT CONCAT(row_version, '|', DATE_FORMAT(updated_at, '%Y-%m-%d %H:%i:%s.%f'), '|', lifecycle_status) FROM cases WHERE id = {first.Id}");

        var response = await client.PostAsJsonAsync(NextLevelPath(first.PublicId), await NextLevelBody(), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await Json(response);
        var code = created.GetProperty("caseCode").GetString()!;
        code.ShouldBe($"{first.UniqueCode}/2");
        var nextId = Convert.ToInt64(await Scalar($"SELECT id FROM cases WHERE case_code = '{code}'"));

        (await Scalar($"SELECT previous_case_id FROM cases WHERE id = {nextId}")).ShouldBe((ulong)first.Id);
        (await Scalar($"SELECT level_seq FROM cases WHERE id = {nextId}")).ShouldBe((ushort)2);
        (await Scalar($"SELECT lifecycle_status FROM cases WHERE id = {nextId}")).ShouldBe("REGISTERED");
        (await Scalar($"SELECT dossier_id = (SELECT dossier_id FROM cases WHERE id = {first.Id}) FROM cases WHERE id = {nextId}")).ShouldBe(1L);
        (await Scalar($"SELECT object_text FROM cases WHERE id = {nextId}")).ShouldBe("Kërkesë për shpërblim dëmi — objekti i shkallës së parë");
        (await Scalar($"SELECT COUNT(*) FROM case_hearings WHERE case_id = {nextId} AND status = 'SCHEDULED'")).ShouldBe(1L);

        // parties copied with new public ids; the active MONITORING link copied
        (await Scalar($"SELECT COUNT(*) FROM case_parties WHERE case_id = {nextId} AND deleted_at IS NULL")).ShouldBe(3L);
        (await Scalar($"SELECT COUNT(*) FROM case_parties n JOIN case_parties o ON o.public_id = n.public_id WHERE n.case_id = {nextId} AND o.case_id = {first.Id}"))
            .ShouldBe(0L);
        (await Scalar($"SELECT identifier FROM case_parties WHERE case_id = {nextId} AND party_kind = 'LEGAL'")).ShouldBe("K12345678A");
        (await Scalar($"SELECT COUNT(*) FROM case_institutions WHERE case_id = {nextId} AND link_mode = 'MONITORING' AND ended_at IS NULL"))
            .ShouldBe(1L);
        (await Scalar($"SELECT COUNT(*) FROM case_assignments WHERE case_id = {nextId}")).ShouldBe(0L);

        (await Scalar($"SELECT GROUP_CONCAT(event_type ORDER BY id) FROM case_history_events WHERE case_id = {nextId}"))
            .ShouldBe("CASE_CREATED_FROM_PREVIOUS,PARTY_ADDED,PARTY_ADDED,PARTY_ADDED,INSTITUTION_LINKED,SENT_TO_HEAD");
        ((string)(await Scalar($"SELECT summary FROM case_history_events WHERE case_id = {nextId} AND event_type = 'CASE_CREATED_FROM_PREVIOUS'"))!)
            .ShouldContain(first.CaseCode);

        // X/1 is not changed at all
        (await Scalar($"SELECT CONCAT(row_version, '|', DATE_FORMAT(updated_at, '%Y-%m-%d %H:%i:%s.%f'), '|', lifecycle_status) FROM cases WHERE id = {first.Id}"))
            .ShouldBe(before);
        (await Scalar($"SELECT COUNT(*) FROM case_parties WHERE case_id = {first.Id} AND deleted_at IS NULL")).ShouldBe(3L);

        // continuing /1 again: it already continued in /2
        var again = await client.PostAsJsonAsync(NextLevelPath(first.PublicId), await NextLevelBody(), Ct);
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Json(again)).GetProperty("title").GetString().ShouldBe($"Kjo çështje ka vazhduar tashmë në {first.UniqueCode}/2");
    }

    private AshApiFactory FactoryRecording(NotClosedRecorder recorder) =>
        new(Db, configureServices: services =>
        {
            services.AddSingleton(recorder);
            services.AddScoped<IEventHandler<PreviousLevelNotClosed>, NotClosedHandler>();
        });

    private static string NotifyPath(Guid caseId) => $"/api/cases/{caseId}/notify-not-closed";

    [Fact]
    public async Task First_level_not_closed_is_refused_without_side_effects()
    {
        var recorder = new NotClosedRecorder();
        await using var factory = FactoryRecording(recorder);
        using var client = factory.CreateClient().AsUser(Protokoll);
        var first = await RegisterFirstLevel(client, close: false);
        var body = await NextLevelBody();

        var response = await client.PostAsJsonAsync(NextLevelPath(first.PublicId), body, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await Json(response);
        problem.GetProperty("code").GetString().ShouldBe("case.previous_not_closed");
        problem.GetProperty("title").GetString()
            .ShouldBe("Shkalla e mëparshme nuk është mbyllur ende në sistem. Avokati duhet të regjistrojë vendimin.");
        recorder.Events.ShouldBeEmpty(); // no side effects in a failing request
        (await Scalar($"SELECT COUNT(*) FROM cases WHERE dossier_id = (SELECT dossier_id FROM cases WHERE id = {first.Id})")).ShouldBe(1L);
        (await Scalar($"SELECT COUNT(*) FROM case_history_events WHERE case_id = {first.Id} AND event_type = 'NEXT_LEVEL_SUMMONS_PENDING'"))
            .ShouldBe(0L);
    }

    [Fact]
    public async Task Notify_not_closed_records_history_and_publishes_once_per_day()
    {
        var recorder = new NotClosedRecorder();
        await using var factory = FactoryRecording(recorder);
        using var client = factory.CreateClient().AsUser(Protokoll);
        var first = await RegisterFirstLevel(client, close: false);
        // a primary lawyer is assigned: avokat1
        await Scalar($"""
            INSERT INTO case_assignments (case_id, user_id, case_role, assigned_by)
            SELECT {first.Id}, (SELECT id FROM users WHERE email = 'avokat1@ash.local'), 'PRIMARY_LAWYER', id
            FROM users WHERE email = 'kryetar@ash.local'
            """);
        var caseRow = $"SELECT CONCAT(row_version, '|', DATE_FORMAT(updated_at, '%Y-%m-%d %H:%i:%s.%f')) FROM cases WHERE id = {first.Id}";
        var before = await Scalar(caseRow);
        var hearingAt = new DateTimeOffset(2030, 3, 15, 9, 30, 0, TimeSpan.FromHours(1)); // 08:30 UTC

        var notified = await client.PostAsJsonAsync(NotifyPath(first.PublicId), await NotifyBody("FL-APEL-77", hearingAt), Ct);

        notified.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await Json(notified);
        result.GetProperty("alreadyNotified").GetBoolean().ShouldBeFalse();
        var row = $"FROM case_history_events WHERE case_id = {first.Id} AND event_type = 'NEXT_LEVEL_SUMMONS_PENDING'";
        (await Scalar($"SELECT COUNT(*) {row}")).ShouldBe(1L);
        var summary = (string)(await Scalar($"SELECT summary {row}"))!;
        summary.ShouldContain("FL-APEL-77");
        summary.ShouldContain("Gjykata e Apelit Tiranë");
        summary.ShouldContain("15.03.2030 09:30"); // shown in Tirane time
        summary.ShouldContain("Protokoll Dev"); // the Protokoll's name
        (await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.new.court')) {row}")).ShouldBe("Gjykata e Apelit Tiranë");
        ((string)(await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.new.hearingAt')) {row}"))!).ShouldStartWith("2030-03-15T08:30:00");

        var announced = recorder.Events.ShouldHaveSingleItem();
        announced.PreviousCasePublicId.ShouldBe(first.PublicId);
        announced.SummonsNumber.ShouldBe("FL-APEL-77");
        announced.HearingAt.ShouldBe(hearingAt.UtcDateTime);
        announced.PrimaryLawyerPublicId.ShouldBe(await UserPublicId("avokat1@ash.local"));
        announced.HeadUserPublicId.ShouldBe(await UserPublicId("kryetar@ash.local"));
        (await Scalar(caseRow)).ShouldBe(before); // the case row itself is not changed

        // the same summons again within 24 hours: answered from the existing row, no new row and no new event
        var again = await Json(await client.PostAsJsonAsync(NotifyPath(first.PublicId), await NotifyBody("FL-APEL-77", hearingAt), Ct));
        again.GetProperty("alreadyNotified").GetBoolean().ShouldBeTrue();
        again.GetProperty("notifiedAt").GetDateTime().ShouldBe(result.GetProperty("notifiedAt").GetDateTime(), TimeSpan.FromSeconds(1));
        (await Scalar($"SELECT COUNT(*) {row}")).ShouldBe(1L);
        recorder.Events.Count.ShouldBe(1);

        // a different summons number is another summons: announced too
        var other = await Json(await client.PostAsJsonAsync(NotifyPath(first.PublicId), await NotifyBody("FL-APEL-78", hearingAt), Ct));
        other.GetProperty("alreadyNotified").GetBoolean().ShouldBeFalse();
        (await Scalar($"SELECT COUNT(*) {row}")).ShouldBe(2L);
        recorder.Events.Count.ShouldBe(2);
    }

    private async Task<Guid> UserPublicId(string email) =>
        Ash.SharedKernel.Ids.PublicId.FromBytes((byte[])(await Scalar($"SELECT public_id FROM users WHERE email = '{email}'"))!);

    private async Task<Dictionary<string, object?>> NotifyBody(string summonsNumber, DateTimeOffset? hearingAt = null)
    {
        await using var seed = await Seed.CreateAsync(Db.AppConnectionString);
        return new Dictionary<string, object?>
        {
            ["summonsNumber"] = summonsNumber,
            ["courtId"] = await seed.CourtAsync("Gjykata e Apelit Tiranë"),
            ["hearingAt"] = (hearingAt ?? DateTimeOffset.UtcNow.AddDays(30)).ToString("yyyy-MM-ddTHH:mm:sszzz")
        };
    }

    [Fact]
    public async Task Notify_not_closed_validates_the_summons_like_a_registration()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Protokoll);
        var open = await RegisterFirstLevel(client, close: false);
        var body = await NotifyBody("FL-1");
        body["courtId"] = null;
        body["hearingAt"] = "2030-03-15T09:30:00"; // no offset

        var response = await client.PostAsJsonAsync(NotifyPath(open.PublicId), body, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var errors = (await Json(response)).GetProperty("errors");
        errors.TryGetProperty("courtId", out _).ShouldBeTrue();
        errors.TryGetProperty("hearingAt", out _).ShouldBeTrue();
        (await Scalar("SELECT COUNT(*) FROM case_history_events WHERE event_type = 'NEXT_LEVEL_SUMMONS_PENDING'")).ShouldBe(0L);
    }

    [Fact]
    public async Task Two_parallel_notifies_for_the_same_summons_write_one_row_and_one_event()
    {
        var recorder = new NotClosedRecorder();
        await using var factory = FactoryRecording(recorder);
        using var setup = factory.CreateClient().AsUser(Protokoll);
        var open = await RegisterFirstLevel(setup, close: false);
        var body = await NotifyBody("FL-PARALEL-1");
        using var a = factory.CreateClient().AsUser(Protokoll);
        using var b = factory.CreateClient().AsUser(Protokoll);

        var responses = await Task.WhenAll(
            a.PostAsJsonAsync(NotifyPath(open.PublicId), body, Ct),
            b.PostAsJsonAsync(NotifyPath(open.PublicId), body, Ct));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);
        var results = await Task.WhenAll(responses.Select(Json));
        results.Select(r => r.GetProperty("alreadyNotified").GetBoolean()).OrderBy(x => x).ShouldBe([false, true]);
        (await Scalar($"SELECT COUNT(*) FROM case_history_events WHERE case_id = {open.Id} AND event_type = 'NEXT_LEVEL_SUMMONS_PENDING'"))
            .ShouldBe(1L);
        recorder.Events.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Notify_not_closed_is_refused_for_a_closed_case_and_needs_the_permission()
    {
        await using var factory = new AshApiFactory(Db);
        using var protokoll = factory.CreateClient().AsUser(Protokoll);
        var closed = await RegisterFirstLevel(protokoll);

        var refused = await protokoll.PostAsJsonAsync(NotifyPath(closed.PublicId), await NotifyBody("FL-1"), Ct);
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Json(refused)).GetProperty("code").GetString().ShouldBe("case.previous_closed");

        var open = await RegisterFirstLevel(protokoll, close: false);
        using var lawyer = factory.CreateClient().AsUser("avokat1@ash.local");
        (await lawyer.PostAsJsonAsync(NotifyPath(open.PublicId), await NotifyBody("FL-1"), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Scalar($"SELECT COUNT(*) FROM case_history_events WHERE event_type = 'NEXT_LEVEL_SUMMONS_PENDING'")).ShouldBe(0L);
    }

    [Fact]
    public async Task Archived_dossier_is_refused()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Protokoll);
        var first = await RegisterFirstLevel(client);
        await Scalar($"""
            UPDATE dossiers SET archived_at = UTC_TIMESTAMP(3), archived_by = (SELECT id FROM users WHERE email = 'kryetar@ash.local')
            WHERE id = (SELECT dossier_id FROM cases WHERE id = {first.Id})
            """);

        var response = await client.PostAsJsonAsync(NextLevelPath(first.PublicId), await NextLevelBody(), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Json(response)).GetProperty("code").GetString().ShouldBe("case.dossier_archived");
    }

    [Fact]
    public async Task Delegated_link_becomes_monitoring_at_a_level_without_delegation()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Protokoll);
        var first = await RegisterFirstLevel(client);
        await Scalar($"UPDATE case_institutions SET link_mode = 'DELEGATED' WHERE case_id = {first.Id}");

        var response = await client.PostAsJsonAsync(NextLevelPath(first.PublicId),
            await NextLevelBody("Gjykata e Lartë", "HIGH_COURT", "Fatmir", "Hysëni"), Ct); // HIGH_COURT: allows_delegation = 0

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await Json(response);
        created.GetProperty("warnings").EnumerateArray()
            .ShouldContain(w => w.GetProperty("code").GetString() == "institution.delegation_not_allowed");
        var nextId = Convert.ToInt64(await Scalar($"SELECT id FROM cases WHERE case_code = '{created.GetProperty("caseCode").GetString()}'"));
        (await Scalar($"SELECT link_mode FROM case_institutions WHERE case_id = {nextId}")).ShouldBe("MONITORING");
        (await Scalar($"SELECT link_mode FROM case_institutions WHERE case_id = {first.Id}")).ShouldBe("DELEGATED"); // X/1 unchanged
    }

    private static async Task<JsonElement> Search(HttpClient client, string q)
    {
        var response = await client.GetAsync($"/api/cases/registration-search?q={Uri.EscapeDataString(q)}", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, q);
        return await Json(response);
    }

    private static JsonElement? Find(JsonElement page, string caseCode) =>
        page.GetProperty("items").EnumerateArray().Cast<JsonElement?>()
            .FirstOrDefault(i => i!.Value.GetProperty("caseCode").GetString() == caseCode);

    [Fact]
    public async Task Registration_search_finds_the_latest_level_by_code_act_number_party_name_and_nipt()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Protokoll);
        var first = await RegisterFirstLevel(client);
        var next = await client.PostAsJsonAsync(NextLevelPath(first.PublicId), await NextLevelBody(), Ct);
        var latest = (await Json(next)).GetProperty("caseCode").GetString()!;
        await Scalar($"UPDATE cases SET lifecycle_status = 'CLOSED', closed_at = UTC_TIMESTAMP(3), closed_by = (SELECT id FROM users WHERE email = 'kryetar@ash.local') WHERE case_code = '{latest}'");

        foreach (var q in new[] { first.UniqueCode, first.CaseCode, first.ActNumber, "financ", "agim hox", "k12345678a" })
        {
            var page = await Search(client, q);
            var item = Find(page, latest);
            item.ShouldNotBeNull(q); // the dossier is shown by its latest level, whatever level matched
            Find(page, first.CaseCode).ShouldBeNull(q);
            item.Value.GetProperty("canContinue").GetBoolean().ShouldBeTrue();
            item.Value.GetProperty("partyCount").GetInt32().ShouldBe(3);
            item.Value.GetProperty("courtName").GetString().ShouldBe("Gjykata e Apelit Tiranë");
            item.Value.TryGetProperty("objectText", out _).ShouldBeFalse();
        }

        // '%' is literal text, not a wildcard; one character is too short
        (await Search(client, "%%")).GetProperty("items").GetArrayLength().ShouldBe(0);
        (await client.GetAsync("/api/cases/registration-search?q=a", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Registration_search_shows_an_open_case_that_cannot_be_continued_yet()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Protokoll);
        var open = await RegisterFirstLevel(client, close: false);

        var item = Find(await Search(client, open.ActNumber), open.CaseCode);

        item.ShouldNotBeNull();
        item.Value.GetProperty("canContinue").GetBoolean().ShouldBeFalse();
        item.Value.GetProperty("reason").GetString().ShouldBe("E pambyllur në sistem");
        item.Value.GetProperty("status").GetString().ShouldBe("REGISTERED");
    }

    [Fact]
    public async Task Users_without_case_link_previous_get_403()
    {
        await using var factory = new AshApiFactory(Db);
        using var protokoll = factory.CreateClient().AsUser(Protokoll);
        var first = await RegisterFirstLevel(protokoll);
        using var lawyer = factory.CreateClient().AsUser("avokat1@ash.local");

        (await lawyer.GetAsync("/api/cases/registration-search?q=financ", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await lawyer.PostAsJsonAsync(NextLevelPath(first.PublicId), await NextLevelBody(), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Two_parallel_next_level_requests_create_one_level()
    {
        await using var factory = new AshApiFactory(Db);
        using var setup = factory.CreateClient().AsUser(Protokoll);
        var first = await RegisterFirstLevel(setup);
        var bodies = new[] { await NextLevelBody(), await NextLevelBody() };
        using var a = factory.CreateClient().AsUser(Protokoll);
        using var b = factory.CreateClient().AsUser(Protokoll);

        var responses = await Task.WhenAll(
            a.PostAsJsonAsync(NextLevelPath(first.PublicId), bodies[0], Ct),
            b.PostAsJsonAsync(NextLevelPath(first.PublicId), bodies[1], Ct));

        responses.Select(r => r.StatusCode).OrderBy(s => s)
            .ShouldBe([HttpStatusCode.Created, HttpStatusCode.Conflict]);
        var conflict = responses.Single(r => r.StatusCode == HttpStatusCode.Conflict);
        (await Json(conflict)).GetProperty("title").GetString().ShouldBe($"Kjo çështje ka vazhduar tashmë në {first.UniqueCode}/2");
        (await Scalar($"SELECT COUNT(*) FROM cases WHERE dossier_id = (SELECT dossier_id FROM cases WHERE id = {first.Id})")).ShouldBe(2L);
    }
}
