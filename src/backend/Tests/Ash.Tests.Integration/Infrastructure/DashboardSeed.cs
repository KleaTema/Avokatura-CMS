using Ash.Modules.Cases.Application.ListCases;
using MySql.Data.MySqlClient;

namespace Ash.Tests.Integration.Infrastructure;

/// <summary>
/// Task 046: <c>count</c> cases (5,000 in the normal suite, 50,000 in the opt-in Perf test) inserted with recursive-CTE
/// INSERT … SELECTs, relative to a fixed <c>now</c> (the API gets the same instant as its IClock). Case n:
/// - office TR when n % offices = 0 (registered by protokoll), else one of the fresh offices (2 offices: TR = n even,
///   Durrës = n odd); status REGISTERED / ACTIVE / CLOSED by n % 3;
/// - registered n % 20 days ago; primary assigned (cases.assigned_at) n % 240 days ago; data entered unless n % 5 = 0;
/// - one SCHEDULED hearing (open cases): none when n % 10 &lt; 3, two days overdue when 3–4, this Tirane week when 5–7,
///   in a month when 8–9 — next_hearing_at mirrors it;
/// - lawyers (not REGISTERED): TR avokat1 primary when n % 4 = 0, else avokat2 (+ avokat1 secondary when n % 20 = 10);
///   DR its own lawyer; the ministry's jurist on its linked cases when n % 10 &lt; 5;
/// - the ministry linked MONITORING when n % 5 = 0, DELEGATED when n % 5 = 1;
/// - CLOSED: a decision, the reasoned one received when n is odd; one history row per case (its registration).
/// </summary>
public static class DashboardSeed
{
    public sealed record Ids(long TrOffice, long DrOffice, string DrKryetarEmail);

    /// <param name="offices">
    /// How many offices share the cases: TR gets n % offices = 0, each other (new) office its own residue, with its own
    /// lawyer and Protokoll. 2 = TR and Durrës, half each (the normal tests). The 50,000 Perf variant uses 20, as a
    /// real installation spreads its cases — with 2, one office would hold 25,000 cases and one lawyer ~12,000.
    /// </param>
    /// <param name="spreadHearings">
    /// Future hearings over the next 52 weeks (about 1 in 52 this week) instead of 30% of all open cases this week.
    /// </param>
    public static async Task<Ids> CreateAsync(
        string connectionString, int count, DateTime nowUtc, int offices = 2, bool spreadHearings = false)
    {
        await using var seed = await Seed.CreateAsync(connectionString);
        var others = new List<(long Office, long Lawyer, long Protokoll)>();
        for (var i = 1; i < offices; i++)
        {
            var name = i == 1 ? "Durrës" : $"Zyra {i}";
            var office = await seed.OfficeAsync($"Zyra {name} {seed.Suffix}", "LOCAL", i == 1 ? null : $"T{seed.Suffix}{i}");
            await seed.UserAsync(i == 1 ? "drkryetar" : $"kryetar{i}", "Kryetar", name, office, null, "OFFICE_HEAD");
            others.Add((office, await seed.UserAsync(i == 1 ? "drlawyer" : $"lawyer{i}", "Avokat", name, office, null, "STATE_LAWYER"),
                await seed.UserAsync(i == 1 ? "drprot" : $"prot{i}", "Protokoll", name, office, null, "PROTOCOL")));
        }

        var dr = others[0].Office;
        var court = await seed.CourtAsync("Gjykata e Shkallës së Parë Tiranë");
        string Elt(string index, Func<(long Office, long Lawyer, long Protokoll), long> pick) =>
            $"ELT({index}, {string.Join(", ", others.Select(o => pick(o)))})";
        var officeOf = $"IF(n % {offices} = 0, @tr, {Elt($"n % {offices}", o => o.Office)})";
        var protokollOf = $"IF(n % {offices} = 0, @prot, {Elt($"n % {offices}", o => o.Protokoll)})";
        var officeIds = string.Join(", ", others.Select(o => o.Office));
        var otherLawyerOf = Elt($"FIELD(c.office_id, {officeIds})", o => o.Lawyer);
        var future = spreadHearings
            ? "@week + INTERVAL (s.n % 364) DAY + INTERVAL 9 HOUR"
            : "@week + INTERVAL ((s.n % 7) * 24 + 9) HOUR";

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        async Task<long> Id(string sql)
        {
            await using var command = new MySqlCommand(sql, connection);
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        var tr = await Id("SELECT id FROM offices WHERE code = 'TR'");
        var week = CaseListSpecialFilter.TiraneWeek(nowUtc).FromUtc;
        var seq = $"WITH RECURSIVE seq (n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM seq WHERE n < {count}) ";
        const string n = "CAST(SUBSTRING(c.act_number, 5) AS UNSIGNED)";
        var prefix = $"S{seed.Suffix}";
        var sql = $"""
            SET SESSION cte_max_recursion_depth = 1000000;
            INSERT INTO dossiers (unique_code, code_format_id, office_id, opened_by)
            {seq}
            SELECT CONCAT('{prefix}-', LPAD(n, 6, '0')), (SELECT id FROM dossier_code_formats WHERE is_active = 1),
                   {officeOf}, {protokollOf}
              FROM seq;
            INSERT INTO cases (dossier_id, level_seq, case_code, office_id, summons_number, act_number, received_date,
                               court_level_id, court_id, object_text, lifecycle_status, priority, next_hearing_at,
                               registered_by, registered_at, assigned_at, first_data_entry_at, closed_at, closed_by)
            {seq}
            SELECT d.id, 1, CONCAT(d.unique_code, '/1'), d.office_id, CONCAT('FL-', s.n), CONCAT('AKT-', s.n), '2026-01-01',
                   (SELECT id FROM court_levels WHERE code = 'FIRST_INSTANCE'), @court, CONCAT('Objekt ', s.n),
                   ELT(s.n % 3 + 1, 'REGISTERED', 'ACTIVE', 'CLOSED'), 'NONE',
                   CASE WHEN s.n % 3 = 2 OR s.n % 10 < 3 THEN NULL
                        WHEN s.n % 10 < 5 THEN @now - INTERVAL 2 DAY
                        WHEN s.n % 10 < 8 THEN {future}
                        ELSE @now + INTERVAL 30 DAY END,
                   d.opened_by,
                   @now - INTERVAL (s.n % 20) DAY - INTERVAL (s.n % 5) HOUR,
                   IF(s.n % 3 = 0, NULL, @now - INTERVAL (s.n % 240) DAY),
                   IF(s.n % 5 = 0, NULL, @now - INTERVAL 1 DAY),
                   IF(s.n % 3 = 2, @now - INTERVAL (s.n % 30) DAY, NULL),
                   IF(s.n % 3 = 2, @kryetar, NULL)
              FROM seq s JOIN dossiers d ON d.unique_code = CONCAT('{prefix}-', LPAD(s.n, 6, '0'));
            INSERT INTO case_hearings (case_id, hearing_type_id, scheduled_at, status, created_by)
            SELECT c.id, (SELECT MIN(id) FROM hearing_types), c.next_hearing_at, 'SCHEDULED', @prot
              FROM cases c WHERE c.case_code LIKE '{prefix}-%' AND c.next_hearing_at IS NOT NULL;
            INSERT INTO case_assignments (case_id, user_id, case_role, assigned_by)
            SELECT c.id, IF(c.office_id = @tr, IF({n} % 4 = 0, @av1, @av2), {otherLawyerOf}), 'PRIMARY_LAWYER', @kryetar
              FROM cases c WHERE c.case_code LIKE '{prefix}-%' AND c.lifecycle_status <> 'REGISTERED';
            INSERT INTO case_assignments (case_id, user_id, case_role, assigned_by)
            SELECT c.id, @av1, 'SECONDARY_LAWYER', @kryetar
              FROM cases c WHERE c.case_code LIKE '{prefix}-%' AND c.lifecycle_status <> 'REGISTERED' AND c.office_id = @tr AND {n} % 20 = 10;
            INSERT INTO case_institutions (case_id, institution_id, link_mode, started_by)
            SELECT c.id, @ministry, IF({n} % 5 = 0, 'MONITORING', 'DELEGATED'), @prot
              FROM cases c WHERE c.case_code LIKE '{prefix}-%' AND {n} % 5 IN (0, 1);
            INSERT INTO case_assignments (case_id, user_id, case_role, assigned_by)
            SELECT c.id, @jurist, 'INSTITUTION_JURIST', @institucion
              FROM cases c WHERE c.case_code LIKE '{prefix}-%' AND {n} % 5 IN (0, 1) AND {n} % 10 < 5;
            INSERT INTO case_decisions (case_id, result_id, decision_received, decision_received_at, recorded_by)
            SELECT c.id, (SELECT MIN(id) FROM decision_results), {n} % 2, IF({n} % 2 = 1, @now, NULL), @kryetar
              FROM cases c WHERE c.case_code LIKE '{prefix}-%' AND c.lifecycle_status = 'CLOSED';
            INSERT INTO case_history_events (dossier_id, case_id, event_type, actor_user_id, occurred_at, summary)
            SELECT c.dossier_id, c.id, 'CASE_REGISTERED', c.registered_by, c.registered_at, CONCAT('Çështja u regjistrua: ', c.case_code)
              FROM cases c WHERE c.case_code LIKE '{prefix}-%';
            """;
        await using (var command = new MySqlCommand(sql, connection))
        {
            command.CommandTimeout = 600;
            foreach (var (name, value) in new (string, object)[]
                     {
                         ("@now", nowUtc), ("@week", week), ("@tr", tr), ("@court", court),
                         ("@prot", await Id("SELECT id FROM users WHERE email = 'protokoll@ash.local'")),
                         ("@kryetar", await Id("SELECT id FROM users WHERE email = 'kryetar@ash.local'")),
                         ("@av1", await Id("SELECT id FROM users WHERE email = 'avokat1@ash.local'")),
                         ("@av2", await Id("SELECT id FROM users WHERE email = 'avokat2@ash.local'")),
                         ("@jurist", await Id("SELECT id FROM users WHERE email = 'jurist@ash.local'")),
                         ("@institucion", await Id("SELECT id FROM users WHERE email = 'institucion@ash.local'")),
                         ("@ministry", await Id("SELECT institution_id FROM users WHERE email = 'institucion@ash.local'")),
                     })
            {
                command.Parameters.AddWithValue(name, value);
            }

            await command.ExecuteNonQueryAsync();
        }

        await using (var analyze = new MySqlCommand(
                         "ANALYZE TABLE cases, case_assignments, case_institutions, case_hearings, case_decisions, case_history_events", connection))
        {
            await analyze.ExecuteNonQueryAsync();
        }

        return new Ids(tr, dr, $"drkryetar-{seed.Suffix}@ash.test");
    }
}
