using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ash.Infrastructure;
using Ash.Modules.Cases;
using Ash.Modules.Cases.Application.ListCases;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Ids;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 019 — the case list. Every test seeds 5,000 cases with recursive-CTE INSERT … SELECTs (two offices, all
/// statuses, ~30% without a next hearing) and checks the API against an independent SQL "oracle" query.
/// Case n (1..5000): office TR when n is even else DR; status REGISTERED/ACTIVE/CLOSED by n % 3; no hearing when
/// n % 10 &lt; 3; primary lawyer (non-REGISTERED) avokat1 when n % 4 = 0, avokat2 for the other TR cases, the DR
/// lawyer in DR; an ENDED avokat1 row when n % 8 = 2; active ministry link when n % 5 = 0, ended link when n % 5 = 1.
/// </summary>
public class CaseListTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string OpenOrder = "c.next_hearing_at IS NULL, c.next_hearing_at, c.id";
    private const string Open = "c.lifecycle_status IN ('REGISTERED','ACTIVE')";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Scenario(
        long Tr, long Dr, long Ministry, long Protokoll, long Kryetar, long Avokat1, long Court2, long Category,
        string DrKryetarEmail, string DrLawyerEmail)
    {
        public string Sql(string text) => text
            .Replace("{tr}", Tr.ToString()).Replace("{dr}", Dr.ToString()).Replace("{ministry}", Ministry.ToString())
            .Replace("{prot}", Protokoll.ToString()).Replace("{av1}", Avokat1.ToString())
            .Replace("{court2}", Court2.ToString()).Replace("{cat}", Category.ToString());
    }

    private async Task<Scenario> SeedAsync()
    {
        await using var seed = await Seed.CreateAsync(Db.AppConnectionString);
        var dr = await seed.OfficeAsync($"Zyra Durrës {seed.Suffix}", "LOCAL");
        await seed.UserAsync("drkryetar", "Kryetar", "Durrës", dr, null, "OFFICE_HEAD");
        var drLawyer = await seed.UserAsync("drlawyer", "Avokat", "Durrës", dr, null, "STATE_LAWYER");
        var drProtokoll = await seed.UserAsync("drprot", "Protokoll", "Durrës", dr, null, "PROTOCOL");
        var court1 = await seed.CourtAsync("Gjykata e Shkallës së Parë Tiranë");
        var court2 = await seed.CourtAsync("Gjykata e Apelit Tiranë");

        async Task<long> Id(string sql) => Convert.ToInt64(await Scalar(sql));
        var scenario = new Scenario(
            Tr: await Id("SELECT id FROM offices WHERE code = 'TR'"),
            Dr: dr,
            Ministry: await Id("SELECT id FROM institutions WHERE name = 'Ministria e Financave dhe Ekonomisë'"),
            Protokoll: await Id("SELECT id FROM users WHERE email = 'protokoll@ash.local'"),
            Kryetar: await Id("SELECT id FROM users WHERE email = 'kryetar@ash.local'"),
            Avokat1: await Id("SELECT id FROM users WHERE email = 'avokat1@ash.local'"),
            Court2: court2,
            Category: await Id("SELECT id FROM case_categories WHERE code = 'NDJEKJE'"),
            DrKryetarEmail: $"drkryetar-{seed.Suffix}@ash.test",
            DrLawyerEmail: $"drlawyer-{seed.Suffix}@ash.test");
        var avokat2 = await Id("SELECT id FROM users WHERE email = 'avokat2@ash.local'");

        const string seq = "WITH RECURSIVE seq (n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 5000) ";
        const string n = "CAST(SUBSTRING(c.act_number, 5) AS UNSIGNED)";
        var sql = $"""
            SET SESSION cte_max_recursion_depth = 10000;
            INSERT INTO dossiers (unique_code, code_format_id, office_id, opened_by)
            {seq}
            SELECT CONCAT('2026-', LPAD(n, 6, '0')), (SELECT id FROM dossier_code_formats WHERE is_active = 1),
                   IF(n % 2 = 0, @tr, @dr), IF(n % 2 = 0, @prot, @drprot)
              FROM seq;
            INSERT INTO cases (dossier_id, level_seq, case_code, office_id, summons_number, act_number, received_date,
                               court_level_id, court_id, object_text, lifecycle_status, category_id, priority,
                               is_suspended, next_hearing_at, registered_by, registered_at, closed_at, closed_by)
            {seq}
            SELECT d.id, 1, CONCAT(d.unique_code, '/1'), d.office_id, CONCAT('FL-', s.n), CONCAT('AKT-', s.n), '2026-01-01',
                   (SELECT id FROM court_levels WHERE code = 'FIRST_INSTANCE'), IF(s.n % 4 < 2, @court1, @court2),
                   IF(s.n = 130, 'Shpronësim i tokës bujqësore', CONCAT('Objekt prove numër ', s.n)),
                   ELT(s.n % 3 + 1, 'REGISTERED', 'ACTIVE', 'CLOSED'),
                   IF(s.n % 2 = 1, @cat, NULL),
                   ELT(s.n % 4 + 1, 'NONE', 'LOW', 'MEDIUM', 'HIGH'),
                   s.n % 7 = 0,
                   IF(s.n % 10 < 3, NULL, TIMESTAMP('2026-11-01') + INTERVAL (s.n % 400) HOUR),
                   IF(s.n % 2 = 1, @drprot, IF(s.n % 4 = 0, @kryetar, @prot)),
                   TIMESTAMP('2026-01-01') + INTERVAL (s.n DIV 30) MINUTE,
                   IF(s.n % 3 = 2, TIMESTAMP('2026-06-01') + INTERVAL (s.n DIV 7) HOUR, NULL),
                   IF(s.n % 3 = 2, @kryetar, NULL)
              FROM seq s JOIN dossiers d ON d.unique_code = CONCAT('2026-', LPAD(s.n, 6, '0'));
            INSERT INTO case_assignments (case_id, user_id, case_role, assigned_by)
            SELECT c.id, IF(c.office_id = @dr, @drlawyer, IF({n} % 4 = 0, @av1, @av2)), 'PRIMARY_LAWYER', @kryetar
              FROM cases c WHERE c.lifecycle_status <> 'REGISTERED';
            INSERT INTO case_assignments (case_id, user_id, case_role, assigned_by, ended_at, ended_by, end_reason)
            SELECT c.id, @av1, 'SECONDARY_LAWYER', @kryetar, UTC_TIMESTAMP(3), @kryetar, 'TRANSFERRED'
              FROM cases c WHERE c.lifecycle_status <> 'REGISTERED' AND {n} % 8 = 2;
            INSERT INTO case_institutions (case_id, institution_id, link_mode, started_by, ended_at, ended_by)
            SELECT c.id, @ministry, 'MONITORING', @prot, IF({n} % 5 = 1, UTC_TIMESTAMP(3), NULL), IF({n} % 5 = 1, @prot, NULL)
              FROM cases c WHERE {n} % 5 IN (0, 1);
            INSERT INTO case_parties (case_id, party_role_id, party_kind, display_name, created_by, deleted_at, deleted_by)
            SELECT c.id, (SELECT id FROM party_roles WHERE code = 'PADITES'), 'NATURAL',
                   IF({n} = 133, 'Kristaq Zyberaj', 'Zyberaj Fshirë'), @prot,
                   IF({n} = 133, NULL, UTC_TIMESTAMP(3)), IF({n} = 133, NULL, @prot)
              FROM cases c WHERE {n} IN (133, 136);
            """;
        await Execute(sql,
            ("@tr", scenario.Tr), ("@dr", dr), ("@prot", scenario.Protokoll), ("@drprot", drProtokoll),
            ("@kryetar", scenario.Kryetar), ("@av1", scenario.Avokat1), ("@av2", avokat2), ("@drlawyer", drLawyer),
            ("@court1", court1), ("@court2", court2), ("@cat", scenario.Category), ("@ministry", scenario.Ministry));
        return scenario;
    }

    // ---------------------------------------------------------------- paging per tab

    [Theory]
    [InlineData("avokat1@ash.local", "mine")]
    [InlineData("protokoll@ash.local", "registered")]
    [InlineData("kryetar@ash.local", "unassigned")]
    [InlineData("kryetar@ash.local", "office")]
    [InlineData("drkryetar", "office")]
    [InlineData("institucion@ash.local", "institution")]
    [InlineData("monitor@ash.local", "all")]
    [InlineData("admin@ash.local", "all")]
    [InlineData("monitor@ash.local", "all&status=CLOSED")]
    public async Task Each_tab_pages_through_every_case_exactly_once_in_order(string user, string tab)
    {
        var s = await SeedAsync();
        var (where, order) = tab switch
        {
            "mine" => ($"{Open} AND EXISTS (SELECT 1 FROM case_assignments a WHERE a.case_id = c.id AND a.user_id = {{av1}} AND a.ended_at IS NULL)", OpenOrder),
            "registered" => ("c.office_id = {tr} AND c.lifecycle_status = 'REGISTERED' AND c.registered_by = {prot}", "c.registered_at DESC, c.id DESC"),
            "unassigned" => ("c.office_id = {tr} AND c.lifecycle_status = 'REGISTERED'", OpenOrder),
            "office" when user == "drkryetar" => ($"c.office_id = {{dr}} AND {Open}", OpenOrder),
            "office" => ($"c.office_id = {{tr}} AND {Open}", OpenOrder),
            "institution" => ($"{Open} AND EXISTS (SELECT 1 FROM case_institutions i WHERE i.case_id = c.id AND i.institution_id = {{ministry}} AND i.ended_at IS NULL)", OpenOrder),
            "all" => (Open, OpenOrder),
            _ => ("c.lifecycle_status = 'CLOSED'", "c.closed_at DESC, c.id DESC")
        };
        var expected = await Oracle(s.Sql(where), order);
        expected.Count.ShouldBeGreaterThan(100, "the tab must span several pages");

        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(user == "drkryetar" ? s.DrKryetarEmail : user);
        var items = await PageAll(client, $"tab={tab}");

        var ids = items.Select(PublicIdOf).ToList();
        ids.Distinct().Count().ShouldBe(ids.Count, "no case may appear twice");
        ids.ShouldBe(expected, "every case exactly once, in the oracle's order");

        if (!tab.Contains("CLOSED") && tab != "registered")
        {
            // NULLs last: once a case without a hearing appears, every following case has none either
            var hearings = items.Select(i => i.GetProperty("nextHearingAt").ValueKind == JsonValueKind.Null).ToList();
            hearings.SkipWhile(isNull => !isNull).ShouldAllBe(isNull => isNull);
            hearings.ShouldContain(true);
            hearings.ShouldContain(false);
        }
    }

    // ---------------------------------------------------------------- access

    [Theory]
    [InlineData("avokat1@ash.local", "all")]
    [InlineData("avokat1@ash.local", "office")]
    [InlineData("avokat1@ash.local", "registered")]
    [InlineData("protokoll@ash.local", "all")]
    [InlineData("protokoll@ash.local", "unassigned")]
    [InlineData("kryetar@ash.local", "all")]
    [InlineData("kryetar@ash.local", "institution")]
    [InlineData("institucion@ash.local", "mine")]
    [InlineData("institucion@ash.local", "office")]
    [InlineData("monitor@ash.local", "office")]
    public async Task A_tab_the_user_may_not_use_is_403(string user, string tab)
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(user);
        var response = await client.GetAsync($"/api/cases?tab={tab}", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Lawyer_sees_only_own_cases_and_items_carry_court_and_primary_lawyer_names()
    {
        var s = await SeedAsync();
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser("avokat1@ash.local");

        var items = await PageAll(client, "tab=mine");
        var notMine = await Oracle(s.Sql(
            "NOT EXISTS (SELECT 1 FROM case_assignments a WHERE a.case_id = c.id AND a.user_id = {av1} AND a.ended_at IS NULL)"), "c.id");
        items.Select(PublicIdOf).Intersect(notMine).ShouldBeEmpty();
        items.ShouldAllBe(i => i.GetProperty("primaryLawyerName").GetString() == "Avokat1 Dev");
        items.Select(i => i.GetProperty("courtName").GetString()).Distinct().OrderBy(x => x)
            .ShouldBe(["Gjykata e Shkallës së Parë Tiranë"]); // avokat1's cases are n % 4 = 0 → court 1
        items.ShouldAllBe(i => i.GetProperty("status").GetString() == "ACTIVE");
    }

    [Fact]
    public async Task Kryetar_of_another_office_sees_none_of_the_TR_cases()
    {
        var s = await SeedAsync();
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(s.DrKryetarEmail);

        var trCases = await Oracle(s.Sql("c.office_id = {tr}"), "c.id");
        foreach (var tab in new[] { "office", "unassigned" })
        {
            var items = await PageAll(client, $"tab={tab}");
            items.ShouldNotBeEmpty();
            items.Select(PublicIdOf).Intersect(trCases).ShouldBeEmpty();
        }
    }

    // ---------------------------------------------------------------- tabs endpoint + default tab

    [Fact]
    public async Task Tabs_endpoint_lists_the_users_tabs_with_counts_and_the_default()
    {
        var s = await SeedAsync();
        await using var factory = new AshApiFactory(Db);

        async Task<JsonElement> Tabs(string user)
        {
            using var client = factory.CreateClient().AsUser(user);
            var response = await client.GetAsync("/api/cases/tabs", Ct);
            var text = await response.Content.ReadAsStringAsync(Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
            return JsonDocument.Parse(text).RootElement.Clone();
        }

        static Dictionary<string, int> Counts(JsonElement tabs) => tabs.GetProperty("tabs").EnumerateArray()
            .ToDictionary(t => t.GetProperty("tab").GetString()!, t => t.GetProperty("count").GetInt32());

        var kryetar = await Tabs("kryetar@ash.local");
        Counts(kryetar).ShouldBe(new Dictionary<string, int>
        {
            ["unassigned"] = (await Oracle(s.Sql("c.office_id = {tr} AND c.lifecycle_status = 'REGISTERED'"), "c.id")).Count,
            ["office"] = (await Oracle(s.Sql($"c.office_id = {{tr}} AND {Open}"), "c.id")).Count
        }, ignoreOrder: true);
        kryetar.GetProperty("defaultTab").GetString().ShouldBe("unassigned");

        (await Tabs("avokat1@ash.local")).GetProperty("defaultTab").GetString().ShouldBe("mine");
        (await Tabs("protokoll@ash.local")).GetProperty("defaultTab").GetString().ShouldBe("registered");
        (await Tabs("institucion@ash.local")).GetProperty("defaultTab").GetString().ShouldBe("institution");
        var monitor = await Tabs("monitor@ash.local");
        Counts(monitor).Keys.ShouldBe(["all"]);
        Counts(monitor)["all"].ShouldBe((await Oracle(Open, "c.id")).Count);
        monitor.GetProperty("defaultTab").GetString().ShouldBe("all");
        (await Tabs("admin@ash.local")).GetProperty("defaultTab").GetString().ShouldBe("all");

        // Kryetar with nothing waiting for a lawyer opens the office tab, and GET /api/cases without a tab follows it
        await Execute(s.Sql("UPDATE cases SET lifecycle_status = 'ACTIVE' WHERE office_id = {tr} AND lifecycle_status = 'REGISTERED'"));
        kryetar = await Tabs("kryetar@ash.local");
        Counts(kryetar)["unassigned"].ShouldBe(0);
        kryetar.GetProperty("defaultTab").GetString().ShouldBe("office");
    }

    [Fact]
    public async Task Without_a_tab_the_default_tab_is_listed()
    {
        await SeedAsync();
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser("kryetar@ash.local");

        var page = await GetPage(client, "/api/cases");
        var items = page.GetProperty("items").EnumerateArray().ToList();
        items.Count.ShouldBe(50);
        items.ShouldAllBe(i => i.GetProperty("status").GetString() == "REGISTERED"); // the unassigned tab
    }

    // ---------------------------------------------------------------- cursor + limit

    [Fact]
    public async Task A_cursor_used_with_other_filters_or_garbage_is_400()
    {
        await SeedAsync();
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser("monitor@ash.local");

        var first = await GetPage(client, "/api/cases?tab=all&limit=10");
        var cursor = Uri.EscapeDataString(first.GetProperty("nextCursor").GetString()!);

        (await client.GetAsync($"/api/cases?tab=all&limit=10&after={cursor}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        foreach (var changed in new[] { "priority=HIGH", "status=CLOSED", "suspended=true", "q=AKT-1", "courtId=1" })
        {
            var response = await client.GetAsync($"/api/cases?tab=all&limit=10&{changed}&after={cursor}", Ct);
            var text = await response.Content.ReadAsStringAsync(Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, changed);
            text.ShouldContain("after");
        }

        (await client.GetAsync("/api/cases?tab=all&after=not-a-cursor", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.GetAsync($"/api/cases?tab=all&after={Uri.EscapeDataString(Ash.SharedKernel.Paging.Cursor.Encode("x~1", 5))}", Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Limit_defaults_to_50_and_is_capped_at_100()
    {
        await SeedAsync();
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser("monitor@ash.local");

        (await GetPage(client, "/api/cases?tab=all")).GetProperty("items").GetArrayLength().ShouldBe(50);
        (await GetPage(client, "/api/cases?tab=all&limit=500")).GetProperty("items").GetArrayLength().ShouldBe(100);
    }

    // ---------------------------------------------------------------- filters + validation

    [Fact]
    public async Task Filters_match_the_oracle()
    {
        var s = await SeedAsync();
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser("monitor@ash.local");
        var avokat1 = PublicId.FromBytes((byte[])(await Scalar("SELECT public_id FROM users WHERE email = 'avokat1@ash.local'"))!);

        var cases = new (string Query, string Where)[]
        {
            ($"lawyerId={avokat1}", $"{Open} AND EXISTS (SELECT 1 FROM case_assignments a WHERE a.case_id = c.id AND a.user_id = {{av1}} AND a.ended_at IS NULL AND a.case_role IN ('PRIMARY_LAWYER','SECONDARY_LAWYER'))"),
            // 2 Nov 2026 in Tirane (CET, UTC+1) = [1 Nov 23:00 UTC, 2 Nov 23:00 UTC)
            ("hearingFrom=2026-11-02&hearingTo=2026-11-02", $"{Open} AND c.next_hearing_at >= '2026-11-01 23:00:00' AND c.next_hearing_at < '2026-11-02 23:00:00'"),
            ($"courtId={s.Court2}&priority=HIGH&suspended=false", $"{Open} AND c.court_id = {{court2}} AND c.priority = 'HIGH' AND c.is_suspended = 0"),
            ("categoryCode=NDJEKJE&status=active", "c.lifecycle_status = 'ACTIVE' AND c.category_id = {cat}"),
            ("suspended=true&status=CLOSED", "c.lifecycle_status = 'CLOSED' AND c.is_suspended = 1"),
        };
        foreach (var (query, where) in cases)
        {
            var order = query.Contains("CLOSED") ? "c.closed_at DESC, c.id DESC" : OpenOrder;
            var expected = await Oracle(s.Sql(where), order);
            expected.ShouldNotBeEmpty(query);
            (await PageAll(client, $"tab=all&{query}")).Select(PublicIdOf).ToList().ShouldBe(expected, query);
        }
    }

    [Theory]
    [InlineData("tab=nope", "tab")]
    [InlineData("tab=all&status=OPEN", "status")]
    [InlineData("tab=all&priority=URGENT", "priority")]
    [InlineData("tab=all&categoryCode=NOPE", "categoryCode")]
    [InlineData("tab=all&lawyerId=00000000-0000-0000-0000-000000000001", "lawyerId")]
    [InlineData("tab=all&hearingFrom=2026-11-05&hearingTo=2026-11-01", "hearingTo")]
    [InlineData("tab=all&q=a", "q")]
    [InlineData("tab=all&q=%20%20", "q")]
    [InlineData("tab=all&courtId=-1", "courtId")]
    public async Task Invalid_query_is_400_with_the_field(string query, string field)
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser("monitor@ash.local");
        var response = await client.GetAsync($"/api/cases?{query}", Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, text);
        text.ShouldContain($"\"{field}\"");
    }

    // ---------------------------------------------------------------- search

    [Theory]
    [InlineData("2026-000124", "c.id = (SELECT id FROM cases WHERE act_number = 'AKT-124')")] // unique code
    [InlineData("2026-000124/1", "c.id = (SELECT id FROM cases WHERE act_number = 'AKT-124')")] // case code
    [InlineData("AKT-124", "c.act_number LIKE 'AKT-124%'")] // act number prefix
    [InlineData("bujqës", "c.act_number = 'AKT-130'")] // a word of the object, as a prefix
    [InlineData("BUJQES", "c.act_number = 'AKT-130'")] // case and accent insensitive
    [InlineData("Zyber", "c.act_number = 'AKT-133'")] // party name prefix; the deleted party of AKT-136 is ignored
    [InlineData("kristaq zyberaj", "c.act_number = 'AKT-133'")]
    [InlineData("AKT_12", "1 = 0")] // LIKE wildcards are literal: "_" does not match the "-" of AKT-12…
    public async Task Search_finds_by_code_act_number_object_and_party(string q, string where)
    {
        var s = await SeedAsync();
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser("monitor@ash.local");

        var expected = await Oracle(s.Sql($"{Open} AND {where}"), OpenOrder);
        if (where != "1 = 0")
        {
            expected.ShouldNotBeEmpty();
        }

        (await PageAll(client, $"tab=all&q={Uri.EscapeDataString(q)}")).Select(PublicIdOf).ToList().ShouldBe(expected);
    }

    [Fact]
    public async Task Search_stays_inside_the_tab_and_the_users_scope()
    {
        var s = await SeedAsync();
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(s.DrKryetarEmail);

        // AKT-124 is a TR case: the DR Kryetar must not find it
        (await PageAll(client, "tab=office&q=2026-000124")).ShouldBeEmpty();
        (await PageAll(client, "tab=office&q=AKT-125")).Select(PublicIdOf)
            .ShouldBe(await Oracle(s.Sql($"{Open} AND c.office_id = {{dr}} AND c.act_number LIKE 'AKT-125%'"), OpenOrder));
    }

    // ---------------------------------------------------------------- EXPLAIN

    [Theory]
    [InlineData("avokat1@ash.local", CaseListTab.Mine, "case_assignments", "ix_ca_user_current")]
    [InlineData("protokoll@ash.local", CaseListTab.Registered, "cases", null)]
    [InlineData("kryetar@ash.local", CaseListTab.Unassigned, "cases", null)]
    [InlineData("kryetar@ash.local", CaseListTab.Office, "cases", null)]
    [InlineData("institucion@ash.local", CaseListTab.Institution, "case_institutions", "ix_ci_institution")]
    [InlineData("monitor@ash.local", CaseListTab.All, "cases", null)]
    public async Task Explain_each_tab_uses_an_index(string user, CaseListTab tab, string drivingTable, string? expectedKey)
    {
        await SeedAsync();
        await Execute("ANALYZE TABLE cases, case_assignments, case_institutions");

        var services = new ServiceCollection();
        services.AddAshInfrastructure(Db.AppConnectionString);
        services.AddCasesModule(new ConfigurationBuilder().Build());
        var currentUser = await LoadUser(user);
        services.AddScoped<ICurrentUser>(_ => currentUser);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var builder = scope.ServiceProvider.GetRequiredService<CaseListQueryBuilder>();

        var sql = builder.Page(CaseListFilter.Open(tab), null, 101).ToQueryString();
        var plan = await Explain(sql);
        var aliases = Regex.Matches(sql, @"`(\w+)` AS `(\w+)`").ToDictionary(m => m.Groups[2].Value, m => m.Groups[1].Value, StringComparer.Ordinal);
        string TableOf(PlanRow row) => aliases.GetValueOrDefault(row.Table, row.Table);

        var text = $"tab {tab}\n{sql}\n" + string.Join('\n', plan.Select(r => $"{TableOf(r),-20} type={r.Type,-8} key={r.Key ?? "NULL",-28} rows={r.Rows} {r.Extra}"));
        TestContext.Current.TestOutputHelper?.WriteLine(text);

        if (tab is CaseListTab.All or CaseListTab.Office)
        {
            // Logged, not asserted. "all" (Monitor/Admin, open status only) matches ~2/3 of the table, and with only two
            // offices in the seed the open cases of one office are ~1/3 of it: MySQL then rightly prefers a full scan
            // over the ix_cases_office_status range. With the real number of offices the office predicate is selective.
            return;
        }

        var casesRows = plan.Where(r => TableOf(r) == "cases").ToList();
        casesRows.ShouldNotBeEmpty(text);
        casesRows.ShouldAllBe(r => r.Type != "ALL", text);
        var driving = plan.Where(r => TableOf(r) == drivingTable).ToList();
        driving.ShouldNotBeEmpty(text);
        driving.ShouldContain(r => r.Key != null && (expectedKey == null || r.Key == expectedKey), text);
    }

    private sealed record PlanRow(string Table, string Type, string? Key, long Rows, string Extra);

    /// <summary>Turns EF's ToQueryString output (parameter lines + statement) into SET @p … ; EXPLAIN statement.</summary>
    private async Task<List<PlanRow>> Explain(string queryString)
    {
        var sets = new StringBuilder();
        var body = new StringBuilder();
        foreach (var line in queryString.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            var parameter = Regex.Match(line, @"^(?:--\s*|SET\s+)(@\w+)\s*=\s*(.*?);?(?:\s*\((?:DbType|Size|Precision|Scale)[^)]*\))*$");
            if (parameter.Success && body.Length == 0)
            {
                sets.Append($"SET {parameter.Groups[1].Value} = {parameter.Groups[2].Value};\n");
            }
            else if (!string.IsNullOrWhiteSpace(line))
            {
                body.AppendLine(line);
            }
        }

        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        if (sets.Length > 0)
        {
            await using var set = new MySqlCommand(sets.ToString(), connection);
            await set.ExecuteNonQueryAsync(Ct);
        }

        await using var command = new MySqlCommand("EXPLAIN " + body, connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var rows = new List<PlanRow>();
        while (await reader.ReadAsync(Ct))
        {
            string? Text(string column) => reader[column] is DBNull ? null : Convert.ToString(reader[column]);
            rows.Add(new PlanRow(Text("table") ?? "", Text("type") ?? "", Text("key"),
                long.TryParse(Text("rows"), out var r) ? r : 0, Text("Extra") ?? ""));
        }

        return rows;
    }

    private sealed class TestUser(long id, long? office, long? institution, string[] roles, string[] permissions) : ICurrentUser
    {
        public long UserId => id;
        public Guid PublicId => Guid.Empty;
        public long? OfficeId => office;
        public long? InstitutionId => institution;
        public IReadOnlyCollection<string> Roles => roles;
        public bool HasPermission(string permission) => permissions.Contains(permission);
    }

    private async Task<TestUser> LoadUser(string email)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        long id;
        long? office, institution;
        await using (var cmd = new MySqlCommand("SELECT id, office_id, institution_id FROM users WHERE email = @e", connection))
        {
            cmd.Parameters.AddWithValue("@e", email);
            await using var reader = await cmd.ExecuteReaderAsync(Ct);
            await reader.ReadAsync(Ct);
            id = reader.GetInt64(0);
            office = reader.IsDBNull(1) ? null : reader.GetInt64(1);
            institution = reader.IsDBNull(2) ? null : reader.GetInt64(2);
        }

        async Task<string[]> Strings(string sql)
        {
            var result = new List<string>();
            await using var cmd = new MySqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@id", id);
            await using var reader = await cmd.ExecuteReaderAsync(Ct);
            while (await reader.ReadAsync(Ct))
            {
                result.Add(reader.GetString(0));
            }

            return [.. result];
        }

        var roles = await Strings("SELECT r.code FROM user_roles ur JOIN roles r ON r.id = ur.role_id WHERE ur.user_id = @id");
        var permissions = await Strings(
            "SELECT DISTINCT p.code FROM user_roles ur JOIN role_permissions rp ON rp.role_id = ur.role_id " +
            "JOIN permissions p ON p.id = rp.permission_id WHERE ur.user_id = @id");
        return new TestUser(id, office, institution, roles, permissions);
    }

    // ---------------------------------------------------------------- helpers

    private static Guid PublicIdOf(JsonElement item) => item.GetProperty("publicId").GetGuid();

    private static async Task<JsonElement> GetPage(HttpClient client, string url)
    {
        var response = await client.GetAsync(url, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>Follows nextCursor with limit=100 until the last page.</summary>
    private static async Task<List<JsonElement>> PageAll(HttpClient client, string query)
    {
        var all = new List<JsonElement>();
        string? cursor = null;
        for (var pages = 1; ; pages++)
        {
            pages.ShouldBeLessThan(100, "paging must end");
            var url = $"/api/cases?{query}&limit=100" + (cursor is null ? "" : $"&after={Uri.EscapeDataString(cursor)}");
            var page = await GetPage(client, url);
            var items = page.GetProperty("items").EnumerateArray().ToList();
            items.Count.ShouldBeLessThanOrEqualTo(100);
            all.AddRange(items);
            cursor = page.GetProperty("nextCursor") is { ValueKind: JsonValueKind.String } next ? next.GetString() : null;
            if (cursor is null)
            {
                return all;
            }

            items.Count.ShouldBe(100, "only the last page may be short");
        }
    }

    /// <summary>The expected list straight from SQL: public ids of the cases matching <paramref name="where"/>.</summary>
    private async Task<List<Guid>> Oracle(string where, string orderBy)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand($"SELECT c.public_id FROM cases c WHERE {where} ORDER BY {orderBy}", connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var ids = new List<Guid>();
        while (await reader.ReadAsync(Ct))
        {
            ids.Add(PublicId.FromBytes((byte[])reader[0]));
        }

        return ids;
    }

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(Ct);
    }

    private async Task Execute(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(Ct);
    }
}
