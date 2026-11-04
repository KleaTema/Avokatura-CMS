using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.Modules.Documents.Contracts;
using Ash.SharedKernel.Time;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 046 — the dashboard, over <see cref="DashboardSeed"/> (5,000 cases) at a fixed "now" (the API's IClock).
/// The core rule: every number on a card equals the total of the list its link opens — checked by following each
/// link through GET /api/cases.
/// </summary>
public class DashboardTests(MySqlFixture db) : IntegrationTest(db)
{
    /// <summary>A Wednesday, 11:00 in Tirane.</summary>
    public static readonly DateTime Now = new(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FixedClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }

    private AshApiFactory Factory() =>
        new(Db, configureServices: services => services.AddSingleton<IClock>(new FixedClock(Now)));

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task<List<JsonElement>> Widgets(HttpClient client) =>
        (await Json(await client.GetAsync("/api/dashboard", Ct))).GetProperty("widgets").EnumerateArray().ToList();

    /// <summary>The app's list URL → the API's query (the frontend's short names → the API's).</summary>
    public static string ApiQuery(string linkPath)
    {
        linkPath.ShouldStartWith("/cases?");
        var names = new Dictionary<string, string>
        {
            ["from"] = "hearingFrom", ["to"] = "hearingTo", ["regFrom"] = "registeredFrom", ["regTo"] = "registeredTo",
            ["lawyer"] = "lawyerId", ["office"] = "officeId",
        };
        var parts = linkPath["/cases?".Length..].Split('&').Select(p =>
        {
            var (name, value) = (p[..p.IndexOf('=')], p[(p.IndexOf('=') + 1)..]);
            return $"{names.GetValueOrDefault(name, name)}={value}";
        });
        return $"/api/cases?{string.Join('&', parts)}&limit=1";
    }

    private static async Task<long> ListTotal(HttpClient client, string linkPath) =>
        (await Json(await client.GetAsync(ApiQuery(linkPath), Ct))).GetProperty("total").GetInt64();

    private static long? Long(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : null;

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Every count, list and chart bar with a link: its number = the linked list's total. Returns how many were checked.</summary>
    private static async Task<int> AssertEveryNumberMatchesItsList(HttpClient client, List<JsonElement> widgets)
    {
        var checkedCount = 0;
        foreach (var w in widgets)
        {
            var code = Text(w, "code")!;
            Text(w, "error").ShouldBeNull(code);
            if (Text(w, "linkPath") is { } link && Long(w, "value") is { } value && code != "officeWorkload" && code != "allByOffice")
            {
                (await ListTotal(client, link)).ShouldBe(value, $"{code}: {link}");
                checkedCount++;
            }

            if (Text(w, "kind") == "chart")
            {
                foreach (var bar in w.GetProperty("items").EnumerateArray())
                {
                    var (v1, v2) = (Long(bar, "value") ?? 0, Long(bar, "value2") ?? 0);
                    if (Text(bar, "link2Path") is { } second)
                    {
                        (await ListTotal(client, Text(bar, "linkPath")!)).ShouldBe(v1, $"{code} {Text(bar, "label")} open");
                        (await ListTotal(client, second)).ShouldBe(v2, $"{code} {Text(bar, "label")} closed");
                    }
                    else
                    {
                        // a lawyer: primary + secondary = the office list filtered by that lawyer
                        (await ListTotal(client, Text(bar, "linkPath")!)).ShouldBe(v1 + v2, $"{code} {Text(bar, "label")}");
                    }

                    checkedCount++;
                }
            }
        }

        return checkedCount;
    }

    // ---------------------------------------------------------------- one source of truth, per audience

    [Theory]
    [InlineData("protokoll@ash.local", "registeredToday,registeredWaiting,myRegistrations", true)]
    [InlineData("kryetar@ash.local", "unassigned,officeMissingData30,officeHearingOverdue,officeDecisionPending,officeWorkload,officeHearingsThisWeek", true)]
    [InlineData("avokat1@ash.local", "mineOpen,mineHearingOverdue,mineMissingData30,mineDecisionPending,mineHearingsThisWeek,mineActivity", true)]
    [InlineData("institucion@ash.local", "institutionLinked,institutionDelegated,institutionNoJurist,institutionUpcoming", true)]
    // the seed gives the jurist no overdue hearing: some of its numbers are 0
    [InlineData("jurist@ash.local", "mineOpen,mineHearingOverdue,mineMissingData30,mineDecisionPending,mineHearingsThisWeek,mineActivity", false)]
    [InlineData("monitor@ash.local", "allHearingsThisWeek,allMissingData6m,allDecisionPending,allByOffice", true)]
    [InlineData("admin@ash.local", "allHearingsThisWeek,allMissingData6m,allDecisionPending,allByOffice,adminFailedScans,adminJobs,adminPartitions", true)]
    public async Task Each_audience_gets_its_widgets_and_every_number_equals_its_list(string email, string codes, bool nonZero)
    {
        await DashboardSeed.CreateAsync(Db.AppConnectionString, 5000, Now);
        await using var factory = Factory();
        using var client = factory.CreateClient().AsUser(email);

        var widgets = await Widgets(client);

        widgets.Select(w => Text(w, "code")).ShouldBe(codes.Split(','));
        widgets.Select(w => Text(w, "code")).Distinct().Count().ShouldBe(widgets.Count);
        var checkedCount = await AssertEveryNumberMatchesItsList(client, widgets);
        TestContext.Current.TestOutputHelper?.WriteLine($"{email}: {checkedCount} numbers checked against their lists");
        // the seed makes the numbers non-trivial: a passing check over zeros proves little
        if (nonZero)
        {
            widgets.Where(w => Text(w, "kind") == "count" && !Text(w, "code")!.StartsWith("admin", StringComparison.Ordinal))
                .ShouldAllBe(w => Long(w, "value") > 0);
        }
        foreach (var w in widgets.Where(w => w.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array))
        {
            w.GetProperty("items").GetArrayLength().ShouldBeLessThanOrEqualTo(8, Text(w, "code"));
        }

        JsonAssert.NoNumericIds(await Json(await client.GetAsync("/api/dashboard", Ct)));
    }

    [Fact]
    public async Task Kryetar_widgets_carry_the_oldest_wait_the_week_list_and_the_workload_per_lawyer()
    {
        await DashboardSeed.CreateAsync(Db.AppConnectionString, 5000, Now);
        await using var factory = Factory();
        using var kryetar = factory.CreateClient().AsUser("kryetar@ash.local");
        var widgets = (await Widgets(kryetar)).ToDictionary(w => Text(w, "code")!);

        // the oldest REGISTERED case of TR was registered 19 days (and a few hours) ago
        var oldest = await Scalar("SELECT MIN(registered_at) FROM cases c JOIN offices o ON o.id = c.office_id WHERE o.code = 'TR' AND c.lifecycle_status = 'REGISTERED'");
        Long(widgets["unassigned"], "secondary").ShouldBe(
            TiraneTime.DateOf(Now).DayNumber - TiraneTime.DateOf(DateTime.SpecifyKind((DateTime)oldest!, DateTimeKind.Utc)).DayNumber);
        Text(widgets["unassigned"], "linkPath").ShouldBe("/cases?tab=unassigned");

        var week = widgets["officeHearingsThisWeek"].GetProperty("items").EnumerateArray().ToList();
        week.Count.ShouldBe(8);
        var (from, to) = Ash.Modules.Cases.Application.ListCases.CaseListSpecialFilter.TiraneWeek(Now);
        week.Select(i => i.GetProperty("at").GetDateTime().ToUniversalTime()).ShouldBeInOrder();
        week.ShouldAllBe(i => i.GetProperty("at").GetDateTime().ToUniversalTime() >= from && i.GetProperty("at").GetDateTime().ToUniversalTime() < to);
        week.ShouldAllBe(i => Text(i, "linkPath")!.EndsWith("/seancat") && Text(i, "courtName") == "Gjykata e Shkallës së Parë Tiranë");

        var lawyers = widgets["officeWorkload"].GetProperty("items").EnumerateArray().Select(i => Text(i, "label")).ToList();
        lawyers.ShouldContain("Avokat1 Dev");
        lawyers.ShouldContain("Avokat2 Dev");
        Text(widgets["officeDecisionPending"], "linkPath").ShouldBe("/cases?tab=office&status=CLOSED&filter=decisionPending");
    }

    [Fact]
    public async Task Activity_shows_only_the_levels_the_lawyer_is_on()
    {
        await DashboardSeed.CreateAsync(Db.AppConnectionString, 600, Now);
        await using var factory = Factory();
        using var lawyer = factory.CreateClient().AsUser("avokat1@ash.local");

        var activity = (await Widgets(lawyer)).Single(w => Text(w, "code") == "mineActivity").GetProperty("items").EnumerateArray().ToList();

        // the 8 most recently recorded history rows (append-only: by id) of the levels avokat1 is on now
        var expected = await Strings("""
            SELECT c.case_code FROM case_history_events e JOIN cases c ON c.id = e.case_id
            WHERE e.case_id IN (SELECT a.case_id FROM case_assignments a JOIN users u ON u.id = a.user_id
                                WHERE u.email = 'avokat1@ash.local' AND a.ended_at IS NULL)
            ORDER BY e.id DESC LIMIT 8
            """);
        activity.Select(i => Text(i, "caseCode")).ShouldBe(expected);
        activity.ShouldAllBe(i => Text(i, "linkPath")!.EndsWith("/historiku"));
    }

    // ---------------------------------------------------------------- the list filters (019 endpoint)

    [Theory]
    [InlineData("kryetar@ash.local", "office", "missingData30",
        "c.office_id = {tr} AND c.lifecycle_status = 'ACTIVE' AND c.first_data_entry_at IS NULL AND c.assigned_at <= @now - INTERVAL 30 DAY")]
    [InlineData("monitor@ash.local", "all", "missingData6m",
        "c.lifecycle_status = 'ACTIVE' AND c.first_data_entry_at IS NULL AND c.assigned_at <= @now - INTERVAL 6 MONTH")]
    [InlineData("kryetar@ash.local", "office", "hearingOverdue",
        "c.office_id = {tr} AND c.lifecycle_status IN ('REGISTERED','ACTIVE') AND c.next_hearing_at < @now")]
    [InlineData("avokat1@ash.local", "mine", "hearingThisWeek",
        "c.lifecycle_status IN ('REGISTERED','ACTIVE') AND {mine} AND EXISTS (SELECT 1 FROM case_hearings h WHERE h.case_id = c.id AND h.status = 'SCHEDULED' AND h.scheduled_at >= @week AND h.scheduled_at < @week + INTERVAL 7 DAY)")]
    [InlineData("kryetar@ash.local", "office", "decisionPending",
        "c.office_id = {tr} AND c.lifecycle_status = 'CLOSED' AND EXISTS (SELECT 1 FROM case_decisions d WHERE d.case_id = c.id AND d.decision_received = 0)")]
    [InlineData("institucion@ash.local", "institution", "noJurist",
        "c.lifecycle_status IN ('REGISTERED','ACTIVE') AND {linked} AND NOT EXISTS (SELECT 1 FROM case_assignments a JOIN users u ON u.id = a.user_id JOIN user_roles ur ON ur.user_id = u.id JOIN roles r ON r.id = ur.role_id WHERE a.case_id = c.id AND a.ended_at IS NULL AND a.case_role = 'INSTITUTION_JURIST' AND u.institution_id = {ministry} AND u.is_active = 1 AND r.code = 'INSTITUTION_JURIST')")]
    [InlineData("institucion@ash.local", "institution", "delegated",
        "c.lifecycle_status IN ('REGISTERED','ACTIVE') AND EXISTS (SELECT 1 FROM case_institutions i WHERE i.case_id = c.id AND i.institution_id = {ministry} AND i.ended_at IS NULL AND i.link_mode = 'DELEGATED')")]
    public async Task Each_new_list_filter_returns_exactly_the_cases_of_its_rule(string email, string tab, string filter, string where)
    {
        var ids = await DashboardSeed.CreateAsync(Db.AppConnectionString, 5000, Now);
        await using var factory = Factory();
        using var client = factory.CreateClient().AsUser(email);
        var ministry = Convert.ToInt64(await Scalar("SELECT institution_id FROM users WHERE email = 'institucion@ash.local'"));
        var mine = "EXISTS (SELECT 1 FROM case_assignments a JOIN users u ON u.id = a.user_id WHERE a.case_id = c.id AND a.ended_at IS NULL AND u.email = 'avokat1@ash.local')";
        var linked = $"EXISTS (SELECT 1 FROM case_institutions i WHERE i.case_id = c.id AND i.institution_id = {ministry} AND i.ended_at IS NULL)";
        var sql = "SELECT c.case_code FROM cases c WHERE " + where
            .Replace("{tr}", ids.TrOffice.ToString()).Replace("{ministry}", ministry.ToString())
            .Replace("{mine}", mine).Replace("{linked}", linked);
        var expected = await Strings(sql, ("@now", Now), ("@week", Ash.Modules.Cases.Application.ListCases.CaseListSpecialFilter.TiraneWeek(Now).FromUtc));
        expected.ShouldNotBeEmpty(filter);

        var actual = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await Json(await client.GetAsync(
                $"/api/cases?tab={tab}&filter={filter}&limit=100{(cursor is null ? "" : $"&after={cursor}")}", Ct));
            page.GetProperty("total").GetInt32().ShouldBe(expected.Count, filter);
            actual.AddRange(page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("caseCode").GetString()!));
            cursor = page.GetProperty("nextCursor").ValueKind == JsonValueKind.Null ? null : page.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null && ++pages < 100);

        actual.OrderBy(x => x, StringComparer.Ordinal).ShouldBe(expected.OrderBy(x => x, StringComparer.Ordinal), filter);
    }

    [Fact]
    public async Task An_unknown_filter_or_an_institution_filter_for_another_user_is_400()
    {
        await using var factory = Factory();
        using var kryetar = factory.CreateClient().AsUser("kryetar@ash.local");

        var unknown = await kryetar.GetAsync("/api/cases?tab=office&filter=nope", Ct);
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await unknown.Content.ReadAsStringAsync(Ct)).ShouldContain("filter");
        (await kryetar.GetAsync("/api/cases?tab=office&filter=noJurist", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await kryetar.GetAsync("/api/cases?tab=office&filter=hearingOverdue", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---------------------------------------------------------------- one failing widget

    /// <summary>Documents that fail once (the admin's "failed scans" widget is its only dashboard user), then work.</summary>
    private sealed class FlakyDocuments : IDocumentService
    {
        public int Calls;

        public Task<int> CountFailedScansAsync(CancellationToken ct = default) =>
            Interlocked.Increment(ref Calls) == 1 ? throw new InvalidOperationException("documents are down") : Task.FromResult(0);

        public Task<DocumentInfo?> GetAsync(Guid publicId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<DocumentInfo?> GetByIdAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<long, int>> CountsByCaseAsync(IReadOnlyCollection<long> caseIds, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> IsCleanAsync(Guid publicId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task A_widget_that_fails_reports_an_error_and_the_others_still_render()
    {
        await DashboardSeed.CreateAsync(Db.AppConnectionString, 600, Now);
        var documents = new FlakyDocuments();
        await using var factory = new AshApiFactory(Db, configureServices: services =>
        {
            services.AddSingleton<IClock>(new FixedClock(Now));
            services.AddScoped<IDocumentService>(_ => documents);
        });
        using var admin = factory.CreateClient().AsUser("admin@ash.local");

        var widgets = await Widgets(admin);

        var failed = widgets.Single(w => Text(w, "code") == "adminFailedScans");
        Text(failed, "error").ShouldNotBeNull();
        Long(failed, "value").ShouldBeNull();
        widgets.Where(w => Text(w, "code") != "adminFailedScans").ShouldAllBe(w => Text(w, "error") == null);
        Long(widgets.Single(w => Text(w, "code") == "allHearingsThisWeek"), "value").ShouldNotBeNull();

        // a failure is never cached: the next load asks again and has it
        var again = (await Widgets(admin)).Single(w => Text(w, "code") == "adminFailedScans");
        Text(again, "error").ShouldBeNull();
        Long(again, "value").ShouldBe(0);
        documents.Calls.ShouldBe(2);
    }

    // ---------------------------------------------------------------- performance (5,000 cases; 50,000 in DashboardPerfTests)

    [Fact]
    public async Task Dashboard_answers_in_under_500_ms_for_kryetar_lawyer_and_monitor_with_5000_cases()
    {
        await DashboardSeed.CreateAsync(Db.AppConnectionString, 5000, Now);
        await DashboardTiming.AssertFastAsync(Factory(), 5000, TestContext.Current.TestOutputHelper, Ct);
    }

    // ---------------------------------------------------------------- helpers

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync(Ct);
        return value is DBNull ? null : value;
    }

    private async Task<List<string>> Strings(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await using var reader = await command.ExecuteReaderAsync(Ct);
        var values = new List<string>();
        while (await reader.ReadAsync(Ct))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }
}

/// <summary>
/// Task 046 acceptance: GET /api/dashboard under 500 ms for the Kryetar, a lawyer and the Monitor — each measured
/// cold (nothing cached for that user's scope), after one warm-up call that fills no shared cache (Protokoll).
/// </summary>
public static class DashboardTiming
{
    public static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(500);

    public static async Task AssertFastAsync(AshApiFactory factory, int cases, ITestOutputHelper? output, CancellationToken ct)
    {
        await using (factory)
        {
            using (var warm = factory.CreateClient().AsUser("protokoll@ash.local"))
            {
                (await warm.GetAsync("/api/dashboard", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
            }

            var timings = new List<(string User, long Ms)>();
            foreach (var email in new[] { "kryetar@ash.local", "avokat1@ash.local", "monitor@ash.local" })
            {
                using var client = factory.CreateClient().AsUser(email);
                var watch = Stopwatch.StartNew();
                var response = await client.GetAsync("/api/dashboard", ct);
                watch.Stop();
                response.StatusCode.ShouldBe(HttpStatusCode.OK);
                var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
                var failed = body.GetProperty("widgets").EnumerateArray()
                    .Where(w => w.TryGetProperty("error", out var e) && e.ValueKind != JsonValueKind.Null).ToList();
                failed.ShouldBeEmpty(email);
                timings.Add((email, watch.ElapsedMilliseconds));
            }

            output?.WriteLine($"GET /api/dashboard with {cases:N0} cases: " + string.Join(", ", timings.Select(t => $"{t.User} {t.Ms} ms")));
            timings.ShouldAllBe(t => t.Ms < Limit.TotalMilliseconds, string.Join(", ", timings.Select(t => $"{t.User} {t.Ms} ms")));
        }
    }
}
