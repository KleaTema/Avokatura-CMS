using Ash.Modules.Identity.Contracts;
using Ash.Modules.Reports.Infrastructure;
using Ash.SharedKernel.Ids;
using Ash.SharedKernel.Paging;
using Ash.SharedKernel.Results;
using Dapper;
using Microsoft.Extensions.Caching.Memory;
using MySql.Data.MySqlClient;

namespace Ash.Modules.Reports.Application.Statistics;

/// <summary>
/// Tabela 5 / 6 and the drill-down (task 044). Runs the fixed statements of <see cref="StatisticsSql"/> with bound
/// parameters only — this class builds no SQL text (StatisticsSqlSafetyTests checks it). A closed year with a snapshot
/// is read from it (frozen); a closed year without one is computed live with a warning; the current year is live and
/// cached 5 minutes per scope and filters.
/// </summary>
public sealed class StatisticsQueries(
    ReportingDb db, StatisticsScope scope, IUserDirectory users, IInstitutionDirectory institutions, IMemoryCache cache, CaseLinks links)
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);
    public const string NotFrozenWarning =
        "Jo e ngrirë: statistikat e këtij viti nuk janë ngrirë ende, ndaj llogariten nga të dhënat e sotme dhe mund të ndryshojnë.";

    private sealed record Snapshot(DateTime CreatedAt);

    public async Task<Result<StatisticsResponse>> GetAsync(StatisticsQuery q, CancellationToken ct, bool export = false)
    {
        var resolved = await scope.ResolveAsync(q, ct);
        if (resolved.IsFailure)
        {
            return resolved.Error!;
        }

        var f = resolved.Value;
        // task 051 (item 8): a Kryetar's answer depends on which cases he supervises NOW — not cached, so an ended
        // supervision leaves his numbers at once (the key holds his id, not the supervised set)
        var cacheable = f.IsCurrentYear && f.ScopeHeadId is null;
        if (cacheable && cache.TryGetValue(f.CacheKey, out StatisticsResponse? cached) && cached is not null)
        {
            return cached;
        }

        await using var connection = await db.OpenAsync(ct, export);
        var snapshot = await SnapshotOfAsync(connection, f, ct);
        var p = Parameters(f, snapshot is not null);

        var table = (await connection.QueryAsync(new CommandDefinition(StatisticsSql.Table, p, cancellationToken: ct)))
            .Cast<IDictionary<string, object?>>()
            .Select(row =>
            {
                var courtId = row["CourtId"] is null ? (int?)null : Convert.ToInt32(row["CourtId"]);
                var values = StatisticsMeasures.Counts.ToDictionary(m => m.Code, m => Convert.ToInt32(row[m.Code] ?? 0));
                return new StatisticsCourtRow(courtId, courtId is null ? StatisticsMeasures.TotalRow : (string)row["CourtName"]!, values);
            })
            .ToList();
        // the rollup's TOTAL row last; with no case at all, an empty TOTAL row
        if (table.Count == 0)
        {
            table.Add(new StatisticsCourtRow(null, StatisticsMeasures.TotalRow, StatisticsMeasures.Counts.ToDictionary(m => m.Code, _ => 0)));
        }

        var courts = table.Where(r => r.CourtId is not null).ToDictionary(r => r.CourtId!.Value, r => r.Court);
        var money = (await connection.QueryAsync<MoneyDb>(new CommandDefinition(StatisticsSql.Money, p, cancellationToken: ct)))
            .Where(m => !m.GroupedCurrency)
            .Select(m => new StatisticsMoneyRow(
                m.GroupedCourt ? null : m.CourtId,
                m.GroupedCourt ? StatisticsMeasures.TotalRow : courts.GetValueOrDefault(m.CourtId ?? 0, string.Empty),
                m.Currency, m.PreventedAmount, m.PreventedM2, m.CausedAmount, m.CausedM2))
            .ToList();
        var results = (await connection.QueryAsync<ResultDb>(new CommandDefinition(StatisticsSql.Results, p, cancellationToken: ct)))
            .Select(r => new StatisticsResultRow(r.ResultCode, r.ResultName, (int)r.Ash, (int)r.Deleguar))
            .ToList();
        var inHandRows = (await connection.QueryAsync<InHandDb>(new CommandDefinition(StatisticsSql.InHand, p, cancellationToken: ct))).ToList();
        var refs = inHandRows.Count == 0
            ? new Dictionary<long, UserRef>()
            : await users.GetRefsAsync(inHandRows.Select(r => r.LawyerId).ToList(), ct);
        var inHand = inHandRows
            .Where(r => refs.ContainsKey(r.LawyerId))
            .Select(r => new StatisticsLawyerRow(refs[r.LawyerId].PublicId, refs[r.LawyerId].Name, r.Count))
            .ToList();

        var response = new StatisticsResponse(
            f.Year, f.Table, snapshot is not null, snapshot?.CreatedAt,
            !f.IsCurrentYear && snapshot is null ? NotFrozenWarning : null,
            StatisticsMeasures.Counts, StatisticsMeasures.DamageMeasure, StatisticsMeasures.InHandMeasure,
            table, money, results, inHand, await DescribeAsync(connection, f, ct));
        if (cacheable)
        {
            cache.Set(f.CacheKey, response, CacheFor);
        }

        return response;
    }

    public async Task<Result<StatisticsCasesPage>> CasesAsync(StatisticsCasesQuery q, CancellationToken ct)
    {
        if (q.Measure is not { } measure || !StatisticsMeasures.DrillDownCodes.Contains(measure))
        {
            return Error.Validation("statistics.measure_invalid", "Masa e kërkuar nuk njihet.");
        }

        var resolved = await scope.ResolveAsync(q.Statistics, ct);
        if (resolved.IsFailure)
        {
            return resolved.Error!;
        }

        var f = resolved.Value;
        await using var connection = await db.OpenAsync(ct);
        var snapshot = await SnapshotOfAsync(connection, f, ct);
        var p = Parameters(f, snapshot is not null);
        p.Add("measure", measure);
        p.Add("court_id", q.CourtId);
        p.Add("currency", string.IsNullOrWhiteSpace(q.Currency) ? null : q.Currency.Trim());
        p.Add("result_id", string.IsNullOrWhiteSpace(q.ResultCode)
            ? null
            : await connection.ExecuteScalarAsync<byte?>(new CommandDefinition(
                "SELECT id FROM decision_results WHERE code = @code", new { code = q.ResultCode.Trim() }, cancellationToken: ct)) ?? byte.MaxValue);
        long? rowLawyer = null;
        if (q.RowLawyerId is { } rowLawyerPublic)
        {
            rowLawyer = (await users.GetIdsAsync([rowLawyerPublic], ct)).TryGetValue(rowLawyerPublic, out var id) ? id : -1;
        }

        p.Add("row_lawyer_id", rowLawyer);
        p.Add("after_id", 0UL);
        p.Add("take", int.MaxValue);
        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(StatisticsSql.CasesCount, p, cancellationToken: ct));

        var take = ReportPaging.Take(q.Limit);
        p.Add("after_id", Cursor.TryDecode(q.After, out _, out var after) ? (ulong)after : 0UL);
        p.Add("take", take + 1);
        var ids = (await connection.QueryAsync<ulong>(new CommandDefinition(StatisticsSql.Cases, p, cancellationToken: ct))).ToList();
        var hasMore = ids.Count > take;
        ids = ids.Take(take).ToList();

        var rows = ids.Count == 0
            ? []
            : (await connection.QueryAsync<CaseDb>(new CommandDefinition(StatisticsSql.CaseDetails, new { ids }, cancellationToken: ct))).ToList();
        var openable = await links.OpenableAsync(connection, ids, ct);
        var items = rows.Select(r => new StatisticsCaseRow(
                PublicId.FromBytes(r.PublicId), r.CaseCode, r.Court, r.Lawyer, r.Status,
                DateTime.SpecifyKind(r.RegisteredAt, DateTimeKind.Utc),
                r.ClosedAt is { } closed ? DateTime.SpecifyKind(closed, DateTimeKind.Utc) : null,
                openable.Contains(PublicId.FromBytes(r.PublicId))))
            .ToList();
        return new StatisticsCasesPage(items, hasMore ? Cursor.Encode("s", (long)ids[^1]) : null, total);
    }

    /// <summary>Every parameter of the statements, bound by name (the SQL never sees a value as text).</summary>
    private static DynamicParameters Parameters(StatisticsFilter f, bool useSnapshot)
    {
        var p = new DynamicParameters();
        p.Add("y_start", f.YearStartUtc);
        p.Add("y_end", f.YearEndUtc);
        p.Add("point", f.PointUtc);
        p.Add("year", f.Year);
        p.Add("use_snapshot", useSnapshot ? 1 : 0);
        p.Add("office_id", f.OfficeId);
        // task 053h: a set of lawyers (one, or an assistant's linked lawyers); Dapper expands the list
        p.Add("lawyer_all", f.Lawyers is null ? 1 : 0);
        p.Add("lawyer_ids", f.Lawyers ?? [0L]);
        p.Add("scope_inst", f.ScopeInstitutionId);
        p.Add("scope_inst_children", f.ScopeInstitutionChildren ? 1 : 0);
        p.Add("f_level", f.CourtLevelCode);
        p.Add("f_court", f.FilterCourtId);
        p.Add("institution_id", f.InstitutionId);
        p.Add("with_children", f.WithChildren ? 1 : 0);
        p.Add("scope_head", f.ScopeHeadId);
        p.Add("scope_office", f.ScopeOfficeId);
        return p;
    }

    /// <summary>A closed year's snapshot, if it was taken (the current year is always live).</summary>
    private static async Task<Snapshot?> SnapshotOfAsync(MySqlConnection connection, StatisticsFilter f, CancellationToken ct)
    {
        if (f.IsCurrentYear)
        {
            return null;
        }

        var createdAt = await connection.ExecuteScalarAsync<DateTime?>(new CommandDefinition(
            "SELECT created_at FROM statistics_snapshots WHERE year = @year", new { year = f.Year }, cancellationToken: ct));
        return createdAt is { } at ? new Snapshot(DateTime.SpecifyKind(at, DateTimeKind.Utc)) : null;
    }

    private async Task<IReadOnlyList<ReportFilterText>> DescribeAsync(MySqlConnection connection, StatisticsFilter f, CancellationToken ct)
    {
        var texts = new List<ReportFilterText>
        {
            new("Viti", f.Year.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("Tabela", f.Table == 5 ? "Tabela 5 — Statistika e çështjeve" : "Tabela 6 — Çështje institucionale"),
        };
        if (f.OfficeId is { } office)
        {
            texts.Add(new("Zyra", await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT name FROM offices WHERE id = @id", new { id = office }, cancellationToken: ct)) ?? string.Empty));
        }
        else if (f.ScopeOfficeId is { } scopeOffice)
        {
            // the Kryetar's whole scope (task 053d): the office, the supervised cases and their own
            var name = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT name FROM offices WHERE id = @id", new { id = scopeOffice }, cancellationToken: ct)) ?? string.Empty;
            texts.Add(new("Zyra", $"{name} + çështjet në mbikëqyrje dhe të miat"));
        }

        if (f.LawyerId is { } lawyer)
        {
            texts.Add(new("Avokati", (await users.GetNamesAsync([lawyer], ct)).GetValueOrDefault(lawyer) ?? string.Empty));
        }

        if (f.InstitutionId is { } institution)
        {
            var name = (await institutions.GetByIdsAsync([institution], ct)).FirstOrDefault()?.Name ?? string.Empty;
            texts.Add(new(f.WithChildren ? "Ministria (me agjencitë e saj)" : "Institucioni", name));
        }

        return texts;
    }

    private sealed class ResultDb
    {
        public string ResultCode { get; set; } = string.Empty;
        public string ResultName { get; set; } = string.Empty;
        public decimal Ash { get; set; }
        public decimal Deleguar { get; set; }
    }

    private sealed class MoneyDb
    {
        public int? CourtId { get; set; }
        public string? Currency { get; set; }
        public bool GroupedCurrency { get; set; }
        public bool GroupedCourt { get; set; }
        public decimal PreventedAmount { get; set; }
        public decimal PreventedM2 { get; set; }
        public decimal CausedAmount { get; set; }
        public decimal CausedM2 { get; set; }
    }

    private sealed class InHandDb
    {
        public long LawyerId { get; set; }
        public int Count { get; set; }
    }

    private sealed class CaseDb
    {
        public byte[] PublicId { get; set; } = [];
        public string CaseCode { get; set; } = string.Empty;
        public string Court { get; set; } = string.Empty;
        public string? Lawyer { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime RegisteredAt { get; set; }
        public DateTime? ClosedAt { get; set; }
    }
}
