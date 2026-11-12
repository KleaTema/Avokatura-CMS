using System.Runtime.CompilerServices;
using System.Text;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Reports.Infrastructure;
using Ash.SharedKernel.Paging;
using Ash.SharedKernel.Results;
using Dapper;
using MySql.Data.MySqlClient;

namespace Ash.Modules.Reports.Application.ActiveCases;

/// <summary>Filters of GET /api/reports/active-cases (public ids, codes; Tirane dates none here).</summary>
public sealed record ActiveCasesFilter(
    Guid? OfficeId,
    Guid? LawyerId,
    int? CourtId,
    string? CategoryCode,
    string? CourtLevelCode,
    string? Priority,
    bool? Suspended,
    string? Q);

/// <summary>Tabela 1, one row per open case (REGISTERED or ACTIVE). Public ids only.</summary>
public sealed record ActiveCaseRow(
    Guid CasePublicId,
    string CaseCode,
    string ActNumber,
    string? Plaintiffs,
    string? Defendants,
    string? ObjectText,
    string Court,
    string? Judge,
    DateTime? NextHearingAt,
    string Lawyers,
    string? LastHearingDescription,
    bool HasSecurityMeasure,
    decimal? ClaimAmount,
    string? ClaimCurrency,
    decimal? ClaimAreaM2,
    string? Category,
    string? Subcategory,
    string Priority,
    bool IsSuspended) : IReportRow
{
    public object? Get(string key) => key switch
    {
        "caseCode" => CaseCode,
        "actNumber" => ActNumber,
        "plaintiffs" => Plaintiffs,
        "defendants" => Defendants,
        "objectText" => ObjectText,
        "court" => Court,
        "judge" => Judge,
        "nextHearingAt" => NextHearingAt,
        "lawyers" => Lawyers,
        "lastHearingDescription" => LastHearingDescription,
        "hasSecurityMeasure" => HasSecurityMeasure,
        "claimAmount" => ClaimAmount,
        "claimCurrency" => ClaimCurrency,
        "claimAreaM2" => ClaimAreaM2,
        "category" => Category,
        "subcategory" => Subcategory,
        "priority" => Priority,
        "isSuspended" => IsSuspended,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };
}

/// <summary>
/// Tabela 1 (task 042), written for the API instead of filtering v_report_active_cases: that view has subqueries in its
/// SELECT list, so MySQL cannot merge it and materializes the WHOLE view for every query. Here: (1) the page's case ids —
/// scope and filters first, on indexed columns of cases, keyset on id; (2) the page's columns by joins on those ids;
/// (3) the parties and (4) the current lawyers each by ONE grouped query for the page's ids (never per row). The
/// columns and values equal the view's for the same cases (ReportTests compares them); the view stays for direct use.
/// </summary>
public sealed class ActiveCasesReport(ReportingDb db, ReportScope scope, ICaseSearchSql search, IOfficeDirectory offices, IUserDirectory users)
{
    private const string LawyerRolesSql = "('PRIMARY_LAWYER', 'SECONDARY_LAWYER')";

    /// <summary>The WHERE over alias c; null when nothing can match (unknown office or lawyer, a text with no word).</summary>
    private async Task<string?> WhereAsync(ActiveCasesFilter f, DynamicParameters p, CancellationToken ct)
    {
        var where = new StringBuilder("c.lifecycle_status <> 'CLOSED' AND ").Append(scope.Sql(p));
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

            // the current primary or secondary (as the case list's lawyer filter)
            p.Add("f_lawyer", lawyerId);
            where.Append($" AND EXISTS (SELECT 1 FROM case_assignments fa WHERE fa.case_id = c.id AND fa.user_id = @f_lawyer AND fa.ended_at IS NULL AND fa.case_role IN {LawyerRolesSql})");
        }

        if (f.CourtId is { } courtId)
        {
            p.Add("f_court", courtId);
            where.Append(" AND c.court_id = @f_court");
        }

        if (!string.IsNullOrWhiteSpace(f.CategoryCode))
        {
            p.Add("f_category", f.CategoryCode.Trim());
            where.Append(" AND c.category_id = (SELECT id FROM case_categories WHERE code = @f_category)");
        }

        if (!string.IsNullOrWhiteSpace(f.CourtLevelCode))
        {
            p.Add("f_level", f.CourtLevelCode.Trim());
            where.Append(" AND c.court_level_id = (SELECT id FROM court_levels WHERE code = @f_level)");
        }

        if (!string.IsNullOrWhiteSpace(f.Priority))
        {
            p.Add("f_priority", f.Priority.Trim());
            where.Append(" AND c.priority = @f_priority");
        }

        if (f.Suspended is { } suspended)
        {
            p.Add("f_suspended", suspended);
            where.Append(" AND c.is_suspended = @f_suspended");
        }

        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            // the shared case search (task 047): the same cases as the list and the header search for this text
            if (await search.ResolveAsync(f.Q, ct) is not { } terms)
            {
                return null;
            }

            p.Add("search_words", terms.Words);
            where.Append($" AND c.id IN ({terms.Sql})");
        }

        return where.ToString();
    }

    public async Task<Result<ReportPage<ActiveCaseRow>>> PageAsync(ActiveCasesFilter f, string? after, int? limit, CancellationToken ct)
    {
        if (scope.Denied() is { } denied)
        {
            return denied;
        }

        var take = ReportPaging.Take(limit);
        var p = new DynamicParameters();
        var where = await WhereAsync(f, p, ct);
        if (where is null)
        {
            return new ReportPage<ActiveCaseRow>([], null, 0, ReportColumns.Active);
        }

        await using var connection = await db.OpenAsync(ct);
        var total = await db.ScalarAsync<int>(connection, $"SELECT COUNT(*) FROM cases c WHERE {where}", p, false, ct);
        var afterId = Cursor.TryDecode(after, out _, out var id) ? id : 0;
        p.Add("after_id", afterId);
        p.Add("take", take + 1);
        var ids = (await db.QueryAsync<ulong>(connection,
            $"SELECT c.id FROM cases c WHERE {where} AND c.id > @after_id ORDER BY c.id LIMIT @take", p, false, ct)).ToList();
        var hasMore = ids.Count > take;
        ids = ids.Take(take).ToList();
        var rows = await RowsAsync(connection, ids, false, ct);
        return new ReportPage<ActiveCaseRow>(rows, hasMore ? Cursor.Encode("a", (long)ids[^1]) : null, total, ReportColumns.Active);
    }

    /// <summary>For the export: the number of rows, or null when nothing can match.</summary>
    public async Task<int> CountAsync(ActiveCasesFilter f, CancellationToken ct)
    {
        var p = new DynamicParameters();
        var where = await WhereAsync(f, p, ct);
        if (where is null)
        {
            return 0;
        }

        await using var connection = await db.OpenAsync(ct);
        return await db.ScalarAsync<int>(connection, $"SELECT COUNT(*) FROM cases c WHERE {where}", p, true, ct);
    }

    /// <summary>For the export: every row, in batches (keyset), so memory does not grow with the total.</summary>
    public async IAsyncEnumerable<ActiveCaseRow> AllAsync(ActiveCasesFilter f, [EnumeratorCancellation] CancellationToken ct)
    {
        var p = new DynamicParameters();
        var where = await WhereAsync(f, p, ct);
        if (where is null)
        {
            yield break;
        }

        await using var connection = await db.OpenAsync(ct);
        ulong after = 0;
        p.Add("take", ReportPaging.ExportBatch);
        while (true)
        {
            p.Add("after_id", after);
            var ids = (await db.QueryAsync<ulong>(connection,
                $"SELECT c.id FROM cases c WHERE {where} AND c.id > @after_id ORDER BY c.id LIMIT @take", p, true, ct)).ToList();
            if (ids.Count == 0)
            {
                yield break;
            }

            foreach (var row in await RowsAsync(connection, ids, true, ct))
            {
                yield return row;
            }

            after = ids[^1];
        }
    }

    public async Task<IReadOnlyList<ReportFilterText>> DescribeAsync(ActiveCasesFilter f, CancellationToken ct)
    {
        var texts = new List<ReportFilterText>();
        await using var connection = await db.OpenAsync(ct);
        if (f.OfficeId is { } office)
        {
            texts.Add(new("Zyra", await ReportNames.OfficeAsync(db, connection, office, ct)));
        }

        if (f.LawyerId is { } lawyer)
        {
            texts.Add(new("Avokati", await ReportNames.UserAsync(users, lawyer, ct)));
        }

        if (f.CourtId is { } court)
        {
            texts.Add(new("Gjykata", await ReportNames.LookupAsync(db, connection, "courts", "id", court, ct)));
        }

        if (!string.IsNullOrWhiteSpace(f.CategoryCode))
        {
            texts.Add(new("Kategoria", await ReportNames.LookupAsync(db, connection, "case_categories", "code", f.CategoryCode.Trim(), ct)));
        }

        if (!string.IsNullOrWhiteSpace(f.CourtLevelCode))
        {
            texts.Add(new("Shkalla", await ReportNames.LookupAsync(db, connection, "court_levels", "code", f.CourtLevelCode.Trim(), ct)));
        }

        if (!string.IsNullOrWhiteSpace(f.Priority))
        {
            texts.Add(new("Prioriteti", f.Priority.Trim()));
        }

        if (f.Suspended is { } suspended)
        {
            texts.Add(new("Pezulluar", suspended ? ReportColumns.Yes : ReportColumns.No));
        }

        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            texts.Add(new("Kërkimi", f.Q.Trim()));
        }

        return texts;
    }

    /// <summary>The page's columns: joins on the ids, then the parties and the lawyers by one grouped query each.</summary>
    private async Task<IReadOnlyList<ActiveCaseRow>> RowsAsync(MySqlConnection connection, IReadOnlyList<ulong> ids, bool export, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var p = new { ids };
        var cases = await db.QueryAsync<ActiveCaseDb>(connection, """
            SELECT c.id AS Id, c.public_id AS PublicId, c.case_code AS CaseCode, c.act_number AS ActNumber, c.object_text AS ObjectText,
                   ct.name AS Court, CONCAT(j.first_name, ' ', j.last_name) AS Judge, c.next_hearing_at AS NextHearingAt,
                   h.description AS LastHearingDescription, c.has_security_measure AS HasSecurityMeasure,
                   c.claim_amount AS ClaimAmount, c.claim_currency AS ClaimCurrency, c.claim_area_m2 AS ClaimAreaM2,
                   cat.name AS Category, sub.name AS Subcategory, c.priority AS Priority, c.is_suspended AS IsSuspended
              FROM cases c
              JOIN courts ct              ON ct.id = c.court_id
              LEFT JOIN judges j          ON j.id = c.judge_id
              LEFT JOIN case_hearings h   ON h.id = c.last_hearing_id
              LEFT JOIN case_categories cat    ON cat.id = c.category_id
              LEFT JOIN case_subcategories sub ON sub.id = c.subcategory_id
             WHERE c.id IN @ids
             ORDER BY c.id
            """, p, export, ct);
        var parties = (await ReportParties.ForAsync(db, connection, ids, export, ct));
        var lawyers = (await db.QueryAsync<LawyerDb>(connection, $"""
            SELECT a.case_id AS CaseId, a.case_role AS Role, CONCAT(u.first_name, ' ', u.last_name) AS Name
              FROM case_assignments a JOIN users u ON u.id = a.user_id
             WHERE a.case_id IN @ids AND a.ended_at IS NULL AND a.case_role IN {LawyerRolesSql}
             ORDER BY a.case_id, a.id
            """, p, export, ct)).ToLookup(l => l.CaseId);

        return cases.Select(c =>
        {
            var party = parties.GetValueOrDefault(c.Id);
            var team = lawyers[c.Id].ToList();
            // as the view: CONCAT_WS(' / ', primary, secondary) — empty text when neither
            var names = new[] { team.FirstOrDefault(l => l.Role == "PRIMARY_LAWYER")?.Name, team.FirstOrDefault(l => l.Role == "SECONDARY_LAWYER")?.Name };
            return new ActiveCaseRow(
                Ash.SharedKernel.Ids.PublicId.FromBytes(c.PublicId), c.CaseCode, c.ActNumber, party?.Plaintiffs, party?.Defendants,
                c.ObjectText, c.Court, c.Judge, Utc(c.NextHearingAt), string.Join(" / ", names.Where(n => n is not null)),
                c.LastHearingDescription, c.HasSecurityMeasure, c.ClaimAmount, c.ClaimCurrency, c.ClaimAreaM2,
                c.Category, c.Subcategory, c.Priority, c.IsSuspended);
        }).ToList();
    }

    internal static DateTime? Utc(DateTime? value) => value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;

    private sealed class ActiveCaseDb
    {
        public ulong Id { get; set; }
        public byte[] PublicId { get; set; } = [];
        public string CaseCode { get; set; } = string.Empty;
        public string ActNumber { get; set; } = string.Empty;
        public string? ObjectText { get; set; }
        public string Court { get; set; } = string.Empty;
        public string? Judge { get; set; }
        public DateTime? NextHearingAt { get; set; }
        public string? LastHearingDescription { get; set; }
        public bool HasSecurityMeasure { get; set; }
        public decimal? ClaimAmount { get; set; }
        public string? ClaimCurrency { get; set; }
        public decimal? ClaimAreaM2 { get; set; }
        public string? Category { get; set; }
        public string? Subcategory { get; set; }
        public string Priority { get; set; } = string.Empty;
        public bool IsSuspended { get; set; }
    }

    private sealed class LawyerDb
    {
        public ulong CaseId { get; set; }
        public string Role { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}
