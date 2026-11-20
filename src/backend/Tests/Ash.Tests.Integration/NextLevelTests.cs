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
            ["courtId"] = await OfficeCourtsTestData.MapAsync(Db.AppConnectionString, OfficeCourtsTestData.FirstInstanceTirana),
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

    /// <summary>The next-level form; <paramref name="officeCode"/>: the office the court is mapped to (null = not mapped).</summary>
    private async Task<Dictionary<string, object?>> NextLevelBody(string court = "Gjykata e Apelit Tiranë", string level = "APPEAL",
        string judgeFirst = "Genc", string judgeLast = "Muçaj", string? officeCode = "TR")
    {
        await using var seed = await Seed.CreateAsync(Db.AppConnectionString);
        var courtId = officeCode is null
            ? await seed.CourtAsync(court)
            : await OfficeCourtsTestData.MapAsync(Db.AppConnectionString, court, officeCode);
        return new Dictionary<string, object?>
        {
            ["summonsNumber"] = $"FL2-{Guid.NewGuid():N}"[..20],
            ["actNumber"] = $"AKT2-{Guid.NewGuid():N}"[..20],
            ["receivedDate"] = TiraneTime.Today(new SystemClock()).ToString("yyyy-MM-dd"),
            ["courtLevelCode"] = level,
            ["courtId"] = courtId,
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

        // task 053j: ONLY the plaintiff and the defendant are copied (copy_to_next_level), with new public ids — the third
        // party (the LEGAL one) is not; the defendant institution's MONITORING link is created
        (await Scalar($"SELECT COUNT(*) FROM case_parties WHERE case_id = {nextId} AND deleted_at IS NULL")).ShouldBe(2L);
        (await Scalar($"""
            SELECT GROUP_CONCAT(r.code ORDER BY p.id) FROM case_parties p JOIN party_roles r ON r.id = p.party_role_id
             WHERE p.case_id = {nextId}
            """)).ShouldBe("PADITES,I_PADITUR");
        (await Scalar($"SELECT COUNT(*) FROM case_parties n JOIN case_parties o ON o.public_id = n.public_id WHERE n.case_id = {nextId} AND o.case_id = {first.Id}"))
            .ShouldBe(0L);
        (await Scalar($"SELECT COUNT(*) FROM case_parties WHERE case_id = {nextId} AND party_kind = 'LEGAL'")).ShouldBe(0L);
        (await Scalar($"SELECT COUNT(*) FROM case_institutions WHERE case_id = {nextId} AND link_mode = 'MONITORING' AND ended_at IS NULL"))
            .ShouldBe(1L);
        (await Scalar($"SELECT COUNT(*) FROM case_assignments WHERE case_id = {nextId}")).ShouldBe(0L);
        (await Scalar($"SELECT category_id IS NULL FROM cases WHERE id = {nextId}")).ShouldBe(1L);

        (await Scalar($"SELECT GROUP_CONCAT(event_type ORDER BY id) FROM case_history_events WHERE case_id = {nextId}"))
            .ShouldBe("CASE_CREATED_FROM_PREVIOUS,PARTY_ADDED,PARTY_ADDED,INSTITUTION_LINKED,SENT_TO_HEAD");
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
        result.GetProperty("hasPrimaryLawyer").GetBoolean().ShouldBeTrue();
        Recipients(result).ShouldBe(["Avokat1 Dev|PRIMARY_LAWYER", await HeadRecipient()]);
        var row = $"FROM case_history_events WHERE case_id = {first.Id} AND event_type = 'NEXT_LEVEL_SUMMONS_PENDING'";
        (await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.new.recipients[0].name')) {row}")).ShouldBe("Avokat1 Dev");
        (await Scalar($"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.new.recipients[1].role')) {row}")).ShouldBe("OFFICE_HEAD");
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

    private static List<string> Recipients(JsonElement response) => response.GetProperty("recipients").EnumerateArray()
        .Select(r => $"{r.GetProperty("name").GetString()}|{r.GetProperty("role").GetString()}").ToList();

    /// <summary>The TR office head (kryetar@ash.local) as a recipient "Name|OFFICE_HEAD".</summary>
    private async Task<string> HeadRecipient() =>
        $"{await Scalar("SELECT CONCAT(first_name, ' ', last_name) FROM users WHERE email = 'kryetar@ash.local'")}|OFFICE_HEAD";

    [Fact]
    public async Task Notify_not_closed_without_a_primary_lawyer_tells_the_office_head_and_lists_the_recipients_first()
    {
        var recorder = new NotClosedRecorder();
        await using var factory = FactoryRecording(recorder);
        using var client = factory.CreateClient().AsUser(Protokoll);
        var first = await RegisterFirstLevel(client, close: false); // REGISTERED: no lawyer yet

        // the dialog asks first: only the Kryetar
        var preview = await client.GetAsync($"{NotifyPath(first.PublicId)}/recipients", Ct);
        preview.StatusCode.ShouldBe(HttpStatusCode.OK);
        var recipients = await Json(preview);
        recipients.GetProperty("hasPrimaryLawyer").GetBoolean().ShouldBeFalse();
        Recipients(recipients).ShouldBe([await HeadRecipient()]);

        var notified = await client.PostAsJsonAsync(NotifyPath(first.PublicId), await NotifyBody("FL-PA-AVOKAT"), Ct);
        notified.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await Json(notified);
        result.GetProperty("hasPrimaryLawyer").GetBoolean().ShouldBeFalse();
        Recipients(result).ShouldBe([await HeadRecipient()]);
        var announced = recorder.Events.ShouldHaveSingleItem();
        announced.PrimaryLawyerPublicId.ShouldBeNull();
        announced.HeadUserPublicId.ShouldBe(await UserPublicId("kryetar@ash.local"));
        var row = $"FROM case_history_events WHERE case_id = {first.Id} AND event_type = 'NEXT_LEVEL_SUMMONS_PENDING'";
        (await Scalar($"SELECT JSON_LENGTH(details, '$.new.recipients') {row}")).ShouldBe(1L);
        ((string)(await Scalar($"SELECT summary {row}"))!).ShouldContain(((string)(await HeadRecipient()))[..^"|OFFICE_HEAD".Length]);

        // with a lawyer assigned later, the recipients follow
        await Scalar($"""
            INSERT INTO case_assignments (case_id, user_id, case_role, assigned_by)
            SELECT {first.Id}, (SELECT id FROM users WHERE email = 'avokat1@ash.local'), 'PRIMARY_LAWYER', id
            FROM users WHERE email = 'kryetar@ash.local'
            """);
        Recipients(await Json(await client.GetAsync($"{NotifyPath(first.PublicId)}/recipients", Ct)))
            .ShouldBe(["Avokat1 Dev|PRIMARY_LAWYER", await HeadRecipient()]);

        // a closed case: nothing to notify; a lawyer may not ask
        await Scalar($"UPDATE cases SET lifecycle_status = 'CLOSED', closed_at = UTC_TIMESTAMP(3), closed_by = (SELECT id FROM users WHERE email = 'kryetar@ash.local') WHERE id = {first.Id}");
        (await client.GetAsync($"{NotifyPath(first.PublicId)}/recipients", Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var lawyer = factory.CreateClient().AsUser("avokat1@ash.local");
        (await lawyer.GetAsync($"{NotifyPath(first.PublicId)}/recipients", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
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

    [Theory]
    [InlineData("Gjykata e Apelit Tiranë", "APPEAL", "Genc", "Muçaj")] // a level that allows delegation
    [InlineData("Gjykata e Lartë", "HIGH_COURT", "Fatmir", "Hysëni")] // one that does not
    public async Task B1_next_level_copies_only_parties_and_object_institution_parties_are_linked_as_monitoring(
        string court, string level, string judgeFirst, string judgeLast)
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Protokoll);
        var first = await RegisterFirstLevel(client);
        // X/1 delegated to the ministry (party) with its category, plus a link to an institution that is NOT a party
        await DelegateWithSql(first.Id, "DELEGIM");
        await Scalar($"""
            INSERT INTO case_institutions (case_id, institution_id, link_mode, started_at, started_by)
            SELECT {first.Id}, i.id, 'MONITORING', UTC_TIMESTAMP(3), u.id
              FROM institutions i JOIN users u ON u.email = 'kryetar@ash.local'
             WHERE i.name <> '{Ministry}' ORDER BY i.id LIMIT 1;
            """);

        // the preview says what the new level gets: the ministry, as MONITORING
        var preview = await Json(await client.GetAsync(PreviewPath(first.PublicId), Ct));
        preview.GetProperty("institutionLinks").EnumerateArray()
            .Select(l => $"{l.GetProperty("institution").GetString()}|{l.GetProperty("mode").GetString()}")
            .ShouldBe([$"{Ministry}|MONITORING"]);

        var response = await client.PostAsJsonAsync(NextLevelPath(first.PublicId),
            await NextLevelBody(court, level, judgeFirst, judgeLast), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await Json(response);
        created.GetProperty("warnings").EnumerateArray()
            .ShouldNotContain(w => w.GetProperty("code").GetString()!.StartsWith("institution."));
        var nextId = Convert.ToInt64(await Scalar($"SELECT id FROM cases WHERE case_code = '{created.GetProperty("caseCode").GetString()}'"));
        (await Scalar($"SELECT COUNT(*) FROM case_parties WHERE case_id = {nextId} AND deleted_at IS NULL")).ShouldBe(2L); // plaintiff + defendant
        (await Scalar($"SELECT object_text FROM cases WHERE id = {nextId}")).ShouldBe("Kërkesë për shpërblim dëmi — objekti i shkallës së parë");
        (await Scalar($"""
            SELECT GROUP_CONCAT(CONCAT(i.name, '|', l.link_mode)) FROM case_institutions l JOIN institutions i ON i.id = l.institution_id
            WHERE l.case_id = {nextId} AND l.ended_at IS NULL
            """)).ShouldBe($"{Ministry}|MONITORING");
        // no category, subcategory or delegation: the new lawyer sets them
        (await Scalar($"SELECT CONCAT_WS('|', IFNULL(category_id, '-'), IFNULL(subcategory_id, '-')) FROM cases WHERE id = {nextId}")).ShouldBe("-|-");
        // X/1 unchanged
        (await Scalar($"SELECT link_mode FROM case_institutions l JOIN institutions i ON i.id = l.institution_id WHERE l.case_id = {first.Id} AND i.name = '{Ministry}'"))
            .ShouldBe("DELEGATED");
        (await Scalar($"SELECT c.code FROM cases x JOIN case_categories c ON c.id = x.category_id WHERE x.id = {first.Id}")).ShouldBe("DELEGIM");
    }

    /// <summary>A delegation as task 027 writes it: the link DELEGATED and the category DELEGIM / RELATIM.</summary>
    private Task<object?> DelegateWithSql(long caseId, string category) => Scalar($"""
        UPDATE case_institutions SET link_mode = 'DELEGATED' WHERE case_id = {caseId};
        UPDATE cases SET category_id = (SELECT id FROM case_categories WHERE code = '{category}'), subcategory_id = NULL WHERE id = {caseId};
        """);

    private Task<object?> CloseWithSql(string caseCode) => Scalar($"""
        UPDATE cases SET lifecycle_status = 'CLOSED', closed_at = UTC_TIMESTAMP(3),
               closed_by = (SELECT id FROM users WHERE email = 'kryetar@ash.local')
        WHERE case_code = '{caseCode}'
        """);

    [Fact]
    public async Task B4_a_retrial_may_continue_at_any_court_level_also_a_lower_one()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Protokoll);
        var first = await RegisterFirstLevel(client);
        // a category with its subcategory on /1: neither is copied (B.1)
        await Scalar($"""
            UPDATE cases x JOIN case_subcategories s ON s.category_id = (SELECT id FROM case_categories WHERE code = 'NDJEKJE')
               SET x.category_id = s.category_id, x.subcategory_id = s.id
             WHERE x.id = {first.Id}
            """);

        // /1 Shkalla I → /2 Apel → /3 Gjykata e Lartë → /4 Shkalla I again (sent back for retrial)
        var steps = new[]
        {
            await NextLevelBody(),
            await NextLevelBody("Gjykata e Lartë", "HIGH_COURT", "Fatmir", "Hysëni"),
            await NextLevelBody("Gjykata e Shkallës së Parë Tiranë", "FIRST_INSTANCE", "Ilir", "Gjyqtari"),
        };
        var previous = first.PublicId;
        var codes = new List<string>();
        foreach (var body in steps)
        {
            var response = await client.PostAsJsonAsync(NextLevelPath(previous), body, Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.Created, string.Join(",", codes));
            var created = await Json(response);
            codes.Add(created.GetProperty("caseCode").GetString()!);
            previous = created.GetProperty("casePublicId").GetGuid();
            await CloseWithSql(codes[^1]);
        }

        codes.ShouldBe([$"{first.UniqueCode}/2", $"{first.UniqueCode}/3", $"{first.UniqueCode}/4"]);
        (await Scalar($"""
            SELECT GROUP_CONCAT(l.code ORDER BY c.level_seq) FROM cases c JOIN court_levels l ON l.id = c.court_level_id
            WHERE c.dossier_id = (SELECT dossier_id FROM cases WHERE id = {first.Id})
            """)).ShouldBe("FIRST_INSTANCE,APPEAL,HIGH_COURT,FIRST_INSTANCE");
        (await Scalar($"SELECT COUNT(*) FROM cases WHERE case_code = '{codes[0]}' AND category_id IS NULL AND subcategory_id IS NULL")).ShouldBe(1L);
        (await Scalar($"SELECT COUNT(*) FROM case_parties WHERE case_id = (SELECT id FROM cases WHERE case_code = '{codes[^1]}') AND deleted_at IS NULL"))
            .ShouldBe(2L); // task 053j: plaintiff + defendant, level after level
    }

    private const string SearchPath = "/api/cases/registration-search";

    /// <summary>GET the search with the given filters ("name=value&amp;…", values escaped here).</summary>
    private static async Task<JsonElement> Search(HttpClient client, params (string Name, object Value)[] filters)
    {
        var response = await client.GetAsync(SearchUrl(filters), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, SearchUrl(filters));
        return await Json(response);
    }

    private static string SearchUrl(params (string Name, object Value)[] filters) =>
        SearchPath + "?" + string.Join('&', filters.Select(f => $"{f.Name}={Uri.EscapeDataString(f.Value.ToString()!)}"));

    private static JsonElement? Find(JsonElement page, string caseCode) =>
        page.GetProperty("items").EnumerateArray().Cast<JsonElement?>()
            .FirstOrDefault(i => i!.Value.GetProperty("caseCode").GetString() == caseCode);

    [Fact]
    public async Task B2_each_search_field_finds_the_dossier_on_any_level_and_shows_its_latest_level()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Protokoll);
        await using var seed = await Seed.CreateAsync(Db.AppConnectionString);
        var first = await RegisterFirstLevel(client);
        var next = await client.PostAsJsonAsync(NextLevelPath(first.PublicId), await NextLevelBody(), Ct);
        var latest = (await Json(next)).GetProperty("caseCode").GetString()!;
        await CloseWithSql(latest);
        // on /1 only: a category with its subcategory and a decision result
        await Scalar($"""
            UPDATE cases x JOIN case_subcategories s ON s.category_id = (SELECT id FROM case_categories WHERE code = 'NDJEKJE')
               SET x.category_id = s.category_id, x.subcategory_id = s.id
             WHERE x.id = {first.Id};
            INSERT INTO case_decisions (case_id, result_id, recorded_by)
            SELECT {first.Id}, (SELECT id FROM decision_results ORDER BY id LIMIT 1), id FROM users WHERE email = 'avokat1@ash.local';
            """);
        var subcategory = (string)(await Scalar($"SELECT s.code FROM cases x JOIN case_subcategories s ON s.id = x.subcategory_id WHERE x.id = {first.Id}"))!;
        var result = (string)(await Scalar("SELECT code FROM decision_results ORDER BY id LIMIT 1"))!;
        var otherResult = (string)(await Scalar("SELECT code FROM decision_results ORDER BY id DESC LIMIT 1"))!;
        var firstCourt = await seed.CourtAsync("Gjykata e Shkallës së Parë Tiranë");
        var appealCourt = await seed.CourtAsync("Gjykata e Apelit Tiranë");
        var firstJudge = await seed.JudgeAsync("Ilir", "Gjyqtari");

        var hits = new (string, object)[][]
        {
            [("code", first.UniqueCode)], [("code", first.CaseCode)], [("code", first.UniqueCode[^4..])],
            [("actNumber", first.ActNumber)], [("actNumber", first.ActNumber[2..10])],
            [("courtId", firstCourt)], [("courtId", appealCourt)], [("judgeId", firstJudge)],
            [("plaintiff", "agim hox")], [("plaintiff", "oxha")],
            [("defendant", "financ")], [("defendant", "k12345678a")], [("defendant", "SHOQERIA")],
            [("object", "shperblim demi")],
            [("category", "NDJEKJE")], [("subcategory", subcategory)], [("result", result)],
            // combined: every filter must match (each on any level of the dossier)
            [("plaintiff", "agim"), ("courtId", appealCourt), ("result", result)],
            [("CODE", first.UniqueCode)], // parameter names are case-insensitive
        };
        foreach (var filters in hits)
        {
            var page = await Search(client, filters);
            var item = Find(page, latest);
            item.ShouldNotBeNull(SearchUrl(filters)); // the dossier is shown by its latest level, whatever level matched
            Find(page, first.CaseCode).ShouldBeNull(SearchUrl(filters));
            item.Value.GetProperty("canContinue").GetBoolean().ShouldBeTrue();
            item.Value.GetProperty("partyCount").GetInt32().ShouldBe(2); // task 053j: the third party was not copied to /2
            item.Value.GetProperty("courtName").GetString().ShouldBe("Gjykata e Apelit Tiranë");
            item.Value.GetProperty("objectSnippet").GetString().ShouldBe("Kërkesë për shpërblim dëmi — objekti i shkallës së parë");
            item.Value.TryGetProperty("objectText", out _).ShouldBeFalse();
        }

        var misses = new (string, object)[][]
        {
            [("code", first.ActNumber)], // the code filter looks in the case code only
            [("actNumber", first.UniqueCode)],
            [("defendant", "agim hox")], // Agim is a plaintiff
            [("plaintiff", "financ")],
            [("plaintiff", "agim"), ("result", otherResult)],
            [("object", "pronesi")],
            [("category", "DELEGIM")],
            [("code", "%%")], // '%' is literal text, not a wildcard
        };
        foreach (var filters in misses)
        {
            Find(await Search(client, filters), latest).ShouldBeNull(SearchUrl(filters));
        }
    }

    [Fact]
    public async Task B2_invalid_or_missing_filters_are_a_400_per_field()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Protokoll);

        async Task<JsonElement> Errors(string query)
        {
            var response = await client.GetAsync($"{SearchPath}{query}", Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, query);
            return (await Json(response)).GetProperty("errors");
        }

        // task 053j: a table of every case — no filter is fine (unknown parameters such as the old q are ignored)
        (await client.GetAsync(SearchPath, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync($"{SearchPath}?q=agim", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        // live search: 1 character is no filter (200); only more than 100 is refused
        (await client.GetAsync($"{SearchPath}?plaintiff=a", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var many = await Errors($"?plaintiff={new string('a', 101)}&courtId=abc&category=NOPE&result=NOPE&status=NOPE");
        foreach (var field in new[] { "plaintiff", "courtId", "category", "result", "status" })
        {
            many.TryGetProperty(field, out _).ShouldBeTrue(field);
        }
    }

    [Fact]
    public async Task B2_the_filter_list_names_each_field_with_its_control()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Protokoll);

        var filters = await Json(await client.GetAsync($"{SearchPath}/filters", Ct));

        filters.EnumerateArray()
            .Select(f => $"{f.GetProperty("name").GetString()}|{f.GetProperty("input").GetString()}|" +
                         (f.TryGetProperty("dependsOn", out var on) ? on.GetString() : null))
            .ShouldBe([
                "code|Text|", "actNumber|Text|", "courtLevel|CourtLevel|", "courtId|Court|courtLevel", "judgeId|Judge|courtId", "plaintiff|Text|", "defendant|Text|",
                "object|Text|", "category|Category|", "subcategory|Subcategory|category", "result|DecisionResult|"
            ]);
        using var lawyer = factory.CreateClient().AsUser("avokat1@ash.local");
        (await lawyer.GetAsync($"{SearchPath}/filters", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task B3_the_result_shows_a_short_snippet_of_a_long_object()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Protokoll);
        var first = await RegisterFirstLevel(client);
        var words = string.Join("  \n ", Enumerable.Range(1, 60).Select(i => $"fjala{i}"));
        await Scalar($"UPDATE cases SET object_text = '{words}' WHERE id = {first.Id}");

        var snippet = Find(await Search(client, ("code", first.CaseCode)), first.CaseCode)!.Value
            .GetProperty("objectSnippet").GetString()!;

        snippet.ShouldEndWith("…");
        snippet.Length.ShouldBeLessThanOrEqualTo(121);
        snippet.ShouldStartWith("fjala1 fjala2 fjala3"); // on one line
        words.Replace("  \n ", " ").ShouldStartWith(snippet[..^1]); // cut at a word
    }

    [Fact]
    public async Task Registration_search_filters_by_status()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Protokoll);
        var closed = await RegisterFirstLevel(client);
        var open = await RegisterFirstLevel(client, close: false);

        var page = await Search(client, ("plaintiff", "oxha"), ("status", "ALL"));
        Find(page, closed.CaseCode).ShouldNotBeNull();
        Find(page, open.CaseCode).ShouldNotBeNull();
        page.GetProperty("total").GetInt32().ShouldBe(page.GetProperty("items").GetArrayLength());

        // task 054d: a search without a status looks in every status; "Mund të vazhdohen" chosen lists no open one
        var searched = await Search(client, ("plaintiff", "oxha"));
        Find(searched, closed.CaseCode).ShouldNotBeNull();
        Find(searched, open.CaseCode).ShouldNotBeNull();
        var continuable = await Search(client, ("plaintiff", "oxha"), ("status", "CONTINUABLE"));
        Find(continuable, closed.CaseCode).ShouldNotBeNull();
        Find(continuable, open.CaseCode).ShouldBeNull();

        var closedOnly = await Search(client, ("plaintiff", "oxha"), ("status", "CLOSED"));
        Find(closedOnly, closed.CaseCode).ShouldNotBeNull();
        Find(closedOnly, open.CaseCode).ShouldBeNull();
        var openOnly = await Search(client, ("plaintiff", "oxha"), ("status", "OPEN"));
        Find(openOnly, open.CaseCode).ShouldNotBeNull();
        Find(openOnly, closed.CaseCode).ShouldBeNull();
    }

    [Fact]
    public async Task Registration_search_shows_an_open_case_that_cannot_be_continued_yet()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Protokoll);
        var open = await RegisterFirstLevel(client, close: false);

        var item = Find(await Search(client, ("actNumber", open.ActNumber), ("status", "ALL")), open.CaseCode);

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

        (await lawyer.GetAsync($"{SearchPath}?defendant=financ", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
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

    // ---------------------------------------------------------------- preview (task 021)

    private static string PreviewPath(Guid previous) => $"/api/cases/{previous}/next-level/preview";

    [Fact]
    public async Task Preview_shows_what_continuing_copies_without_internal_ids_and_is_audited()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Protokoll);
        var first = await RegisterFirstLevel(client);
        // a removed party is not copied, so it is not previewed
        await Scalar($"""
            INSERT INTO case_parties (case_id, party_role_id, party_kind, display_name, created_by, deleted_at, deleted_by)
            SELECT {first.Id}, r.id, 'NATURAL', 'E Hequr', u.id, UTC_TIMESTAMP(3), u.id
              FROM party_roles r JOIN users u ON u.email = 'protokoll@ash.local' WHERE r.code = 'PADITES'
            """);

        var response = await client.GetAsync(PreviewPath(first.PublicId), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var preview = await Json(response);
        JsonAssert.NoNumericIds(preview);
        preview.GetProperty("caseCode").GetString().ShouldBe(first.CaseCode);
        preview.GetProperty("courtLevelName").GetString().ShouldBe("Shkalla e parë");
        preview.GetProperty("courtName").GetString().ShouldBe("Gjykata e Shkallës së Parë Tiranë");
        preview.GetProperty("objectText").GetString().ShouldBe("Kërkesë për shpërblim dëmi — objekti i shkallës së parë");
        preview.GetProperty("parties").EnumerateArray()
            .Select(p => $"{p.GetProperty("role").GetString()}|{p.GetProperty("kind").GetString()}|{p.GetProperty("displayName").GetString()}|{p.GetProperty("identifier").GetString()}")
            .ShouldBe([
                // task 053j: only the copied roles — the third party (Shoqëria Alfa) is not previewed
                "Paditës|NATURAL|Agim Hoxha|",
                $"I paditur|INSTITUTION|{Ministry}|"
            ]);
        var link = preview.GetProperty("institutionLinks").EnumerateArray().Single();
        link.GetProperty("institution").GetString().ShouldBe(Ministry);
        link.GetProperty("mode").GetString().ShouldBe("MONITORING");

        var sql = $"SELECT JSON_UNQUOTE(JSON_EXTRACT(details, '$.Purpose')) FROM ash_audit.audit_log " +
                  $"WHERE action = 'CASE_VIEW' AND case_id = {first.Id}";
        var deadline = DateTime.UtcNow.AddSeconds(15);
        object? purpose;
        while ((purpose = await ScalarAudit(sql)) is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100, Ct);
        }

        purpose.ShouldBe("registration preview");
    }

    [Fact]
    public async Task Preview_is_refused_exactly_like_the_registration()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(Protokoll);

        var open = await RegisterFirstLevel(client, close: false);
        var notClosed = await client.GetAsync(PreviewPath(open.PublicId), Ct);
        notClosed.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Json(notClosed)).GetProperty("code").GetString().ShouldBe("case.previous_not_closed");

        var first = await RegisterFirstLevel(client);
        (await client.PostAsJsonAsync(NextLevelPath(first.PublicId), await NextLevelBody(), Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var continued = await client.GetAsync(PreviewPath(first.PublicId), Ct);
        continued.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Json(continued)).GetProperty("code").GetString().ShouldBe("case.already_continued");

        (await client.GetAsync(PreviewPath(Guid.NewGuid()), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var lawyer = factory.CreateClient().AsUser("avokat1@ash.local");
        (await lawyer.GetAsync(PreviewPath(first.PublicId), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private async Task<object?> ScalarAudit(string sql)
    {
        await using var connection = new MySqlConnection(Db.AuditConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync(Ct);
        return value is DBNull ? null : value;
    }
}
