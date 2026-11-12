using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Reports.Application.ActiveCases;
using Ash.Modules.Reports.Infrastructure;
using Ash.SharedKernel.Paging;
using Ash.SharedKernel.Results;
using Ash.SharedKernel.Time;
using Dapper;
using MySql.Data.MySqlClient;

namespace Ash.Modules.Reports.Application.ClosedCases;

/// <summary>Filters of GET /api/reports/closed-cases. ClosedFrom/ClosedTo are Tirane calendar dates (both included).</summary>
public sealed record ClosedCasesFilter(
    DateOnly? ClosedFrom,
    DateOnly? ClosedTo,
    string? ResultCode,
    string? OutcomeGroup,
    int? CourtId,
    Guid? LawyerId,
    Guid? OfficeId);

/// <summary>Tabela 3: one row per closed LEVEL. Money and m² separate; the currency per row (never mixed in a total).</summary>
public sealed record ClosedCaseRow(
    Guid CasePublicId,
    Guid DossierPublicId,
    string CaseCode,
    string ActNumber,
    string CourtLevel,
    string Court,
    string? Judge,
    string? Plaintiffs,
    string? Defendants,
    string? ObjectText,
    string? Lawyer,
    string Result,
    string? ResultOtherText,
    string? DecisionNumber,
    DateOnly? DecisionDate,
    string? Dispositive,
    string? Currency,
    decimal? WonAmount,
    decimal? WonAreaM2,
    decimal? LostAmount,
    decimal? LostAreaM2,
    decimal? PreventedDamageAmount,
    decimal? PreventedDamageAreaM2,
    decimal? CausedDamageAmount,
    decimal? CausedDamageAreaM2,
    decimal? CourtCostsAmount,
    bool HasSecurityMeasure,
    bool IsAppealed,
    bool IsRecursed,
    bool IsFinal,
    bool DecisionReceived,
    DateTime ClosedAt) : IReportRow
{
    public object? Get(string key) => key switch
    {
        "caseCode" => CaseCode,
        "actNumber" => ActNumber,
        "courtLevel" => CourtLevel,
        "court" => Court,
        "judge" => Judge,
        "plaintiffs" => Plaintiffs,
        "defendants" => Defendants,
        "objectText" => ObjectText,
        "lawyer" => Lawyer,
        "result" => Result,
        "resultOtherText" => ResultOtherText,
        "decisionNumber" => DecisionNumber,
        "decisionDate" => DecisionDate,
        "dispositive" => Dispositive,
        "currency" => Currency,
        "wonAmount" => WonAmount,
        "wonAreaM2" => WonAreaM2,
        "lostAmount" => LostAmount,
        "lostAreaM2" => LostAreaM2,
        "preventedDamageAmount" => PreventedDamageAmount,
        "preventedDamageAreaM2" => PreventedDamageAreaM2,
        "causedDamageAmount" => CausedDamageAmount,
        "causedDamageAreaM2" => CausedDamageAreaM2,
        "courtCostsAmount" => CourtCostsAmount,
        "hasSecurityMeasure" => HasSecurityMeasure,
        "isAppealed" => IsAppealed,
        "isRecursed" => IsRecursed,
        "isFinal" => IsFinal,
        "decisionReceived" => DecisionReceived,
        "closedAt" => ClosedAt,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };
}

/// <summary>
/// Tabela 3 (task 043): one row per closed level, ordered by dossier then level (keyset on (dossier_id, level_seq), the
/// unique key uq_cases_dossier_level), so the levels of a dossier stay together. Written like the active report (ids
/// first, then the page's columns, parties grouped). The lawyer is the PRIMARY AT CLOSING — the assignment with
/// assigned_at ≤ closed_at &lt; ended_at (or still open) — not the current one: a later transfer does not change it.
/// (v_report_closed_cases takes the latest primary by assigned_at instead; the view is left as it is — see
/// docs/client-questions.md, report-closed-lawyer.)
/// </summary>
public sealed class ClosedCasesReport(ReportingDb db, ReportScope scope, IOfficeDirectory offices, IUserDirectory users)
{
    private const string PrimaryAtClosing =
        "a.case_role = 'PRIMARY_LAWYER' AND a.assigned_at <= c.closed_at AND (a.ended_at IS NULL OR a.ended_at > c.closed_at)";

    private const string From = "cases c JOIN case_decisions d ON d.case_id = c.id";

    private async Task<string?> WhereAsync(ClosedCasesFilter f, DynamicParameters p, CancellationToken ct)
    {
        var where = new StringBuilder("c.lifecycle_status = 'CLOSED' AND ").Append(scope.Sql(p));
        if (f.ClosedFrom is { } from)
        {
            p.Add("f_closed_from", TiraneTime.StartOfDayUtc(from));
            where.Append(" AND c.closed_at >= @f_closed_from");
        }

        if (f.ClosedTo is { } to)
        {
            p.Add("f_closed_to", TiraneTime.StartOfDayUtc(to.AddDays(1)));
            where.Append(" AND c.closed_at < @f_closed_to");
        }

        if (!string.IsNullOrWhiteSpace(f.ResultCode))
        {
            p.Add("f_result", f.ResultCode.Trim());
            where.Append(" AND d.result_id = (SELECT id FROM decision_results WHERE code = @f_result)");
        }

        if (!string.IsNullOrWhiteSpace(f.OutcomeGroup))
        {
            p.Add("f_outcome", f.OutcomeGroup.Trim());
            where.Append(" AND d.result_id IN (SELECT id FROM decision_results WHERE outcome_group = @f_outcome)");
        }

        if (f.CourtId is { } courtId)
        {
            p.Add("f_court", courtId);
            where.Append(" AND c.court_id = @f_court");
        }

        if (f.OfficeId is { } officePublicId)
        {
            if (await offices.GetIdAsync(officePublicId, ct) is not { } officeId)
            {
                return null;
            }

            p.Add("f_office", officeId);
            where.Append(" AND c.office_id = @f_office");
        }

        if (f.LawyerId is { } lawyerPublicId)
        {
            var ids = await users.GetIdsAsync([lawyerPublicId], ct);
            if (!ids.TryGetValue(lawyerPublicId, out var lawyerId))
            {
                return null;
            }

            // the lawyer of the report's column: the primary at closing
            p.Add("f_lawyer", lawyerId);
            where.Append($" AND EXISTS (SELECT 1 FROM case_assignments a WHERE a.case_id = c.id AND a.user_id = @f_lawyer AND {PrimaryAtClosing})");
        }

        return where.ToString();
    }

    public async Task<Result<ReportPage<ClosedCaseRow>>> PageAsync(ClosedCasesFilter f, string? after, int? limit, CancellationToken ct)
    {
        if (scope.Denied() is { } denied)
        {
            return denied;
        }

        if (f.ClosedFrom is { } from && f.ClosedTo is { } to && from > to)
        {
            return Error.Validation("report.dates_invalid", "Data \"nga\" duhet të jetë para datës \"deri\".");
        }

        var take = ReportPaging.Take(limit);
        var p = new DynamicParameters();
        var where = await WhereAsync(f, p, ct);
        if (where is null)
        {
            return new ReportPage<ClosedCaseRow>([], null, 0, ReportColumns.Closed);
        }

        await using var connection = await db.OpenAsync(ct);
        var total = await db.ScalarAsync<int>(connection, $"SELECT COUNT(*) FROM {From} WHERE {where}", p, false, ct);
        var keys = await KeysAsync(connection, where, p, after, take + 1, false, ct);
        var hasMore = keys.Count > take;
        keys = keys.Take(take).ToList();
        var rows = await RowsAsync(connection, keys.Select(k => k.Id).ToList(), false, ct);
        return new ReportPage<ClosedCaseRow>(rows, hasMore ? Encode(keys[^1]) : null, total, ReportColumns.Closed);
    }

    public async Task<int> CountAsync(ClosedCasesFilter f, CancellationToken ct)
    {
        var p = new DynamicParameters();
        var where = await WhereAsync(f, p, ct);
        if (where is null)
        {
            return 0;
        }

        await using var connection = await db.OpenAsync(ct);
        return await db.ScalarAsync<int>(connection, $"SELECT COUNT(*) FROM {From} WHERE {where}", p, true, ct);
    }

    public async IAsyncEnumerable<ClosedCaseRow> AllAsync(ClosedCasesFilter f, [EnumeratorCancellation] CancellationToken ct)
    {
        var p = new DynamicParameters();
        var where = await WhereAsync(f, p, ct);
        if (where is null)
        {
            yield break;
        }

        await using var connection = await db.OpenAsync(ct);
        string? after = null;
        while (true)
        {
            var keys = await KeysAsync(connection, where, p, after, ReportPaging.ExportBatch, true, ct);
            if (keys.Count == 0)
            {
                yield break;
            }

            foreach (var row in await RowsAsync(connection, keys.Select(k => k.Id).ToList(), true, ct))
            {
                yield return row;
            }

            after = Encode(keys[^1]);
        }
    }

    public async Task<IReadOnlyList<ReportFilterText>> DescribeAsync(ClosedCasesFilter f, CancellationToken ct)
    {
        var texts = new List<ReportFilterText>();
        await using var connection = await db.OpenAsync(ct);
        if (f.ClosedFrom is { } from)
        {
            texts.Add(new("Mbyllur nga", from.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)));
        }

        if (f.ClosedTo is { } to)
        {
            texts.Add(new("Mbyllur deri", to.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)));
        }

        if (!string.IsNullOrWhiteSpace(f.ResultCode))
        {
            texts.Add(new("Rezultati", await ReportNames.LookupAsync(db, connection, "decision_results", "code", f.ResultCode.Trim(), ct)));
        }

        if (!string.IsNullOrWhiteSpace(f.OutcomeGroup))
        {
            texts.Add(new("Grupi i rezultatit", OutcomeGroupName(f.OutcomeGroup.Trim())));
        }

        if (f.CourtId is { } court)
        {
            texts.Add(new("Gjykata", await ReportNames.LookupAsync(db, connection, "courts", "id", court, ct)));
        }

        if (f.LawyerId is { } lawyer)
        {
            texts.Add(new("Avokati", await ReportNames.UserAsync(users, lawyer, ct)));
        }

        if (f.OfficeId is { } office)
        {
            texts.Add(new("Zyra", await ReportNames.OfficeAsync(db, connection, office, ct)));
        }

        return texts;
    }

    public static string OutcomeGroupName(string group) => group switch
    {
        "WIN" => "Fituar",
        "LOSS" => "Humbur",
        "PARTIAL" => "Pjesërisht",
        "PROCEDURAL" => "Procedurale",
        "OTHER" => "Tjetër",
        _ => group,
    };

    private sealed record Key(ulong Id, ulong DossierId, ushort LevelSeq);

    private static string Encode(Key k) => Cursor.Encode(
        $"{k.DossierId.ToString(CultureInfo.InvariantCulture)}:{k.LevelSeq.ToString(CultureInfo.InvariantCulture)}", (long)k.Id);

    private async Task<List<Key>> KeysAsync(
        MySqlConnection connection, string where, DynamicParameters p, string? after, int take, bool export, CancellationToken ct)
    {
        var keyset = string.Empty;
        if (Cursor.TryDecode(after, out var sortKey, out _) && sortKey.Split(':') is [var dossier, var level]
            && ulong.TryParse(dossier, CultureInfo.InvariantCulture, out var dossierId)
            && ushort.TryParse(level, CultureInfo.InvariantCulture, out var levelSeq))
        {
            p.Add("after_dossier", dossierId);
            p.Add("after_level", levelSeq);
            keyset = " AND (c.dossier_id > @after_dossier OR (c.dossier_id = @after_dossier AND c.level_seq > @after_level))";
        }

        p.Add("take", take);
        return (await db.QueryAsync<KeyDb>(connection,
                $"SELECT c.id AS Id, c.dossier_id AS DossierId, c.level_seq AS LevelSeq FROM {From} WHERE {where}{keyset} ORDER BY c.dossier_id, c.level_seq LIMIT @take",
                p, export, ct))
            .Select(k => new Key(k.Id, k.DossierId, k.LevelSeq))
            .ToList();
    }

    private async Task<IReadOnlyList<ClosedCaseRow>> RowsAsync(MySqlConnection connection, IReadOnlyList<ulong> ids, bool export, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var p = new { ids };
        var rows = await db.QueryAsync<ClosedDb>(connection, """
            SELECT c.id AS Id, c.public_id AS PublicId, dos.public_id AS DossierPublicId, c.case_code AS CaseCode, c.act_number AS ActNumber,
                   cl.name AS CourtLevel, ct.name AS Court, CONCAT(j.first_name, ' ', j.last_name) AS Judge, c.object_text AS ObjectText,
                   dr.name AS Result, d.result_other_text AS ResultOtherText, d.decision_number AS DecisionNumber,
                   d.decision_date AS DecisionDate, d.dispositive AS Dispositive, d.currency AS Currency,
                   d.won_amount AS WonAmount, d.won_area_m2 AS WonAreaM2, d.lost_amount AS LostAmount, d.lost_area_m2 AS LostAreaM2,
                   d.prevented_damage_amount AS PreventedDamageAmount, d.prevented_damage_area_m2 AS PreventedDamageAreaM2,
                   d.caused_damage_amount AS CausedDamageAmount, d.caused_damage_area_m2 AS CausedDamageAreaM2,
                   d.court_costs_amount AS CourtCostsAmount, c.has_security_measure AS HasSecurityMeasure,
                   d.is_appealed AS IsAppealed, d.is_recursed AS IsRecursed, d.is_final AS IsFinal,
                   d.decision_received AS DecisionReceived, c.closed_at AS ClosedAt
              FROM cases c
              JOIN dossiers dos         ON dos.id = c.dossier_id
              JOIN court_levels cl      ON cl.id = c.court_level_id
              JOIN courts ct            ON ct.id = c.court_id
              LEFT JOIN judges j        ON j.id = c.judge_id
              JOIN case_decisions d     ON d.case_id = c.id
              JOIN decision_results dr  ON dr.id = d.result_id
             WHERE c.id IN @ids
             ORDER BY c.dossier_id, c.level_seq
            """, p, export, ct);
        var parties = await ReportParties.ForAsync(db, connection, ids, export, ct);
        var lawyers = (await db.QueryAsync<LawyerDb>(connection, $"""
            SELECT a.case_id AS CaseId, CONCAT(u.first_name, ' ', u.last_name) AS Name
              FROM case_assignments a
              JOIN cases c ON c.id = a.case_id
              JOIN users u ON u.id = a.user_id
             WHERE a.case_id IN @ids AND {PrimaryAtClosing}
             ORDER BY a.assigned_at DESC
            """, p, export, ct))
            .GroupBy(l => l.CaseId)
            .ToDictionary(g => g.Key, g => g.First().Name);

        return rows.Select(r =>
        {
            var party = parties.GetValueOrDefault(r.Id);
            return new ClosedCaseRow(
                Ash.SharedKernel.Ids.PublicId.FromBytes(r.PublicId), Ash.SharedKernel.Ids.PublicId.FromBytes(r.DossierPublicId),
                r.CaseCode, r.ActNumber, r.CourtLevel, r.Court, r.Judge, party?.Plaintiffs, party?.Defendants, r.ObjectText,
                lawyers.GetValueOrDefault(r.Id), r.Result, r.ResultOtherText, r.DecisionNumber,
                r.DecisionDate is { } date ? DateOnly.FromDateTime(date) : null, r.Dispositive, r.Currency,
                r.WonAmount, r.WonAreaM2, r.LostAmount, r.LostAreaM2, r.PreventedDamageAmount, r.PreventedDamageAreaM2,
                r.CausedDamageAmount, r.CausedDamageAreaM2, r.CourtCostsAmount, r.HasSecurityMeasure,
                r.IsAppealed, r.IsRecursed, r.IsFinal, r.DecisionReceived, ActiveCasesReport.Utc(r.ClosedAt)!.Value);
        }).ToList();
    }

    private sealed class KeyDb
    {
        public ulong Id { get; set; }
        public ulong DossierId { get; set; }
        public ushort LevelSeq { get; set; }
    }

    private sealed class LawyerDb
    {
        public ulong CaseId { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class ClosedDb
    {
        public ulong Id { get; set; }
        public byte[] PublicId { get; set; } = [];
        public byte[] DossierPublicId { get; set; } = [];
        public string CaseCode { get; set; } = string.Empty;
        public string ActNumber { get; set; } = string.Empty;
        public string CourtLevel { get; set; } = string.Empty;
        public string Court { get; set; } = string.Empty;
        public string? Judge { get; set; }
        public string? ObjectText { get; set; }
        public string Result { get; set; } = string.Empty;
        public string? ResultOtherText { get; set; }
        public string? DecisionNumber { get; set; }
        public DateTime? DecisionDate { get; set; }
        public string? Dispositive { get; set; }
        public string? Currency { get; set; }
        public decimal? WonAmount { get; set; }
        public decimal? WonAreaM2 { get; set; }
        public decimal? LostAmount { get; set; }
        public decimal? LostAreaM2 { get; set; }
        public decimal? PreventedDamageAmount { get; set; }
        public decimal? PreventedDamageAreaM2 { get; set; }
        public decimal? CausedDamageAmount { get; set; }
        public decimal? CausedDamageAreaM2 { get; set; }
        public decimal? CourtCostsAmount { get; set; }
        public bool HasSecurityMeasure { get; set; }
        public bool IsAppealed { get; set; }
        public bool IsRecursed { get; set; }
        public bool IsFinal { get; set; }
        public bool DecisionReceived { get; set; }
        public DateTime? ClosedAt { get; set; }
    }
}
