using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ash.Modules.Reports.Application.Statistics;
using Ash.SharedKernel.Ids;
using Ash.SharedKernel.Time;
using Ash.Tests.Integration.Infrastructure;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 044 — Tabela 5 / 6. The clock stands on 15 June 2027: 2027 is the current year (live), 2026 a closed one.
/// The scenario is the ash-db test (docs/sql/tests/test_scenario.sql) with dates, plus edge cases:
/// <list type="bullet">
/// <item>A (/1, first instance): registered 20.10.2026, primary avokat1, closed 10.12.2026 — lost, appealed, the
/// reasoned decision NOT received; caused damage 2,500,000 ALL.</item>
/// <item>A (/2, appeal court): registered 15.12.2026, primary avokat1, transferred to avokat2 on 01.02.2027; open.</item>
/// <item>B: registered 01.03.2026, closed 01.03.2027 (won; prevented damage 1,000 EUR, 50 m²) — open at the end of 2026.</item>
/// <item>C: registered 31.12.2026 at 23:30 Tirane (22:30 UTC) — counts in 2026.</item>
/// <item>D: registered 05.04.2027, DELEGIM, suspended; the ministry's agency is its defendant.</item>
/// </list>
/// </summary>
public class StatisticsTests(MySqlFixture db) : IntegrationTest(db)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTime Now = new(2027, 6, 15, 10, 0, 0, DateTimeKind.Utc);
    private const string Stats = "/api/reports/statistics";

    private sealed record Scenario(long Office, long Court1, long Court2, long A1, long A2, long B, long C, long D, long Ministry, long Agency);

    // ---------------------------------------------------------------- helpers

    private async Task<object?> Scalar(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        var result = await command.ExecuteScalarAsync(Ct);
        return result is DBNull ? null : result;
    }

    private async Task<long> Id(string sql, params (string Name, object? Value)[] parameters) =>
        Convert.ToInt64(await Scalar(sql, parameters));

    private sealed class FixedClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }

    private AshApiFactory Factory() => new(Db, configureServices: s => s.AddSingleton<IClock>(new FixedClock(Now)));

    private static DateTime Utc(int y, int m, int d, int h = 9, int min = 0) => new(y, m, d, h, min, 0, DateTimeKind.Utc);

    private async Task<long> UserId(string email) => await Id("SELECT id FROM users WHERE email = @e", ("@e", email));

    private async Task<Guid> PublicIdOf(string table, long id) =>
        PublicId.FromBytes((byte[])(await Scalar($"SELECT public_id FROM {table} WHERE id = @id", ("@id", id)))!);

    private async Task<long> CaseAsync(long office, long? dossier, int level, long court, DateTime registeredAt, DateTime? closedAt, string category = "NDJEKJE")
    {
        var protokoll = await UserId("protokoll@ash.local");
        var suffix = Guid.NewGuid().ToString("N")[..8];
        if (dossier is null)
        {
            dossier = await Id("""
                INSERT INTO dossiers (unique_code, code_format_id, office_id, opened_by)
                VALUES (@c, (SELECT id FROM dossier_code_formats WHERE is_active = 1), @o, @u); SELECT LAST_INSERT_ID();
                """, ("@c", $"ST-{suffix}"), ("@o", office), ("@u", protokoll));
        }

        var levelCode = level == 1 ? "FIRST_INSTANCE" : "APPEAL";
        return await Id("""
            INSERT INTO cases (dossier_id, level_seq, case_code, office_id, summons_number, act_number, received_date, court_level_id,
                               court_id, object_text, lifecycle_status, registered_by, registered_at, closed_at, closed_by, category_id)
            VALUES (@d, @l, CONCAT((SELECT unique_code FROM dossiers WHERE id = @d), '/', @l), @o, 'FL', CONCAT('AKT-', @s), DATE(@r),
                    (SELECT id FROM court_levels WHERE code = @lc), @court, 'Objekt', IF(@closed IS NULL, 'ACTIVE', 'CLOSED'), @u, @r,
                    @closed, IF(@closed IS NULL, NULL, @u), (SELECT id FROM case_categories WHERE code = @cat));
            SELECT LAST_INSERT_ID();
            """, ("@d", dossier), ("@l", level), ("@o", office), ("@s", suffix), ("@r", registeredAt), ("@lc", levelCode),
            ("@court", court), ("@closed", closedAt), ("@u", protokoll), ("@cat", category));
    }

    private async Task PrimaryAsync(long caseId, string email, DateTime from, DateTime? to = null) =>
        await Scalar("""
            INSERT INTO case_assignments (case_id, user_id, case_role, assigned_by, assigned_at, ended_at, end_reason)
            VALUES (@c, @u, 'PRIMARY_LAWYER', @k, @from, @to, IF(@to IS NULL, NULL, 'TRANSFERRED'))
            """, ("@c", caseId), ("@u", await UserId(email)), ("@k", await UserId("kryetar@ash.local")), ("@from", from), ("@to", to));

    private async Task DecisionAsync(long caseId, string result, bool appealed, bool received, string? currency,
        decimal? prevented = null, decimal? preventedM2 = null, decimal? caused = null) =>
        await Scalar("""
            INSERT INTO case_decisions (case_id, result_id, currency, prevented_damage_amount, prevented_damage_area_m2,
                                        caused_damage_amount, is_appealed, decision_received, decision_received_at, recorded_by)
            VALUES (@c, (SELECT id FROM decision_results WHERE code = @r), @cur, @p, @pm2, @ca, @ap, @rec, IF(@rec, '2027-01-20', NULL), @k)
            """, ("@c", caseId), ("@r", result), ("@cur", currency), ("@p", prevented), ("@pm2", preventedM2), ("@ca", caused),
            ("@ap", appealed), ("@rec", received), ("@k", await UserId("kryetar@ash.local")));

    private async Task<Scenario> ScenarioAsync()
    {
        var office = await Id("SELECT id FROM offices WHERE code = 'TR'");
        var court1 = await Id("SELECT id FROM courts WHERE name = 'Gjykata e Shkallës së Parë Tiranë'");
        var court2 = await Id("SELECT id FROM courts WHERE name = 'Gjykata e Apelit Tiranë'");

        var a1 = await CaseAsync(office, null, 1, court1, Utc(2026, 10, 20), Utc(2026, 12, 10));
        await PrimaryAsync(a1, "avokat1@ash.local", Utc(2026, 10, 21));
        await DecisionAsync(a1, "HUMBUR", appealed: true, received: false, "ALL", caused: 2_500_000m);
        var a2 = await CaseAsync(office, await Id("SELECT dossier_id FROM cases WHERE id = @id", ("@id", a1)), 2, court2, Utc(2026, 12, 15), null);
        await PrimaryAsync(a2, "avokat1@ash.local", Utc(2026, 12, 16), Utc(2027, 2, 1));
        await PrimaryAsync(a2, "avokat2@ash.local", Utc(2027, 2, 1));

        var b = await CaseAsync(office, null, 1, court1, Utc(2026, 3, 1), Utc(2027, 3, 1));
        await PrimaryAsync(b, "avokat2@ash.local", Utc(2026, 3, 2));
        await DecisionAsync(b, "FITUAR", appealed: false, received: true, "EUR", prevented: 1000m, preventedM2: 50m);

        // 31.12.2026 23:30 in Tirane = 22:30 UTC: still 2026
        var c = await CaseAsync(office, null, 1, court1, Utc(2026, 12, 31, 22, 30), null);
        await PrimaryAsync(c, "avokat1@ash.local", Utc(2027, 1, 5));

        // the ministry's agency (parent_institution_id) is the defendant of D
        var ministry = await Id("SELECT institution_id FROM users WHERE email = 'institucion@ash.local'");
        var agency = await Id("""
            INSERT INTO institutions (name, short_name, institution_type, parent_institution_id)
            VALUES (CONCAT('Agjencia test ', UUID()), 'AGJ', 'AGENCY', @m); SELECT LAST_INSERT_ID();
            """, ("@m", ministry));
        var d = await CaseAsync(office, null, 1, court1, Utc(2027, 4, 5), null, "DELEGIM");
        await Scalar("UPDATE cases SET is_suspended = 1 WHERE id = @id", ("@id", d));
        await Scalar("""
            INSERT INTO case_parties (case_id, party_role_id, party_kind, institution_id, display_name, created_by)
            SELECT @c, id, 'INSTITUTION', @i, 'Agjencia', @u FROM party_roles WHERE code = 'I_PADITUR'
            """, ("@c", d), ("@i", agency), ("@u", await UserId("protokoll@ash.local")));

        return new Scenario(office, court1, court2, a1, a2, b, c, d, ministry, agency);
    }

    private static async Task<JsonElement> Get(HttpClient client, string query)
    {
        var response = await client.GetAsync($"{Stats}?{query}", Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        return JsonDocument.Parse(text).RootElement;
    }

    private static int Value(JsonElement stats, long? courtId, string measure) =>
        stats.GetProperty("courts").EnumerateArray()
            .Single(r => courtId is null
                ? r.GetProperty("courtId").ValueKind == JsonValueKind.Null
                : r.GetProperty("courtId").ValueKind == JsonValueKind.Number && r.GetProperty("courtId").GetInt64() == courtId)
            .GetProperty("values").GetProperty(measure).GetInt32();

    private static async Task<int> DrillTotal(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<JsonElement>($"{Stats}/cases?{query}", Ct)).GetProperty("total").GetInt32();

    // ---------------------------------------------------------------- the figures

    [Fact]
    public async Task Year_2027_carries_one_active_and_one_awaiting_the_reasoned_decision_and_2026_is_counted_at_its_own_points()
    {
        var s = await ScenarioAsync();
        await using var factory = Factory();
        using var admin = factory.CreateClient().AsUser("admin@ash.local");

        // the acceptance criterion (the ash-db scenario): 2027
        var y2027 = await Get(admin, "year=2027");
        y2027.GetProperty("frozen").GetBoolean().ShouldBeFalse();
        y2027.GetProperty("warning").ValueKind.ShouldBe(JsonValueKind.Null); // the current year is live by design
        Value(y2027, null, "mbartur_pritje_vendimi").ShouldBe(1);            // A/1: closed 2026, decision awaited
        Value(y2027, null, "mbartur_aktive").ShouldBe(3);                    // A/2, B, C open on 1.1.2027
        Value(y2027, s.Court2, "mbartur_aktive").ShouldBe(1);                // the ash-db scenario's case: A/2 at the APPEAL court
        Value(y2027, null, "ardhur_gjate_vitit").ShouldBe(1);                // D
        Value(y2027, null, "total").ShouldBe(4);
        Value(y2027, null, "transferuar").ShouldBe(1);                       // A/2 on 1.2.2027
        Value(y2027, null, "perfunduar_ash").ShouldBe(1);                    // B, closed 1.3.2027
        Value(y2027, null, "aktive").ShouldBe(3);                            // today: A/2, C, D
        Value(y2027, null, "deleguar").ShouldBe(1);
        Value(y2027, null, "aktive_ndjekje").ShouldBe(2);
        Value(y2027, null, "pezulluar_deleguar").ShouldBe(1);

        // 2026, a closed year without snapshot: computed live, with the warning
        var y2026 = await Get(admin, "year=2026");
        y2026.GetProperty("frozen").GetBoolean().ShouldBeFalse();
        y2026.GetProperty("warning").GetString()!.ShouldStartWith("Jo e ngrirë");
        Value(y2026, null, "ardhur_gjate_vitit").ShouldBe(4);                // A/1, A/2, B, and C (31.12. 23:30 Tirane)
        Value(y2026, null, "mbartur_aktive").ShouldBe(0);
        // open at the END of 2026 — B too, although it was closed in 2027 (point in time, not "today")
        Value(y2026, null, "aktive").ShouldBe(3);                            // A/2, B, C
        Value(y2026, null, "perfunduar_ash").ShouldBe(1);                    // A/1
        Value(y2026, null, "ankimuar_rekursuar").ShouldBe(1);
        Value(y2026, s.Court1, "ardhur_gjate_vitit").ShouldBe(3);            // A/1, B, C at the first-instance court
        Value(y2026, s.Court2, "ardhur_gjate_vitit").ShouldBe(1);            // A/2 at the APPEAL court
        Value(y2026, s.Court2, "aktive").ShouldBe(1);

        // money per currency, never summed across: A/1 caused 2,500,000 ALL in 2026; B prevented 1,000 EUR in 2027
        var money2026 = y2026.GetProperty("money").EnumerateArray().ToList();
        money2026.Select(m => m.GetProperty("currency").GetString()).Distinct().ShouldBe(["ALL"]);
        var money2027 = y2027.GetProperty("money").EnumerateArray().ToList();
        var eur = money2027.Single(m => m.GetProperty("currency").GetString() == "EUR" && m.GetProperty("courtId").ValueKind == JsonValueKind.Null);
        eur.GetProperty("preventedAmount").GetDecimal().ShouldBe(1000m);
        eur.GetProperty("preventedM2").GetDecimal().ShouldBe(50m);
        money2027.ShouldNotContain(m => m.GetProperty("currency").GetString() == "ALL");

        // Rezultati and Në dorë
        y2027.GetProperty("results").EnumerateArray().Single().GetProperty("resultCode").GetString().ShouldBe("FITUAR");
        y2027.GetProperty("inHand").EnumerateArray().Select(l => l.GetProperty("lawyer").GetString()).ShouldBe(
            ["Avokat1 Dev", "Avokat2 Dev"], ignoreOrder: true); // C (avokat1), A/2 (avokat2); D is delegated

        // the lawyer filter: open measures by the primary at the end point, closed ones by the primary at closing
        var avokat1 = await PublicIdOf("users", await UserId("avokat1@ash.local"));
        var avokat2 = await PublicIdOf("users", await UserId("avokat2@ash.local"));
        var mine1 = await Get(admin, $"year=2027&lawyerId={avokat1}");
        Value(mine1, null, "mbartur_pritje_vendimi").ShouldBe(1);           // A/1 closed under avokat1
        Value(mine1, null, "aktive").ShouldBe(1);                           // C (A/2 is avokat2's now)
        var mine2 = await Get(admin, $"year=2027&lawyerId={avokat2}");
        Value(mine2, null, "aktive").ShouldBe(1);                           // A/2
        Value(mine2, null, "perfunduar_ash").ShouldBe(1);                   // B closed under avokat2
    }

    /// <summary>
    /// Client decision 12.11.2026, section A: a level without a decision stays ACTIVE and is "mbartur aktive" on
    /// 1 January; a level closed with a decision but without the reasoned decision is CLOSED — not "mbartur aktive",
    /// it is "mbartur në pritje të vendimit të arsyetuar".
    /// </summary>
    [Fact]
    public async Task Section_A_no_decision_is_carried_active_closed_without_the_reasoned_decision_is_not()
    {
        var s = await ScenarioAsync();
        await using var factory = Factory();
        using var admin = factory.CreateClient().AsUser("admin@ash.local");
        async Task<List<string>> Codes(string measure) =>
            (await admin.GetFromJsonAsync<JsonElement>($"{Stats}/cases?year=2027&measure={measure}&limit=100", Ct))
                .GetProperty("items").EnumerateArray().Select(i => i.GetProperty("caseCode").GetString()!).ToList();
        async Task<string> Code(long id) => (string)(await Scalar("SELECT case_code FROM cases WHERE id = @id", ("@id", id)))!;

        // A/1: decision recorded 10.12.2026, reasoned decision not arrived
        (await Scalar("SELECT lifecycle_status FROM cases WHERE id = @id", ("@id", s.A1))).ShouldBe("CLOSED");
        var carried = await Codes("mbartur_aktive");
        carried.ShouldNotContain(await Code(s.A1));
        (await Codes("mbartur_pritje_vendimi")).ShouldBe([await Code(s.A1)]);
        // A/2 and C: no decision by 1.1.2027 — ACTIVE, carried; B too (its decision came on 1.3.2027)
        carried.ShouldBe([await Code(s.A2), await Code(s.B), await Code(s.C)], ignoreOrder: true);
    }

    [Fact]
    public async Task The_drill_down_lists_exactly_as_many_cases_as_every_number_shows()
    {
        var s = await ScenarioAsync();
        await using var factory = Factory();
        using var admin = factory.CreateClient().AsUser("admin@ash.local");

        var checkedCells = 0;
        foreach (var year in new[] { 2026, 2027 })
        {
            var stats = await Get(admin, $"year={year}");
            foreach (var row in stats.GetProperty("courts").EnumerateArray())
            {
                var court = row.GetProperty("courtId").ValueKind == JsonValueKind.Null ? "" : $"&courtId={row.GetProperty("courtId").GetInt64()}";
                foreach (var measure in StatisticsMeasures.Counts)
                {
                    var shown = row.GetProperty("values").GetProperty(measure.Code).GetInt32();
                    (await DrillTotal(admin, $"year={year}&measure={measure.Code}{court}")).ShouldBe(shown, $"{year} {measure.Code} {court}");
                    checkedCells++;
                }
            }

            foreach (var lawyer in stats.GetProperty("inHand").EnumerateArray())
            {
                (await DrillTotal(admin, $"year={year}&measure=ne_dore&rowLawyerId={lawyer.GetProperty("lawyerId").GetGuid()}"))
                    .ShouldBe(lawyer.GetProperty("count").GetInt32());
            }

            foreach (var result in stats.GetProperty("results").EnumerateArray())
            {
                var code = result.GetProperty("resultCode").GetString();
                (await DrillTotal(admin, $"year={year}&measure=perfunduar_ash&resultCode={code}")).ShouldBe(result.GetProperty("ash").GetInt32());
                (await DrillTotal(admin, $"year={year}&measure=perfunduar_deleguar&resultCode={code}")).ShouldBe(result.GetProperty("deleguar").GetInt32());
            }
        }

        checkedCells.ShouldBeGreaterThan(60);

        // a page of the drill-down: case code, court, lawyer, status, dates
        var page = await admin.GetFromJsonAsync<JsonElement>($"{Stats}/cases?year=2027&measure=mbartur_aktive&courtId={s.Court2}", Ct);
        var item = page.GetProperty("items").EnumerateArray().Single();
        item.GetProperty("court").GetString().ShouldBe("Gjykata e Apelit Tiranë");
        item.GetProperty("lawyer").GetString().ShouldBe("Avokat2 Dev");
        item.GetProperty("status").GetString().ShouldBe("ACTIVE");
        (await admin.GetAsync($"{Stats}/cases?year=2027&measure=DROP TABLE cases", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Tabela_6_by_ministry_includes_its_agencies_and_by_institution_only_itself()
    {
        var s = await ScenarioAsync();
        await using var factory = Factory();
        using var admin = factory.CreateClient().AsUser("admin@ash.local");
        var ministry = await PublicIdOf("institutions", s.Ministry);
        var agency = await PublicIdOf("institutions", s.Agency);

        Value(await Get(admin, $"year=2027&table=6&ministryId={ministry}"), null, "ardhur_gjate_vitit").ShouldBe(1); // D, via its agency
        Value(await Get(admin, $"year=2027&table=6&institutionId={ministry}"), null, "ardhur_gjate_vitit").ShouldBe(0);
        Value(await Get(admin, $"year=2027&table=6&institutionId={agency}"), null, "ardhur_gjate_vitit").ShouldBe(1);
        (await admin.GetAsync($"{Stats}?year=2027&table=6", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---------------------------------------------------------------- access

    [Fact]
    public async Task Each_role_sees_only_its_own_statistics()
    {
        await ScenarioAsync();
        await using var factory = Factory();
        var otherOffice = await Id("""
            INSERT INTO offices (code, name, office_type) VALUES (CONCAT('X', LEFT(UUID(), 6)), 'Zyra tjetër', 'LOCAL'); SELECT LAST_INSERT_ID();
            """);
        var other = await PublicIdOf("offices", otherOffice);
        var tr = await PublicIdOf("offices", await Id("SELECT id FROM offices WHERE code = 'TR'"));

        using var kryetar = factory.CreateClient().AsUser("kryetar@ash.local");
        (await kryetar.GetAsync($"{Stats}?year=2027&officeId={other}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var own = await Get(kryetar, "year=2027");
        own.GetProperty("filters").EnumerateArray().ShouldContain(f => f.GetProperty("label").GetString() == "Zyra");
        Value(own, null, "total").ShouldBe(4);
        (await kryetar.GetAsync($"{Stats}?year=2027&officeId={tr}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var lawyer = factory.CreateClient().AsUser("avokat1@ash.local");
        Value(await Get(lawyer, "year=2027"), null, "aktive").ShouldBe(1); // forced to their own cases (C)
        var avokat2 = await PublicIdOf("users", await UserId("avokat2@ash.local"));
        (await lawyer.GetAsync($"{Stats}?year=2027&lawyerId={avokat2}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var institution = factory.CreateClient().AsUser("institucion@ash.local");
        (await institution.GetAsync($"{Stats}?year=2027&table=5", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await institution.GetAsync($"{Stats}?year=2027&table=6", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var protokoll = factory.CreateClient().AsUser("protokoll@ash.local");
        (await protokoll.GetAsync($"{Stats}?year=2027", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await protokoll.GetAsync($"{Stats}/export?year=2027", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await protokoll.GetAsync($"{Stats}/cases?year=2027&measure=total", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ---------------------------------------------------------------- the snapshot

    [Fact]
    public async Task A_snapshot_freezes_a_closed_year_against_later_changes_and_only_an_admin_retakes_it()
    {
        var s = await ScenarioAsync();
        await using var factory = Factory();
        using var admin = factory.CreateClient().AsUser("admin@ash.local");
        using var kryetar = factory.CreateClient().AsUser("kryetar@ash.local");

        var reason = new { reason = "Ngrirja vjetore e testit" };
        (await kryetar.PostAsJsonAsync("/api/admin/statistics/snapshot/2026", reason, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await admin.PostAsJsonAsync("/api/admin/statistics/snapshot/2027", reason, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest); // not closed
        var noReason = await admin.PostAsJsonAsync("/api/admin/statistics/snapshot/2026", new { reason = "  shkurt  " }, Ct);
        noReason.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await noReason.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("statistics.snapshot_reason");
        var taken = await admin.PostAsJsonAsync("/api/admin/statistics/snapshot/2026", reason, Ct);
        taken.StatusCode.ShouldBe(HttpStatusCode.OK, await taken.Content.ReadAsStringAsync(Ct));

        var frozen = await Get(admin, "year=2026");
        frozen.GetProperty("frozen").GetBoolean().ShouldBeTrue();
        frozen.GetProperty("warning").ValueKind.ShouldBe(JsonValueKind.Null);
        Value(frozen, null, "aktive").ShouldBe(3);
        Value(frozen, null, "deleguar").ShouldBe(0);

        // C is delegated afterwards: 2026 stays as published; the live computation would differ
        await Scalar("UPDATE cases SET category_id = (SELECT id FROM case_categories WHERE code = 'DELEGIM') WHERE id = @id", ("@id", s.C));
        var after = await Get(admin, "year=2026");
        Value(after, null, "deleguar").ShouldBe(0);
        Value(after, null, "aktive_ndjekje").ShouldBe(Value(frozen, null, "aktive_ndjekje"));
        // the drill-down reads the same frozen rows
        (await DrillTotal(admin, "year=2026&measure=aktive_ndjekje")).ShouldBe(Value(after, null, "aktive_ndjekje"));

        // the yearly job (1 January) does nothing for a year already frozen; a re-run by the admin replaces it — audited
        (await admin.PostAsJsonAsync("/api/admin/statistics/snapshot/2026", new { reason = "Korrigjim i kategorisë së çështjes C" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        Value(await Get(admin, "year=2026"), null, "deleguar").ShouldBe(1);
        var deadline = DateTime.UtcNow.AddSeconds(15);
        List<string> audits;
        while ((audits = await AuditDetails()).Count < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100, Ct);
        }

        audits.Count.ShouldBe(2);
        using var replaced = JsonDocument.Parse(audits[1]);
        replaced.RootElement.GetProperty("action").GetString().ShouldBe("STATISTICS_SNAPSHOT_REPLACED");
        replaced.RootElement.GetProperty("reason").GetString().ShouldBe("Korrigjim i kategorisë së çështjes C");
        using var first = JsonDocument.Parse(audits[0]);
        first.RootElement.GetProperty("reason").GetString().ShouldBe("Ngrirja vjetore e testit");
    }

    /// <summary>The details of the snapshot audit rows of 2026, oldest first.</summary>
    private async Task<List<string>> AuditDetails()
    {
        await using var connection = new MySqlConnection(Db.AuditConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(
            "SELECT details FROM ash_audit.audit_log WHERE entity_type = 'STATISTICS_SNAPSHOT' AND entity_id = 2026 ORDER BY id", connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var rows = new List<string>();
        while (await reader.ReadAsync(Ct))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    [Fact]
    public async Task The_yearly_job_freezes_the_year_that_just_ended_once()
    {
        await ScenarioAsync();
        await using var factory = Factory();
        var runner = factory.Services.GetRequiredService<Ash.Infrastructure.Jobs.JobRunner>();

        // 23:30 UTC on 31.12 = 00:30 on 1.1.2027 in Tirane (UTC+1 in winter): the job's own time
        var outcome = await runner.RunAsync("statistics-snapshot", new DateTime(2026, 12, 31, 23, 30, 0, DateTimeKind.Utc), Ct);
        outcome.ShouldBe(Ash.Infrastructure.Jobs.JobOutcome.Succeeded,
            (string?)await Scalar("SELECT last_error FROM ash_ops.job_runs WHERE job_name = 'statistics-snapshot'"));
        (await Scalar("SELECT case_count FROM statistics_snapshots WHERE year = 2026")).ShouldBe(4u);
        var takenAt = await Scalar("SELECT created_at FROM statistics_snapshots WHERE year = 2026");
        await runner.RunAsync("statistics-snapshot", new DateTime(2027, 1, 2, 0, 0, 0, DateTimeKind.Utc), Ct);
        (await Scalar("SELECT created_at FROM statistics_snapshots WHERE year = 2026")).ShouldBe(takenAt);
    }

    // ---------------------------------------------------------------- export

    [Fact]
    public async Task The_export_has_the_three_sheets_numbers_and_one_money_column_per_currency()
    {
        await ScenarioAsync();
        await using var factory = Factory();
        using var admin = factory.CreateClient().AsUser("admin@ash.local");

        var response = await admin.GetAsync($"{Stats}/export?year=2027", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        response.Content.Headers.ContentDisposition!.FileNameStar.ShouldBe("raporti-statistikat-2027-2027-06-15.xlsx");
        using var doc = SpreadsheetDocument.Open(new MemoryStream(await response.Content.ReadAsByteArrayAsync(Ct)), false);
        var names = doc.WorkbookPart!.Workbook.Descendants<Sheet>().Select(x => x.Name!.Value).ToList();
        names.ShouldBe(["Tabela 5", "Rezultati", "Në dorë", "Filtrat"]);

        var sheet = (WorksheetPart)doc.WorkbookPart.GetPartById(doc.WorkbookPart.Workbook.Descendants<Sheet>().First().Id!);
        var rows = sheet.Worksheet.Descendants<Row>().Select(r => r.Elements<Cell>().ToList()).ToList();
        string Text(Cell c) => c.DataType?.Value == CellValues.InlineString ? c.InlineString!.Text!.Text : c.CellValue?.Text ?? "";
        var headers = rows[0].Select(Text).ToList();
        headers.ShouldContain("Dëm i parandaluar (EUR)");
        headers.ShouldContain("Dëm i parandaluar (m²)");
        headers.ShouldNotContain(h => h.Contains("(ALL)")); // no ALL amount in 2027
        var total = rows.Single(r => Text(r[0]) == "Gjithsej");
        var totalCell = total[headers.IndexOf("Total")];
        totalCell.DataType!.Value.ShouldBe(CellValues.Number);
        totalCell.CellValue!.Text.ShouldBe("4");
        decimal.Parse(total[headers.IndexOf("Dëm i parandaluar (EUR)")].CellValue!.Text, CultureInfo.InvariantCulture).ShouldBe(1000m);
    }

    // ---------------------------------------------------------------- the SQL is parameterised

    [Fact]
    public void The_statistics_SQL_is_never_built_from_strings_at_request_time()
    {
        // from this source file (the build output may be anywhere, e.g. --artifacts-path): Tests/Ash.Tests.Integration/…
        var backend = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFile())!, "..", ".."));
        var module = Path.Combine(backend, "Modules", "Reports", "Ash.Modules.Reports");
        Directory.Exists(module).ShouldBeTrue(module);

        // the request-time code: no interpolated or concatenated SQL, no builders, no Replace
        foreach (var file in new[] { "StatisticsQueries.cs", "StatisticsScope.cs", "StatisticsSnapshots.cs" })
        {
            var code = File.ReadAllText(Path.Combine(module, "Application", "Statistics", file));
            code.ShouldNotContain("StringBuilder");
            code.ShouldNotContain("string.Concat");
            code.ShouldNotContain(".Replace(");
            code.ShouldNotContain("string.Format");
            Regex.IsMatch(code, "\\$\"[^\"]*\\b(SELECT|WHERE|INSERT|UPDATE|DELETE|FROM)\\b").ShouldBeFalse($"{file}: an interpolated SQL string");
            Regex.IsMatch(code, "\"\\s*\\+|\\+\\s*\"").ShouldBeFalse($"{file}: a concatenated string");
        }

        // the SQL files take values only as @parameters (no placeholders to fill in)
        var files = Directory.GetFiles(Path.Combine(module, "Infrastructure", "Sql"), "Statistics*.sql");
        files.Length.ShouldBeGreaterThan(5);
        foreach (var sql in files)
        {
            File.ReadAllText(sql).ShouldNotContain("{", customMessage: Path.GetFileName(sql));
        }
    }

    private static string ThisFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}
