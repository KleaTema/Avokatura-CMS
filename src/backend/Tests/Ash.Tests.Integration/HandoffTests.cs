using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.Modules.Notifications.Application.Email;
using Ash.Modules.Notifications.Contracts;
using Ash.SharedKernel.Ids;
using Ash.SharedKernel.Time;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 053c — "Përcjellja e çështjes" (client decision B.3, default D5): closing a level that is not final hands it
/// to the next court and its office; the office's Protokoll are told (notification + e-mail through the outbox) and
/// find it in "Çështjet e ardhura" until X/n+1 is registered. <see cref="CaseSeed"/>'s ACTIVE first-instance case of
/// office TR (avokat1 primary). The appeal court is mapped to the seed's Durrës office, which gets two active
/// Protokoll and an inactive one.
/// </summary>
public class HandoffTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string Primary = "avokat1@ash.local";
    private const string TrProtokoll = "protokoll@ash.local";
    private const string HighCourt = "Gjykata e Lartë";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Records every e-mail; fails the first <see cref="FailFirst"/> sends.</summary>
    private sealed class FakeEmailSender : IEmailSender
    {
        public ConcurrentQueue<EmailMessage> Sent { get; } = new();
        public int FailFirst { get; set; }
        private int _calls;

        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _calls) <= FailFirst)
            {
                throw new InvalidOperationException("SMTP down (test)");
            }

            Sent.Enqueue(message);
            return Task.CompletedTask;
        }
    }

    private sealed record Ctx(
        CaseSeed Seed, AshApiFactory Factory, FakeEmailSender Mail, Guid Case, int AppealCourt, int HighCourtId,
        string DrProtokoll1, string DrProtokoll2, string DrInactive) : IAsyncDisposable
    {
        public HttpClient As(string email) => Factory.CreateClient().AsUser(email);
        public long Id => Seed.ActiveCaseId;

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            await Seed.DisposeAsync();
        }
    }

    private async Task<Ctx> SetupAsync()
    {
        var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var mail = new FakeEmailSender();
        var factory = new AshApiFactory(Db, configureServices: services => services.AddSingleton<IEmailSender>(mail));
        var id = PublicId.FromBytes((byte[])(await Scalar($"SELECT public_id FROM cases WHERE id = {seed.ActiveCaseId}"))!);
        var dr = await Scalar($"SELECT code FROM offices WHERE id = {seed.OfficeDrId}");
        var appeal = await HandoffTestData.MapAppealCourtAsync(Db.AppConnectionString, (string)dr!);
        var high = Convert.ToInt32(await Scalar($"SELECT id FROM courts WHERE name = '{HighCourt}'"));
        var p1 = await seed.AddOfficeUserAsync("drprot1", seed.OfficeDrId, "PROTOCOL");
        var p2 = await seed.AddOfficeUserAsync("drprot2", seed.OfficeDrId, "PROTOCOL");
        var inactive = await seed.AddOfficeUserAsync("drprot3", seed.OfficeDrId, "PROTOCOL", active: false);
        return new Ctx(seed, factory, mail, id, appeal, high, p1, p2, inactive);
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private static async Task<uint> CaseRowVersion(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"/api/cases/{id}", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await Json(response)).GetProperty("header").GetProperty("rowVersion").GetUInt32();
    }

    private static async Task<HttpResponseMessage> Close(HttpClient client, Guid id, params (string Key, object? Value)[] fields)
    {
        var body = new Dictionary<string, object?>
        {
            ["resultCode"] = "HUMBUR",
            ["decisionNumber"] = "V-77",
            ["decisionDate"] = DateTime.UtcNow.AddDays(-2).ToString("yyyy-MM-dd"),
            ["decisionReceived"] = false,
            ["isAppealed"] = true,
            ["rowVersion"] = await CaseRowVersion(client, id),
        };
        foreach (var (key, value) in fields)
        {
            body[key] = value;
        }

        return await client.PostAsJsonAsync($"/api/cases/{id}/decision", body, Ct);
    }

    private static async Task<string> Expect(HttpResponseMessage response, HttpStatusCode status)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(status, text);
        return text;
    }

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(Ct);
    }

    private async Task<long> Count(string sql) => Convert.ToInt64(await Scalar($"SELECT COUNT(*) {sql}"));

    private Task<long> NotificationsOf(string email, string type) =>
        Count($"FROM notifications n JOIN users u ON u.id = n.user_id WHERE u.email = '{email}' AND n.notification_type = '{type}'");

    private Task<long> MailsTo(string email) => Count($"FROM email_outbox WHERE to_address = '{email}'");

    private static async Task<JsonElement> Incoming(HttpClient client)
    {
        var response = await client.GetAsync("/api/cases/handoffs/incoming", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await Json(response);
    }

    private static async Task<int?> IncomingTabCount(HttpClient client)
    {
        var tabs = await Json(await client.GetAsync("/api/cases/tabs", Ct));
        var tab = tabs.GetProperty("tabs").EnumerateArray().FirstOrDefault(t => t.GetProperty("tab").GetString() == "incoming");
        return tab.ValueKind == JsonValueKind.Undefined ? null : tab.GetProperty("count").GetInt32();
    }

    private async Task<Guid> OfficePublicId(long officeId) =>
        PublicId.FromBytes((byte[])(await Scalar($"SELECT public_id FROM offices WHERE id = {officeId}"))!);

    // ---------------------------------------------------------------- closing

    [Fact]
    public async Task Closing_not_final_without_the_destination_is_400_and_final_with_one_is_400()
    {
        await using var s = await SetupAsync();
        using var lawyer = s.As(Primary);

        var missing = await Expect(await Close(lawyer, s.Case), HttpStatusCode.BadRequest);
        missing.ShouldContain("Zgjidhni shkallën dhe gjykatën ku përcillet çështja.");
        missing.ShouldContain("\"nextCourtLevelCode\"");
        missing.ShouldContain("\"nextCourtId\"");

        // a court of another level
        var wrongLevel = await Expect(await Close(lawyer, s.Case, ("nextCourtLevelCode", "FIRST_INSTANCE"), ("nextCourtId", s.AppealCourt)),
            HttpStatusCode.BadRequest);
        wrongLevel.ShouldContain("Gjykata nuk është e kësaj shkalle.");

        var final = await Expect(await Close(lawyer, s.Case, ("isAppealed", false), ("isFinal", true),
            ("nextCourtLevelCode", "APPEAL"), ("nextCourtId", s.AppealCourt)), HttpStatusCode.BadRequest);
        final.ShouldContain("handoff.not_for_final");

        (await Scalar($"SELECT lifecycle_status FROM cases WHERE id = {s.Id}")).ShouldBe("ACTIVE"); // nothing happened
        (await Count("FROM case_handoffs")).ShouldBe(0L);

        // final without a destination closes, with no handoff
        await Expect(await Close(lawyer, s.Case, ("isAppealed", false), ("isFinal", true)), HttpStatusCode.OK);
        (await Count("FROM case_handoffs")).ShouldBe(0L);
    }

    [Fact]
    public async Task Handoff_tells_every_active_Protokoll_of_the_receiving_office_only_and_shows_the_case_as_incoming()
    {
        await using var s = await SetupAsync();
        using var lawyer = s.As(Primary);

        await Expect(await Close(lawyer, s.Case, ("nextCourtLevelCode", "APPEAL"), ("nextCourtId", s.AppealCourt)), HttpStatusCode.OK);

        (await Scalar($"SELECT CONCAT(to_office_id, '|', court_id) FROM case_handoffs WHERE from_case_id = {s.Id} AND accepted_case_id IS NULL AND cancelled_at IS NULL"))
            .ShouldBe($"{s.Seed.OfficeDrId}|{s.AppealCourt}");
        (await Count($"FROM case_history_events WHERE case_id = {s.Id} AND event_type = 'CASE_HANDED_OFF'")).ShouldBe(1L);

        // notification + e-mail for each ACTIVE Durrës Protokoll; nothing for the inactive one or the TR Protokoll
        foreach (var protokoll in new[] { s.DrProtokoll1, s.DrProtokoll2 })
        {
            (await NotificationsOf(protokoll, NotificationTypes.CaseHandedOff)).ShouldBe(1L, protokoll);
            (await MailsTo(protokoll)).ShouldBe(1L, protokoll);
        }

        (await NotificationsOf(s.DrInactive, NotificationTypes.CaseHandedOff)).ShouldBe(0L);
        (await MailsTo(s.DrInactive)).ShouldBe(0L);
        (await NotificationsOf(TrProtokoll, NotificationTypes.CaseHandedOff)).ShouldBe(0L);
        (await MailsTo(TrProtokoll)).ShouldBe(0L);
        (await Scalar($"SELECT title FROM notifications n JOIN users u ON u.id = n.user_id WHERE u.email = '{s.DrProtokoll1}'"))
            .ShouldBe("Ju është dhënë akses në një çështje ekzistuese");
        var body = (string)(await Scalar($"SELECT body_text FROM email_outbox WHERE to_address = '{s.DrProtokoll1}'"))!;
        body.ShouldContain("Mund të mos e keni marrë ende fletëthirrjen");
        body.ShouldContain("Çështjet e ardhura");
        (await Scalar("SELECT GROUP_CONCAT(DISTINCT status) FROM email_outbox")).ShouldBe("PENDING"); // sent after commit by the dispatcher

        // "Çështjet e ardhura": registration-level data only
        using var dr = s.As(s.DrProtokoll1);
        var incoming = await Incoming(dr);
        incoming.GetProperty("total").GetInt32().ShouldBe(1);
        var item = incoming.GetProperty("items").EnumerateArray().Single();
        // public ids only (courtId is a reference list id, as in the registration form)
        item.TryGetProperty("caseId", out _).ShouldBeFalse();
        item.TryGetProperty("id", out _).ShouldBeFalse();
        item.GetProperty("casePublicId").GetGuid().ShouldBe(s.Case);
        item.GetProperty("courtLevelCode").GetString().ShouldBe("APPEAL");
        item.GetProperty("courtName").GetString().ShouldBe(HandoffTestData.AppealCourt);
        item.GetProperty("resultName").GetString().ShouldNotBeNullOrEmpty();
        item.GetProperty("objectText").GetString()
            .ShouldBe((string)(await Scalar($"SELECT object_text FROM cases WHERE id = {s.Id}"))!);
        item.GetProperty("partyNames").ValueKind.ShouldBe(JsonValueKind.Array);
        item.TryGetProperty("documents", out _).ShouldBeFalse();
        (await IncomingTabCount(dr)).ShouldBe(1);

        // the receiving Protokoll does not get the case itself (no documents, no chat)
        (await dr.GetAsync($"/api/cases/{s.Case}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // other offices see nothing
        using var tr = s.As(TrProtokoll);
        (await Incoming(tr)).GetProperty("total").GetInt32().ShouldBe(0);
        (await IncomingTabCount(tr)).ShouldBe(0);
        // a lawyer has no incoming list
        (await lawyer.GetAsync("/api/cases/handoffs/incoming", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // the case page shows the open handoff
        var view = await Json(await lawyer.GetAsync($"/api/cases/{s.Case}/handoff", Ct));
        view.GetProperty("office").GetProperty("name").GetString().ShouldStartWith("Zyra Durrës");
        view.GetProperty("canChange").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task An_unmapped_court_needs_the_office_and_a_reason_a_mapped_one_refuses_another_office()
    {
        await using var s = await SetupAsync();
        using var lawyer = s.As(Primary);
        var tr = await OfficePublicId(s.Seed.OfficeTrId);

        var destination = await Json(await lawyer.GetAsync($"/api/cases/handoff-destination?courtId={s.HighCourtId}", Ct));
        destination.GetProperty("office").ValueKind.ShouldBe(JsonValueKind.Null);
        destination.GetProperty("offices").GetArrayLength().ShouldBeGreaterThan(1);
        var mapped = await Json(await lawyer.GetAsync($"/api/cases/handoff-destination?courtId={s.AppealCourt}", Ct));
        mapped.GetProperty("office").GetProperty("name").GetString().ShouldStartWith("Zyra Durrës");

        var high = new (string, object?)[] { ("nextCourtLevelCode", "HIGH_COURT"), ("nextCourtId", s.HighCourtId) };
        var missing = await Expect(await Close(lawyer, s.Case, high), HttpStatusCode.BadRequest);
        missing.ShouldContain("\"receivingOfficePublicId\"");
        missing.ShouldContain("\"reason\"");
        await Expect(await Close(lawyer, s.Case, [.. high, ("receivingOfficePublicId", tr), ("handoffReason", "shkurt")]),
            HttpStatusCode.BadRequest); // reason too short

        // a mapped court decides the office itself
        var other = await Expect(await Close(lawyer, s.Case, ("nextCourtLevelCode", "APPEAL"), ("nextCourtId", s.AppealCourt),
            ("receivingOfficePublicId", tr)), HttpStatusCode.BadRequest);
        other.ShouldContain("zyra nuk zgjidhet");

        await Expect(await Close(lawyer, s.Case, [.. high, ("receivingOfficePublicId", tr),
            ("handoffReason", "Gjykata e Lartë nuk ka zyrë në listë")]), HttpStatusCode.OK);
        (await Scalar($"SELECT CONCAT(to_office_id, '|', override_reason) FROM case_handoffs WHERE from_case_id = {s.Id}"))
            .ShouldBe($"{s.Seed.OfficeTrId}|Gjykata e Lartë nuk ka zyrë në listë");
        (await NotificationsOf(TrProtokoll, NotificationTypes.CaseHandedOff)).ShouldBe(1L);
        (await NotificationsOf(s.DrProtokoll1, NotificationTypes.CaseHandedOff)).ShouldBe(0L);
    }

    [Fact]
    public async Task Correcting_the_destination_moves_the_case_to_the_new_office_and_tells_both()
    {
        await using var s = await SetupAsync();
        using var lawyer = s.As(Primary);
        await Expect(await Close(lawyer, s.Case, ("nextCourtLevelCode", "APPEAL"), ("nextCourtId", s.AppealCourt)), HttpStatusCode.OK);
        var tr = await OfficePublicId(s.Seed.OfficeTrId);
        var change = new
        {
            nextCourtLevelCode = "HIGH_COURT", nextCourtId = s.HighCourtId, receivingOfficePublicId = tr,
            reason = "Rekurs në Gjykatën e Lartë, jo apel",
        };

        // the Protokoll (no decision.record, no view) cannot; a lawyer of another office neither
        using var trProtokoll = s.As(TrProtokoll);
        (await trProtokoll.PutAsJsonAsync($"/api/cases/{s.Case}/handoff", change, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // the secondary lawyer may (decision.record, D1)
        using var secondary = s.As("avokat2@ash.local");
        var changed = await Json(await secondary.PutAsJsonAsync($"/api/cases/{s.Case}/handoff", change, Ct));
        changed.GetProperty("courtLevelCode").GetString().ShouldBe("HIGH_COURT");

        (await Count($"FROM case_handoffs WHERE from_case_id = {s.Id}")).ShouldBe(2L);
        (await Scalar($"SELECT cancel_reason FROM case_handoffs WHERE from_case_id = {s.Id} AND cancelled_at IS NOT NULL"))
            .ShouldBe("Destinacioni i përcjelljes u ndryshua");
        (await Count($"FROM case_history_events WHERE case_id = {s.Id} AND event_type = 'HANDOFF_CHANGED'")).ShouldBe(1L);
        (await NotificationsOf(s.DrProtokoll1, NotificationTypes.HandoffWithdrawn)).ShouldBe(1L);
        (await NotificationsOf(TrProtokoll, NotificationTypes.CaseHandedOff)).ShouldBe(1L);
        (await MailsTo(s.DrProtokoll1)).ShouldBe(2L); // handed off, then withdrawn
        (await MailsTo(TrProtokoll)).ShouldBe(1L);

        using var dr = s.As(s.DrProtokoll1);
        (await Incoming(dr)).GetProperty("total").GetInt32().ShouldBe(0);
        (await Incoming(trProtokoll)).GetProperty("total").GetInt32().ShouldBe(1);

        // the same destination again: nothing new
        await secondary.PutAsJsonAsync($"/api/cases/{s.Case}/handoff", change, Ct);
        (await Count($"FROM case_handoffs WHERE from_case_id = {s.Id}")).ShouldBe(2L);
    }

    [Fact]
    public async Task The_decision_becoming_final_cancels_the_open_handoff_and_tells_the_receiving_Protokoll()
    {
        await using var s = await SetupAsync();
        using var lawyer = s.As(Primary);
        await Expect(await Close(lawyer, s.Case, ("nextCourtLevelCode", "APPEAL"), ("nextCourtId", s.AppealCourt)), HttpStatusCode.OK);
        var decision = await Json(await lawyer.GetAsync($"/api/cases/{s.Case}", Ct));
        var rowVersion = decision.GetProperty("decision").GetProperty("rowVersion").GetUInt32();

        var update = await lawyer.PutAsJsonAsync($"/api/cases/{s.Case}/decision", new
        {
            resultCode = "HUMBUR", decisionNumber = "V-77", decisionReceived = false, isAppealed = false, isFinal = true, rowVersion,
        }, Ct);
        await Expect(update, HttpStatusCode.OK);

        (await Scalar($"SELECT cancel_reason FROM case_handoffs WHERE from_case_id = {s.Id}")).ShouldBe("Vendimi mori formë të prerë");
        (await Scalar($"SELECT cancelled_by IS NULL FROM case_handoffs WHERE from_case_id = {s.Id}")).ShouldBe(1L);
        (await Count($"FROM case_history_events WHERE case_id = {s.Id} AND event_type = 'HANDOFF_CANCELLED'")).ShouldBe(1L);
        (await NotificationsOf(s.DrProtokoll2, NotificationTypes.HandoffWithdrawn)).ShouldBe(1L);
        using var dr = s.As(s.DrProtokoll1);
        (await Incoming(dr)).GetProperty("total").GetInt32().ShouldBe(0);
        (await lawyer.GetAsync($"/api/cases/{s.Case}/handoff", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Registering_the_next_level_accepts_the_handoff()
    {
        await using var s = await SetupAsync();
        using var lawyer = s.As(Primary);
        await Expect(await Close(lawyer, s.Case, ("nextCourtLevelCode", "APPEAL"), ("nextCourtId", s.AppealCourt)), HttpStatusCode.OK);
        // the Durrës office needs its Kryetar to register (the new level goes to them)
        var headEmail = await s.Seed.AddOfficeUserAsync("drhead", s.Seed.OfficeDrId, "OFFICE_HEAD");
        await Scalar($"UPDATE offices SET head_user_id = (SELECT id FROM users WHERE email = '{headEmail}') WHERE id = {s.Seed.OfficeDrId}");
        await using var seed = await Seed.CreateAsync(Db.AppConnectionString);
        using var dr = s.As(s.DrProtokoll1);

        var next = await dr.PostAsJsonAsync($"/api/cases/{s.Case}/next-level", new Dictionary<string, object?>
        {
            ["summonsNumber"] = $"FL-H-{Guid.NewGuid():N}"[..18],
            ["actNumber"] = $"AKT-H-{Guid.NewGuid():N}"[..18],
            ["receivedDate"] = TiraneTime.Today(new SystemClock()).ToString("yyyy-MM-dd"),
            ["courtLevelCode"] = "APPEAL",
            ["courtId"] = s.AppealCourt,
            ["judgeId"] = await seed.JudgeAsync("Genc", "Muçaj"),
            ["firstHearing"] = new { at = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-ddTHH:mm:ssZ") },
        }, Ct);
        var text = await Expect(next, HttpStatusCode.Created);
        var nextCode = JsonDocument.Parse(text).RootElement.GetProperty("caseCode").GetString();

        (await Scalar($"SELECT c.case_code FROM case_handoffs h JOIN cases c ON c.id = h.accepted_case_id WHERE h.from_case_id = {s.Id}"))
            .ShouldBe(nextCode);
        (await Incoming(dr)).GetProperty("total").GetInt32().ShouldBe(0);
        (await IncomingTabCount(dr)).ShouldBe(0);
        // accepted: no more corrections
        var tr = await OfficePublicId(s.Seed.OfficeTrId);
        (await lawyer.PutAsJsonAsync($"/api/cases/{s.Case}/handoff", new
        {
            nextCourtLevelCode = "HIGH_COURT", nextCourtId = s.HighCourtId, receivingOfficePublicId = tr, reason = "Rekurs në Gjykatën e Lartë",
        }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    // ---------------------------------------------------------------- court → office mapping (admin)

    [Fact]
    public async Task Admin_maps_courts_to_an_office_one_active_office_per_court()
    {
        await using var s = await SetupAsync(); // the appeal court is already Durrës's
        using var admin = s.As("admin@ash.local");
        var tr = await OfficePublicId(s.Seed.OfficeTrId);
        var dr = await OfficePublicId(s.Seed.OfficeDrId);

        // TR may not take Durrës's court
        var taken = await admin.PutAsJsonAsync($"/api/identity/admin/offices/{tr}/courts", new { courtIds = new[] { s.AppealCourt, s.HighCourtId } }, Ct);
        (await Expect(taken, HttpStatusCode.Conflict)).ShouldContain(HandoffTestData.AppealCourt);

        var set = await Json(await admin.PutAsJsonAsync($"/api/identity/admin/offices/{tr}/courts", new { courtIds = new[] { s.HighCourtId } }, Ct));
        set.EnumerateArray().Select(m => m.GetProperty("courtId").GetInt32()).ShouldBe([s.HighCourtId]);
        var all = await Json(await admin.GetAsync("/api/identity/admin/office-courts", Ct));
        all.EnumerateArray().Select(m => $"{m.GetProperty("courtId").GetInt32()}|{m.GetProperty("officePublicId").GetGuid()}")
            .OrderBy(x => x).ShouldBe(new[] { $"{s.AppealCourt}|{dr}", $"{s.HighCourtId}|{tr}" }.OrderBy(x => x));

        // Durrës gives the appeal court up; TR takes it (the old row stays, inactive)
        await Expect(await admin.PutAsJsonAsync($"/api/identity/admin/offices/{dr}/courts", new { courtIds = Array.Empty<int>() }, Ct), HttpStatusCode.OK);
        await Expect(await admin.PutAsJsonAsync($"/api/identity/admin/offices/{tr}/courts", new { courtIds = new[] { s.HighCourtId, s.AppealCourt } }, Ct), HttpStatusCode.OK);
        (await Count($"FROM office_courts WHERE court_id = {s.AppealCourt}")).ShouldBe(2L);
        using var lawyer = s.As(Primary);
        (await Json(await lawyer.GetAsync($"/api/cases/handoff-destination?courtId={s.AppealCourt}", Ct)))
            .GetProperty("office").GetProperty("publicId").GetGuid().ShouldBe(tr);

        // admin only
        (await lawyer.PutAsJsonAsync($"/api/identity/admin/offices/{tr}/courts", new { courtIds = Array.Empty<int>() }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ---------------------------------------------------------------- e-mail outbox

    [Fact]
    public async Task The_dispatcher_sends_after_commit_retries_a_failure_and_gives_up_after_the_last_attempt()
    {
        await using var s = await SetupAsync();
        using var lawyer = s.As(Primary);
        await Expect(await Close(lawyer, s.Case, ("nextCourtLevelCode", "APPEAL"), ("nextCourtId", s.AppealCourt)), HttpStatusCode.OK);
        var dispatcher = s.Factory.Services.GetRequiredService<EmailDispatcher>();
        var options = s.Factory.Services.GetRequiredService<EmailOptions>();

        // the first send fails: that mail waits (attempts 1, a later next_attempt_at); the other is sent
        s.Mail.FailFirst = 1;
        (await dispatcher.RunOnceAsync(Ct)).ShouldBe(1);
        (await Scalar("SELECT GROUP_CONCAT(CONCAT(status, ':', attempts) ORDER BY status) FROM email_outbox")).ShouldBe("PENDING:1,SENT:0");
        (await Scalar("SELECT next_attempt_at > UTC_TIMESTAMP(3) FROM email_outbox WHERE status = 'PENDING'")).ShouldBe(1L);
        s.Mail.Sent.Single().Subject.ShouldContain("Ju është dhënë akses");

        // not due yet: nothing happens
        (await dispatcher.RunOnceAsync(Ct)).ShouldBe(0);

        // due again, failing until the last attempt → FAILED with the error
        s.Mail.FailFirst = int.MaxValue;
        for (var attempt = 2; attempt <= options.MaxAttempts; attempt++)
        {
            await Scalar("UPDATE email_outbox SET next_attempt_at = UTC_TIMESTAMP(3) - INTERVAL 1 SECOND WHERE status = 'PENDING'");
            await dispatcher.RunOnceAsync(Ct);
        }

        (await Scalar("SELECT CONCAT(status, ':', attempts, ':', last_error) FROM email_outbox WHERE status <> 'SENT'"))
            .ShouldBe($"FAILED:{options.MaxAttempts}:SMTP down (test)");
    }

    [Fact]
    public void Retry_delay_grows_to_an_hour()
    {
        EmailDispatcher.RetryDelay(1).ShouldBe(TimeSpan.FromMinutes(1));
        EmailDispatcher.RetryDelay(3).ShouldBe(TimeSpan.FromMinutes(4));
        EmailDispatcher.RetryDelay(20).ShouldBe(TimeSpan.FromHours(1));
    }
}
