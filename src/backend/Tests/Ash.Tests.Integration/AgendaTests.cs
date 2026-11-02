using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ash.Infrastructure;
using Ash.Modules.Cases;
using Ash.Modules.Cases.Application.Agenda;
using Ash.Modules.Reference;
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
/// Task 031 — the agenda. Scenario (week 09–15.11.2026, Tirane = UTC+1):
/// TR cases A, B, C with avokat1 primary; D where avokat1 was primary and was transferred away (avokat2 now);
/// E with avokat2; the ministry monitors B (active link) and E (ended link); F in a new DR office (its own Kryetar
/// and lawyer). Hearings: A 10.11 09:00 + 20.11 (next week), B 10.11 09:30 (30 min after A: conflict for avokat1),
/// C 12.11 HELD + 13.11 CANCELLED, D 11.11 09:00, E 11.11 11:00, F 11.11 09:00.
/// </summary>
public class AgendaTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string Week = "from=2026-11-09&to=2026-11-15";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Scenario(
        long Tr, long Dr, Guid TrPublic, Guid DrPublic, Guid Avokat1, Guid Avokat2, string DrKryetarEmail,
        long A, long B, long C, long D, long E, long F,
        long A1, long ANextWeek, long B1, long CHeld, long CCancelled, long D1, long E1, long F1);

    private async Task<Scenario> SeedAsync()
    {
        await using var seed = await Seed.CreateAsync(Db.AppConnectionString);
        var dr = await seed.OfficeAsync($"Zyra Durrës {seed.Suffix}", "LOCAL");
        await seed.UserAsync("drkryetar", "Kryetar", "Durrës", dr, null, "OFFICE_HEAD");
        var drLawyer = await seed.UserAsync("drlawyer", "Avokat", "Durrës", dr, null, "STATE_LAWYER");
        var court = await seed.CourtAsync("Gjykata e Shkallës së Parë Tiranë");

        var tr = await Id("SELECT id FROM offices WHERE code = 'TR'");
        var prot = await Id("SELECT id FROM users WHERE email = 'protokoll@ash.local'");
        var kryetar = await Id("SELECT id FROM users WHERE email = 'kryetar@ash.local'");
        var av1 = await Id("SELECT id FROM users WHERE email = 'avokat1@ash.local'");
        var av2 = await Id("SELECT id FROM users WHERE email = 'avokat2@ash.local'");
        var ministry = await Id("SELECT id FROM institutions WHERE name = 'Ministria e Financave dhe Ekonomisë'");

        async Task<long> Case(long office, int n, long primary)
        {
            var dossier = await Insert(
                "INSERT INTO dossiers (unique_code, code_format_id, office_id, opened_by) " +
                "SELECT @u, id, @o, @p FROM dossier_code_formats WHERE is_active = 1",
                ("@u", $"2026-9{n:D5}"), ("@o", office), ("@p", prot));
            var id = await Insert(
                "INSERT INTO cases (dossier_id, level_seq, case_code, office_id, summons_number, act_number, received_date, " +
                "court_level_id, court_id, object_text, lifecycle_status, registered_by) " +
                "SELECT @d, 1, @cc, @o, @sn, @an, '2026-10-01', id, @ct, 'Objekt prove', 'ACTIVE', @p FROM court_levels WHERE code = 'FIRST_INSTANCE'",
                ("@d", dossier), ("@cc", $"2026-9{n:D5}/1"), ("@o", office), ("@sn", $"FL-{n}"), ("@an", $"AKT-{n}"),
                ("@ct", court), ("@p", prot));
            await Assign(id, primary, "PRIMARY_LAWYER");
            return id;
        }

        var a = await Case(tr, 1, av1);
        var b = await Case(tr, 2, av1);
        var c = await Case(tr, 3, av1);
        var d = await Case(tr, 4, av1);
        await Execute("UPDATE case_assignments SET ended_at = UTC_TIMESTAMP(3), ended_by = @k, end_reason = 'TRANSFERRED' WHERE case_id = @c",
            ("@k", kryetar), ("@c", d));
        await Assign(d, av2, "PRIMARY_LAWYER");
        var e = await Case(tr, 5, av2);
        var f = await Case(dr, 6, drLawyer);
        await Execute("INSERT INTO case_institutions (case_id, institution_id, link_mode, started_by) VALUES (@c, @i, 'MONITORING', @p)",
            ("@c", b), ("@i", ministry), ("@p", prot));
        await Execute("INSERT INTO case_institutions (case_id, institution_id, link_mode, started_by, ended_at, ended_by) " +
                      "VALUES (@c, @i, 'MONITORING', @p, UTC_TIMESTAMP(3), @p)", ("@c", e), ("@i", ministry), ("@p", prot));

        return new Scenario(tr, dr, await PublicIdOf("offices", tr), await PublicIdOf("offices", dr),
            await PublicIdOf("users", av1), await PublicIdOf("users", av2), $"drkryetar-{seed.Suffix}@ash.test",
            a, b, c, d, e, f,
            A1: await Hearing(a, "2026-11-10 08:00:00"),
            ANextWeek: await Hearing(a, "2026-11-20 08:00:00"),
            B1: await Hearing(b, "2026-11-10 08:30:00"),
            CHeld: await Hearing(c, "2026-11-12 08:00:00", "HELD"),
            CCancelled: await Hearing(c, "2026-11-13 08:00:00", "CANCELLED"),
            D1: await Hearing(d, "2026-11-11 08:00:00"),
            E1: await Hearing(e, "2026-11-11 10:00:00"),
            F1: await Hearing(f, "2026-11-11 08:00:00"));

        Task Assign(long caseId, long user, string role) =>
            Execute("INSERT INTO case_assignments (case_id, user_id, case_role, assigned_by) VALUES (@c, @u, @r, @k)",
                ("@c", caseId), ("@u", user), ("@r", role), ("@k", kryetar));
    }

    private async Task<long> Hearing(long caseId, string utc, string status = "SCHEDULED") =>
        await Insert(
            "INSERT INTO case_hearings (case_id, hearing_type_id, scheduled_at, status, created_by) " +
            "SELECT @c, id, @at, @s, (SELECT registered_by FROM cases WHERE id = @c) FROM hearing_types WHERE code = 'PERGATITORE'",
            ("@c", caseId), ("@at", DateTime.Parse(utc)), ("@s", status));

    // ---------------------------------------------------------------- own cases

    [Fact]
    public async Task Lawyer_sees_exactly_the_hearings_of_their_cases_in_the_week()
    {
        var s = await SeedAsync();
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser("avokat1@ash.local");

        var items = await Agenda(client, Week);

        // not D (transferred away), not E/F (others' cases), not A's next-week hearing, not the cancelled one
        (await Ids(items)).ShouldBe([s.A1, s.B1, s.CHeld]);
        JsonAssert.NoNumericIds(JsonDocument.Parse(JsonSerializer.Serialize(items)).RootElement);
        var first = items[0];
        first.GetProperty("caseCode").GetString().ShouldBe("2026-900001/1");
        first.GetProperty("scheduledAt").GetDateTime().ToUniversalTime().ShouldBe(new DateTime(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc));
        first.GetProperty("hearingType").GetProperty("code").GetString().ShouldBe("PERGATITORE");
        first.GetProperty("courtName").GetString().ShouldBe("Gjykata e Shkallës së Parë Tiranë");
        first.GetProperty("primaryLawyerName").GetString().ShouldBe("Avokat1 Dev");
        first.GetProperty("status").GetString().ShouldBe("SCHEDULED");
        items[2].GetProperty("status").GetString().ShouldBe("HELD");
    }

    [Fact]
    public async Task Two_hearings_30_minutes_apart_for_the_same_lawyer_conflict_on_both()
    {
        await SeedAsync();
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser("avokat1@ash.local");

        var items = await Agenda(client, Week);

        Conflict(items[0]).ShouldBe(["2026-900002/1"]);
        Conflict(items[1]).ShouldBe(["2026-900001/1"]);
        items[2].GetProperty("conflict").GetBoolean().ShouldBeFalse();
        Conflict(items[2]).ShouldBeEmpty();
    }

    [Fact]
    public async Task Cancelled_hearings_are_excluded_unless_asked_for()
    {
        var s = await SeedAsync();
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser("avokat1@ash.local");

        (await Ids(await Agenda(client, Week))).ShouldNotContain(s.CCancelled);
        var all = await Agenda(client, Week + "&includeCancelled=true");
        (await Ids(all)).ShouldBe([s.A1, s.B1, s.CHeld, s.CCancelled]);
        all[3].GetProperty("conflict").GetBoolean().ShouldBeFalse(); // a cancelled hearing never conflicts
    }

    [Fact]
    public async Task Range_across_the_DST_change_of_25_10_keeps_the_day_edges_in_Tirane()
    {
        var s = await SeedAsync();
        // 25.10.2026: 03:00 CEST → 02:00 CET; the day 25.10 is [24.10 22:00Z, 25.10 23:00Z) — 25 hours long
        var lastOf24 = await Hearing(s.A, "2026-10-24 21:59:59");
        var firstOf25 = await Hearing(s.A, "2026-10-24 22:00:00");
        var lastOf25 = await Hearing(s.A, "2026-10-25 22:59:59");
        var firstOf26 = await Hearing(s.A, "2026-10-25 23:00:00");
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser("avokat1@ash.local");

        (await Ids(await Agenda(client, "from=2026-10-25&to=2026-10-25"))).ShouldBe([firstOf25, lastOf25]);
        (await Ids(await Agenda(client, "from=2026-10-24&to=2026-10-24"))).ShouldBe([lastOf24]);
        (await Ids(await Agenda(client, "from=2026-10-26&to=2026-10-26"))).ShouldBe([firstOf26]);
        (await Ids(await Agenda(client, "from=2026-10-24&to=2026-10-26"))).ShouldBe([lastOf24, firstOf25, lastOf25, firstOf26]);
    }

    [Fact]
    public async Task Range_is_at_most_62_days_and_dates_are_required()
    {
        await SeedAsync();
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser("avokat1@ash.local");

        (await client.GetAsync("/api/agenda?from=2026-11-01&to=2027-01-01", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK); // 62 days
        await ShouldBe400(client, "from=2026-11-01&to=2027-01-02", "to"); // 63 days
        await ShouldBe400(client, "from=2026-11-10&to=2026-11-09", "to");
        await ShouldBe400(client, "to=2026-11-09", "from");
    }

    [Fact]
    public async Task Institution_user_sees_the_hearings_of_the_actively_linked_cases()
    {
        var s = await SeedAsync();
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser("institucion@ash.local");

        (await Ids(await Agenda(client, Week))).ShouldBe([s.B1]); // E's link has ended
        (await client.GetAsync($"/api/agenda?{Week}&lawyerId={s.Avokat1}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync($"/api/agenda?{Week}&officeId={s.TrPublic}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Lawyer_may_not_ask_for_another_lawyer()
    {
        var s = await SeedAsync();
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser("avokat1@ash.local");

        (await client.GetAsync($"/api/agenda?{Week}&lawyerId={s.Avokat2}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Ids(await Agenda(client, $"{Week}&lawyerId={s.Avokat1}"))).ShouldBe([s.A1, s.B1, s.CHeld]);
    }

    // ---------------------------------------------------------------- Kryetar and Monitor

    [Fact]
    public async Task Kryetar_sees_the_office_hearings_and_filters_by_lawyer()
    {
        var s = await SeedAsync();
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser("kryetar@ash.local");

        var items = await Agenda(client, Week);
        (await Ids(items)).ShouldBe([s.A1, s.B1, s.D1, s.E1, s.CHeld]);
        // per primary lawyer: A and B (avokat1) conflict; D and E (avokat2) are two hours apart
        Conflict(items[0]).ShouldBe(["2026-900002/1"]);
        Conflict(items[2]).ShouldBeEmpty();
        items[2].GetProperty("primaryLawyerName").GetString().ShouldBe("Avokat2 Dev");

        (await Ids(await Agenda(client, $"{Week}&lawyerId={s.Avokat2}"))).ShouldBe([s.D1, s.E1]);
        (await Ids(await Agenda(client, $"{Week}&officeId={s.TrPublic}"))).ShouldBe([s.A1, s.B1, s.D1, s.E1, s.CHeld]);
        (await client.GetAsync($"/api/agenda?{Week}&officeId={s.DrPublic}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Kryetar_of_another_office_sees_nothing_of_TR_and_may_not_filter_by_a_TR_lawyer()
    {
        var s = await SeedAsync();
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser(s.DrKryetarEmail);

        (await Ids(await Agenda(client, Week))).ShouldBe([s.F1]);
        (await client.GetAsync($"/api/agenda?{Week}&lawyerId={s.Avokat1}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync($"/api/agenda?{Week}&lawyerId={Guid.NewGuid()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Monitor_sees_all_and_filters_by_office_and_lawyer()
    {
        var s = await SeedAsync();
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser("monitor@ash.local");

        (await Ids(await Agenda(client, Week))).ShouldBe([s.A1, s.B1, s.D1, s.F1, s.E1, s.CHeld]);
        (await Ids(await Agenda(client, $"{Week}&officeId={s.DrPublic}"))).ShouldBe([s.F1]);
        (await Ids(await Agenda(client, $"{Week}&officeId={s.TrPublic}&lawyerId={s.Avokat1}"))).ShouldBe([s.A1, s.B1, s.CHeld]);
        await ShouldBe400(client, $"{Week}&officeId={Guid.NewGuid()}", "officeId");
    }

    [Fact]
    public async Task More_than_2000_hearings_ask_for_a_shorter_period()
    {
        var s = await SeedAsync();
        await Execute(
            "SET SESSION cte_max_recursion_depth = 3000; " +
            "INSERT INTO case_hearings (case_id, hearing_type_id, scheduled_at, status, created_by) " +
            "WITH RECURSIVE seq (n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 2000) " +
            "SELECT @c, (SELECT id FROM hearing_types WHERE code = 'PERGATITORE'), TIMESTAMP('2026-12-01') + INTERVAL n MINUTE, " +
            "'SCHEDULED', (SELECT registered_by FROM cases WHERE id = @c) FROM seq",
            ("@c", s.A));
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser("avokat1@ash.local");

        (await client.GetAsync("/api/agenda?from=2026-12-01&to=2026-12-31", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK); // exactly 2000
        await Hearing(s.B, "2026-12-15 08:00:00");
        var response = await client.GetAsync("/api/agenda?from=2026-12-01&to=2026-12-31", Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, text);
        text.ShouldContain("Zgjidhni një periudhë më të shkurtër.");
    }

    // ---------------------------------------------------------------- EXPLAIN

    [Theory]
    [InlineData("avokat1@ash.local", AgendaScope.Own, "case_assignments", "ix_ca_user_current")]
    [InlineData("kryetar@ash.local", AgendaScope.Office, "case_hearings", null)]
    public async Task Explain_the_agenda_query_uses_the_indexes(string email, AgendaScope scope, string table, string? expectedKey)
    {
        await SeedAsync();
        await BulkSeedAsync();
        await Execute("ANALYZE TABLE cases, case_assignments, case_institutions, case_hearings");

        var services = new ServiceCollection();
        services.AddAshInfrastructure(Db.AppConnectionString);
        services.AddReferenceModule(new ConfigurationBuilder().Build());
        services.AddCasesModule(new ConfigurationBuilder().Build());
        var currentUser = await LoadUser(email);
        services.AddScoped<ICurrentUser>(_ => currentUser);
        await using var provider = services.BuildServiceProvider();
        await using var scopeProvider = provider.CreateAsyncScope();
        var builder = scopeProvider.ServiceProvider.GetRequiredService<AgendaQueryBuilder>();

        var filter = new AgendaFilter(scope, new DateTime(2026, 11, 8, 23, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 15, 23, 0, 0, DateTimeKind.Utc), null,
            scope == AgendaScope.Office ? (ushort)currentUser.OfficeId!.Value : null, false);
        var sql = builder.Rows(filter).Take(2001).ToQueryString(); // as the handler: MaxItems + 1
        var plan = await Explain(sql);
        var aliases = Regex.Matches(sql, @"`(\w+)` AS `(\w+)`").ToDictionary(m => m.Groups[2].Value, m => m.Groups[1].Value, StringComparer.Ordinal);
        string TableOf(PlanRow row) => aliases.GetValueOrDefault(row.Table, row.Table);
        var text = $"{scope}\n{sql}\n" + string.Join('\n', plan.Select(r => $"{TableOf(r),-20} type={r.Type,-8} key={r.Key ?? "NULL",-28} rows={r.Rows} {r.Extra}"));
        TestContext.Current.TestOutputHelper?.WriteLine(text);

        foreach (var name in new[] { "case_hearings", "cases" })
        {
            var rows = plan.Where(r => TableOf(r) == name).ToList();
            rows.ShouldNotBeEmpty(text);
            rows.ShouldAllBe(r => r.Type != "ALL" && r.Key != null, text);
        }

        plan.Where(r => TableOf(r) == table).ShouldContain(r => r.Key != null && (expectedKey == null || r.Key == expectedKey), text);
    }

    /// <summary>3,000 more cases (TR/DR) with their primary lawyer and 4 hearings each over a year: the optimizer needs data.</summary>
    private async Task BulkSeedAsync()
    {
        var tr = await Id("SELECT id FROM offices WHERE code = 'TR'");
        var dr = await Id("SELECT id FROM offices WHERE code <> 'TR' ORDER BY id DESC LIMIT 1");
        var prot = await Id("SELECT id FROM users WHERE email = 'protokoll@ash.local'");
        var av2 = await Id("SELECT id FROM users WHERE email = 'avokat2@ash.local'");
        var kryetar = await Id("SELECT id FROM users WHERE email = 'kryetar@ash.local'");
        var court = await Id("SELECT id FROM courts WHERE name = 'Gjykata e Shkallës së Parë Tiranë'");
        const string seq = "WITH RECURSIVE seq (n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < 3000) ";
        await Execute($"""
            SET SESSION cte_max_recursion_depth = 10000;
            INSERT INTO dossiers (unique_code, code_format_id, office_id, opened_by)
            {seq}
            SELECT CONCAT('2026-', LPAD(n, 6, '0')), (SELECT id FROM dossier_code_formats WHERE is_active = 1),
                   IF(n % 2 = 0, @tr, @dr), @prot FROM seq;
            INSERT INTO cases (dossier_id, level_seq, case_code, office_id, summons_number, act_number, received_date,
                               court_level_id, court_id, object_text, lifecycle_status, registered_by)
            SELECT d.id, 1, CONCAT(d.unique_code, '/1'), d.office_id, 'FL', CONCAT('B-', d.id), '2026-01-01',
                   (SELECT id FROM court_levels WHERE code = 'FIRST_INSTANCE'), @court, 'Objekt', 'ACTIVE', @prot
              FROM dossiers d WHERE d.unique_code LIKE '2026-0%';
            INSERT INTO case_assignments (case_id, user_id, case_role, assigned_by)
            SELECT c.id, @av2, 'PRIMARY_LAWYER', @kryetar FROM cases c WHERE c.act_number LIKE 'B-%';
            INSERT INTO case_hearings (case_id, hearing_type_id, scheduled_at, status, created_by)
            SELECT c.id, (SELECT id FROM hearing_types WHERE code = 'PERGATITORE'),
                   TIMESTAMP('2026-01-01') + INTERVAL ((c.id * 37 + k.k * 9001) % 8760) HOUR, 'SCHEDULED', @prot
              FROM cases c JOIN (SELECT 1 AS k UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4) k
             WHERE c.act_number LIKE 'B-%';
            """,
            ("@tr", tr), ("@dr", dr), ("@prot", prot), ("@av2", av2), ("@kryetar", kryetar), ("@court", court));
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<List<JsonElement>> Agenda(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/agenda?{query}", Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        return JsonDocument.Parse(text).RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private static async Task ShouldBe400(HttpClient client, string query, string field)
    {
        var response = await client.GetAsync($"/api/agenda?{query}", Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, text);
        JsonDocument.Parse(text).RootElement.GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue(text);
    }

    private static List<string> Conflict(JsonElement item)
    {
        var with = item.GetProperty("conflictWith").EnumerateArray().Select(x => x.GetString()!).ToList();
        item.GetProperty("conflict").GetBoolean().ShouldBe(with.Count > 0);
        return with;
    }

    /// <summary>The internal ids of the returned hearings, in the returned order.</summary>
    private async Task<List<long>> Ids(List<JsonElement> items)
    {
        var byPublicId = new Dictionary<Guid, long>();
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand("SELECT id, public_id FROM case_hearings", connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            byPublicId[PublicId.FromBytes((byte[])reader[1])] = reader.GetInt64(0);
        }

        return items.Select(item => byPublicId[item.GetProperty("hearingPublicId").GetGuid()]).ToList();
    }

    private async Task<Guid> PublicIdOf(string table, long id) =>
        PublicId.FromBytes((byte[])(await Scalar($"SELECT public_id FROM {table} WHERE id = {id}"))!);

    private async Task<long> Id(string sql) => Convert.ToInt64(await Scalar(sql));

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(Ct);
    }

    private async Task<long> Insert(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(Ct);
        return command.LastInsertedId;
    }

    private async Task Execute(string sql, params (string Name, object Value)[] parameters) => await Insert(sql, parameters);

    // EXPLAIN helpers, as in CaseListTests (task 019)

    private sealed record PlanRow(string Table, string Type, string? Key, long Rows, string Extra);

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
        var id = await Id($"SELECT id FROM users WHERE email = '{email}'");
        var office = await Scalar($"SELECT office_id FROM users WHERE id = {id}");
        var institution = await Scalar($"SELECT institution_id FROM users WHERE id = {id}");
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);

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
        return new TestUser(id, office is DBNull or null ? null : Convert.ToInt64(office),
            institution is DBNull or null ? null : Convert.ToInt64(institution), roles, permissions);
    }
}
