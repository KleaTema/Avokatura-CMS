using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.SharedKernel.Ids;
using Ash.SharedKernel.Time;
using Ash.Tests.Integration.Infrastructure;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 053d — "Mbikëqyrja" (client decision C, default D3). The Monitor puts the Durrës lawyer (every case where
/// they are the CURRENT primary) and one other Durrës case under the TR Kryetar's supervision. Then every surface —
/// the list ("Në mbikëqyrje" and the whole scope), the case page, the agenda, the dashboard, the header search, the
/// reports and the statistics — shows the Kryetar the same supervised cases, and none of the third Durrës case.
/// </summary>
public class SupervisionTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string Monitor = "monitor@ash.local";
    private const string Kryetar = "kryetar@ash.local";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Ctx(CaseSeed Seed, AshApiFactory Factory, string ByLawyer, string ByCase, string Other,
        Guid ByLawyerId, Guid OtherId) : IAsyncDisposable
    {
        public HttpClient As(string email) => Factory.CreateClient().AsUser(email);

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            await Seed.DisposeAsync();
        }
    }

    /// <summary>Three ACTIVE Durrës cases with a hearing this week: the Durrës lawyer's, one to supervise alone, one unrelated.</summary>
    private async Task<Ctx> SetupAsync()
    {
        var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        var factory = new AshApiFactory(Db);
        var hearing = DateTime.UtcNow.Date.AddHours(9); // today: in this week's agenda
        var dr = seed.OfficeDrId;
        var byLawyer = await seed.AddOfficeCaseAsync(dr, "SUPA", seed.User("drlawyer").Id, hearing);
        var byCase = await seed.AddOfficeCaseAsync(dr, "SUPB", hearingAtUtc: hearing);
        var other = await seed.AddOfficeCaseAsync(dr, "SUPC", hearingAtUtc: hearing);
        return new Ctx(seed, factory, byLawyer.Code, byCase.Code, other.Code,
            await PublicIdOf(byLawyer.Id), await PublicIdOf(other.Id));
    }

    // ---------------------------------------------------------------- helpers

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(Ct);
    }

    private async Task<Guid> PublicIdOf(long caseId) =>
        PublicId.FromBytes((byte[])(await Scalar($"SELECT public_id FROM cases WHERE id = {caseId}"))!);

    private async Task<Guid> UserPublicId(long userId) =>
        PublicId.FromBytes((byte[])(await Scalar($"SELECT public_id FROM users WHERE id = {userId}"))!);

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.IsSuccessStatusCode.ShouldBeTrue(text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<HttpResponseMessage> Supervise(HttpClient monitor, object body) =>
        await monitor.PostAsJsonAsync("/api/supervisions", body, Ct);

    /// <summary>Supervises the Durrës lawyer and the second case; returns the lawyer supervision's public id.</summary>
    private async Task<Guid> SuperviseBothAsync(Ctx s)
    {
        using var monitor = s.As(Monitor);
        var head = await UserPublicId(s.Seed.User("kryetar").Id);
        var lawyer = await Json(await Supervise(monitor, new
        {
            headUserPublicId = head, kind = "LAWYER", lawyerUserPublicId = await UserPublicId(s.Seed.User("drlawyer").Id),
        }));
        lawyer.GetProperty("coveredCases").GetInt32().ShouldBe(1);
        await Json(await Supervise(monitor, new { headUserPublicId = head, kind = "CASE", caseCode = s.ByCase }));
        return lawyer.GetProperty("publicId").GetGuid();
    }

    private static List<string> Codes(JsonElement items, string property = "caseCode") =>
        items.EnumerateArray().Select(i => i.GetProperty(property).GetString()!).ToList();

    private static async Task<List<string>> ListCodes(HttpClient client, string tab) =>
        Codes((await Json(await client.GetAsync($"/api/cases?tab={tab}&status=ALL&limit=100", Ct))).GetProperty("items"));

    /// <summary>The three test cases each surface shows the Kryetar (only the S-SUP… codes count).</summary>
    private static List<string> Ours(IEnumerable<string> codes) => codes.Where(c => c.StartsWith("S-SUP")).Order().ToList();

    // ---------------------------------------------------------------- the surfaces

    [Fact]
    public async Task Every_surface_shows_the_supervised_Kryetar_the_same_cases()
    {
        await using var s = await SetupAsync();
        using var kryetar = s.As(Kryetar);
        // before: none of the Durrës cases
        Ours(await ListCodes(kryetar, "office")).ShouldBeEmpty();
        (await kryetar.GetAsync($"/api/cases/{s.ByLawyerId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await SuperviseBothAsync(s);
        string[] expected = [s.ByLawyer, s.ByCase];
        var supervised = expected.Order().ToList();

        // the list: "Në mbikëqyrje" exactly; "Të zyrës" still only the office
        Ours(await ListCodes(kryetar, "supervised")).ShouldBe(supervised);
        Ours(await ListCodes(kryetar, "office")).ShouldBeEmpty();
        var tabs = await Json(await kryetar.GetAsync("/api/cases/tabs", Ct));
        tabs.GetProperty("tabs").EnumerateArray().Single(t => t.GetProperty("tab").GetString() == "supervised")
            .GetProperty("count").GetInt32().ShouldBe(2);

        // the case page: viewable, with the Kryetar's actions (transfer, priority, the decision rights)
        var detail = await Json(await kryetar.GetAsync($"/api/cases/{s.ByLawyerId}", Ct));
        var permissions = detail.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ToList();
        permissions.ShouldContain("case.transfer");
        permissions.ShouldContain("case.set_priority_any");
        permissions.ShouldContain("decision.record");
        permissions.ShouldNotContain("case.edit_registration"); // the registration desk stays with the own office
        (await kryetar.GetAsync($"/api/cases/{s.OtherId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        // the transfer picker: the case office's lawyers (himself only with a lawyer role, task 053i — he has none)
        var lawyers = await Json(await kryetar.GetAsync($"/api/cases/{s.ByLawyerId}/assignable-lawyers", Ct));
        lawyers.EnumerateArray().ShouldNotContain(l => l.GetProperty("isSelf").GetBoolean());
        lawyers.EnumerateArray().ShouldContain(l => l.GetProperty("fullName").GetString() == "Dr Lawyer");

        // the agenda (this week): the supervised hearings, and with the supervised lawyer as filter
        var monday = TiraneTime.Today(new SystemClock());
        var range = $"from={monday.AddDays(-1):yyyy-MM-dd}&to={monday.AddDays(1):yyyy-MM-dd}";
        Ours(Codes(await Json(await kryetar.GetAsync($"/api/agenda?{range}", Ct)))).ShouldBe(supervised);
        var drLawyer = await UserPublicId(s.Seed.User("drlawyer").Id);
        Ours(Codes(await Json(await kryetar.GetAsync($"/api/agenda?{range}&lawyerId={drLawyer}", Ct)))).ShouldBe([s.ByLawyer]);

        // the dashboard
        var widgets = (await Json(await kryetar.GetAsync("/api/dashboard", Ct))).GetProperty("widgets").EnumerateArray().ToList();
        widgets.Single(w => w.GetProperty("code").GetString() == "supervisedOpen").GetProperty("value").GetInt64().ShouldBe(2);

        // the header search
        var search = await Json(await kryetar.GetAsync("/api/search?q=SUP&limit=20", Ct));
        Ours(Codes(search.GetProperty("items"))).ShouldBe(supervised);

        // the reports (Tabela 1) and the statistics (the active cases of this year)
        var report = await Json(await kryetar.GetAsync("/api/reports/active-cases?limit=100", Ct));
        Ours(Codes(report.GetProperty("items"))).ShouldBe(supervised);
        var year = DateTime.UtcNow.Year;
        var stats = await Json(await kryetar.GetAsync($"/api/reports/statistics/cases?year={year}&table=5&measure=aktive&limit=100", Ct));
        Ours(Codes(stats.GetProperty("items"))).ShouldBe(supervised);

        // the Monitor still sees everything; another Kryetar-less user nothing new
        using var monitor = s.As(Monitor);
        Ours(await ListCodes(monitor, "all")).ShouldBe(new[] { s.ByLawyer, s.ByCase, s.Other }.Order().ToList());
    }

    [Fact]
    public async Task A_lawyer_supervision_follows_the_current_primary_and_ending_it_removes_the_cases()
    {
        await using var s = await SetupAsync();
        var lawyerSupervision = await SuperviseBothAsync(s);
        using var kryetar = s.As(Kryetar);

        // a later case of the supervised lawyer joins at once
        var later = await s.Seed.AddOfficeCaseAsync(s.Seed.OfficeDrId, "SUPD", s.Seed.User("drlawyer").Id);
        Ours(await ListCodes(kryetar, "supervised")).ShouldBe(new[] { s.ByLawyer, s.ByCase, later.Code }.Order().ToList());

        // the lawyer is no longer primary of the first case (transferred): it leaves
        await Scalar($"UPDATE case_assignments SET ended_at = UTC_TIMESTAMP(3), end_reason = 'TRANSFERRED' WHERE case_id = (SELECT id FROM cases WHERE case_code = '{s.ByLawyer}')");
        Ours(await ListCodes(kryetar, "supervised")).ShouldBe(new[] { s.ByCase, later.Code }.Order().ToList());

        // ended: the lawyer's cases leave everywhere; the single case stays
        using var monitor = s.As(Monitor);
        var ended = await Json(await monitor.PostAsync($"/api/supervisions/{lawyerSupervision}/end", null, Ct));
        ended.GetProperty("endedAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
        Ours(await ListCodes(kryetar, "supervised")).ShouldBe([s.ByCase]);
        (await kryetar.GetAsync($"/api/cases/{await PublicIdOf(later.Id)}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await monitor.PostAsync($"/api/supervisions/{lawyerSupervision}/end", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // the list of the Monitor: active only by default, the ended one with includeEnded
        (await Json(await monitor.GetAsync("/api/supervisions", Ct))).GetArrayLength().ShouldBe(1);
        (await Json(await monitor.GetAsync("/api/supervisions?includeEnded=true", Ct))).GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task The_Kryetar_is_told_and_a_case_supervision_is_in_the_case_history()
    {
        await using var s = await SetupAsync();
        await SuperviseBothAsync(s);

        var kryetarId = s.Seed.User("kryetar").Id;
        (await Scalar($"SELECT COUNT(*) FROM notifications WHERE user_id = {kryetarId} AND notification_type = 'SUPERVISION_ASSIGNED'"))
            .ShouldBe(2L);
        ((string)(await Scalar($"SELECT body FROM notifications WHERE user_id = {kryetarId} AND case_id IS NULL AND notification_type = 'SUPERVISION_ASSIGNED'"))!)
            .ShouldContain("Dr Lawyer");
        (await Scalar($"SELECT COUNT(*) FROM case_history_events h JOIN cases c ON c.id = h.case_id WHERE c.case_code = '{s.ByCase}' AND h.event_type = 'SUPERVISION_STARTED'"))
            .ShouldBe(1L);
        // a lawyer supervision writes no case history
        (await Scalar($"SELECT COUNT(*) FROM case_history_events h JOIN cases c ON c.id = h.case_id WHERE c.case_code = '{s.ByLawyer}' AND h.event_type = 'SUPERVISION_STARTED'"))
            .ShouldBe(0L);
    }

    [Fact]
    public async Task Only_the_Monitor_supervises_and_the_rules_are_checked()
    {
        await using var s = await SetupAsync();
        using var monitor = s.As(Monitor);
        var head = await UserPublicId(s.Seed.User("kryetar").Id);

        // the Kryetar's own office lawyer / case: already theirs → 400
        (await Supervise(monitor, new { headUserPublicId = head, kind = "LAWYER", lawyerUserPublicId = await UserPublicId(s.Seed.User("avokat1").Id) }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var ownCase = (string)(await Scalar($"SELECT case_code FROM cases WHERE id = {s.Seed.ActiveCaseId}"))!;
        (await Supervise(monitor, new { headUserPublicId = head, kind = "CASE", caseCode = ownCase })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        // not a Kryetar, an unknown case, no kind
        (await Supervise(monitor, new { headUserPublicId = await UserPublicId(s.Seed.User("avokat1").Id), kind = "CASE", caseCode = s.ByCase }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Supervise(monitor, new { headUserPublicId = head, kind = "CASE", caseCode = "NUK-EKZISTON/1" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Supervise(monitor, new { headUserPublicId = head })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // twice → 409
        (await Supervise(monitor, new { headUserPublicId = head, kind = "CASE", caseCode = s.ByCase })).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await Supervise(monitor, new { headUserPublicId = head, kind = "CASE", caseCode = s.ByCase })).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // the Kryetar and the lawyers may not
        using var kryetar = s.As(Kryetar);
        (await kryetar.GetAsync("/api/supervisions", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Supervise(kryetar, new { headUserPublicId = head, kind = "CASE", caseCode = s.Other })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var people = await Json(await monitor.GetAsync("/api/supervisions/people", Ct));
        people.GetProperty("heads").EnumerateArray().ShouldContain(h => h.GetProperty("publicId").GetGuid() == head);
        people.GetProperty("lawyers").EnumerateArray().ShouldContain(l => l.GetProperty("name").GetString() == "Dr Lawyer");
    }
}
