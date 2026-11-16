using Ash.Modules.Reports.Infrastructure;
using Ash.SharedKernel.Ids;
using Ash.SharedKernel.Results;
using Dapper;

namespace Ash.Modules.Reports.Application.Builder;

/// <summary>The builder's filters of Tabela 1 / 3 as SQL: "AND (…)" predicates over alias c (d) and their parameters.</summary>
public sealed record ReportCriteria(string Sql, DynamicParameters Parameters);

/// <summary>
/// Task 053h: the filter builder of Tabela 1 and 3 — the metadata (filters with their scoped options, columns) and the
/// criteria the reports, exports and saved reports run with. The options of a select are the values that exist among
/// the user's cases of the table (ReportAudience) under the choices of its ancestors (shkalla → gjykata → gjyqtari …).
/// </summary>
public sealed class CaseReportBuilder(ReportingDb db, ReportAudienceResolver audiences)
{
    public static Result<ReportCriteria> Criteria(int table, ReportFilterValues values)
    {
        var p = new DynamicParameters();
        var where = CaseReportFilters.Where(table, values, p);
        return where.IsFailure ? where.Error! : new ReportCriteria(where.Value, p);
    }

    public async Task<Result<ReportFiltersResponse>> MetaAsync(int table, ReportFilterValues values, CancellationToken ct)
    {
        var audience = await audiences.ResolveAsync(ct);
        if (audience.IsFailure)
        {
            return audience.Error!;
        }

        // the chosen values must be readable (an unknown key or a bad date → 400 before any option query)
        var check = Criteria(table, values);
        if (check.IsFailure)
        {
            return check.Error!;
        }

        var (from, @base) = CaseReportFilters.Base(table);
        var defs = CaseReportFilters.For(table);
        var filters = new List<ReportFilterMeta>(defs.Count);
        await using var connection = await db.OpenAsync(ct);
        foreach (var def in defs)
        {
            IReadOnlyList<ReportFilterOption>? options = null;
            if (def.Options is not null)
            {
                var p = new DynamicParameters();
                var scope = audience.Value.CasesSql(p);
                var ancestors = CaseReportFilters.Where(table, values, p, CaseReportFilters.Ancestors(table, def)).Value;
                var scoped = $"FROM {from} WHERE {@base} AND {scope}{ancestors}";
                var sql = def.Options(scoped).Replace("/*MINISTRY*/", MinistryLimit(def, values), StringComparison.Ordinal);
                var rows = await db.QueryAsync<OptionDb>(connection, sql, p, false, ct);
                options = rows
                    .Select(r => new ReportFilterOption(
                        r.Bytes is { Length: 16 } bytes ? PublicId.FromBytes(bytes).ToString() : r.Value ?? string.Empty,
                        def.Labels?.GetValueOrDefault(r.Label) ?? r.Label))
                    .ToList();
            }

            filters.Add(new ReportFilterMeta(def.Key, def.Label, def.Kind, def.Parent, def.IsDefault, false, false, options));
        }

        var columns = (table == ReportTables.Closed ? ReportColumns.Closed : ReportColumns.Active)
            .Select(c => new ReportColumnMeta(c.Key, c.Header, c.Kind.ToString(), c.Kind == ReportColumnKind.Decimal))
            .ToList();
        return new ReportFiltersResponse(table, ReportTables.Name(table), filters, columns, new Dictionary<string, string>());
    }

    /// <summary>
    /// Under a chosen ministry the institution list is the ministry and its direct agencies, not every other
    /// institution that happens to be a party of the same cases (the value is a parameter of the parent's predicate).
    /// </summary>
    private static string MinistryLimit(CaseFilterDef def, ReportFilterValues values) =>
        def.Key == "institution" && values.Text("ministry") is not null
            ? "AND (i.id = (SELECT im.id FROM institutions im WHERE im.public_id = @bf_ministry) " +
              "OR i.parent_institution_id = (SELECT im.id FROM institutions im WHERE im.public_id = @bf_ministry))"
            : string.Empty;

    private sealed class OptionDb
    {
        public string? Value { get; set; }
        public byte[]? Bytes { get; set; }
        public string Label { get; set; } = string.Empty;
    }
}
