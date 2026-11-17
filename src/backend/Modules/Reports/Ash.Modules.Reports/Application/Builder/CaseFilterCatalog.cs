using Ash.Modules.Reports.Contracts;
using Ash.Modules.Reports.Infrastructure;
using Ash.SharedKernel.Ids;
using Ash.SharedKernel.Results;
using Dapper;

namespace Ash.Modules.Reports.Application.Builder;

/// <summary>
/// <see cref="ICaseFilterCatalog"/> over <see cref="CaseReportFilters.Lookup"/> (task 053j): the same definitions,
/// predicates and option queries as the report builder (task 053h), here over the latest level of every dossier of the
/// country — the Protokoll's lookup is country-wide (the Cases module checks who may use it).
/// </summary>
internal sealed class CaseFilterCatalog(ReportingDb db) : ICaseFilterCatalog
{
    public async Task<Result<IReadOnlyList<CaseFilterField>>> DescribeLookupAsync(string? valuesJson, CancellationToken ct = default)
    {
        if (ReportFilterValues.Parse(valuesJson) is not { } values)
        {
            return Invalid();
        }

        var check = CaseReportFilters.Where(CaseReportFilters.Lookup, values, new DynamicParameters());
        if (check.IsFailure)
        {
            return check.Error!;
        }

        var fields = new List<CaseFilterField>(CaseReportFilters.Lookup.Count);
        await using var connection = await db.OpenAsync(ct);
        foreach (var def in CaseReportFilters.Lookup)
        {
            IReadOnlyList<CaseFilterOption>? options = null;
            if (def.Options is not null)
            {
                var p = new DynamicParameters();
                var ancestors = CaseReportFilters.Where(CaseReportFilters.Lookup, values, p,
                    CaseReportFilters.Ancestors(CaseReportFilters.Lookup, def)).Value;
                var sql = def.Options($"{CaseReportFilters.LookupFrom}{ancestors}")
                    .Replace("/*MINISTRY*/", CaseReportBuilder.MinistryLimit(def, values), StringComparison.Ordinal);
                var rows = await db.QueryAsync<CaseReportBuilder.OptionDb>(connection, sql, p, false, ct);
                options = rows
                    .Select(r => new CaseFilterOption(
                        r.Bytes is { Length: 16 } bytes ? PublicId.FromBytes(bytes).ToString() : r.Value ?? string.Empty,
                        def.Labels?.GetValueOrDefault(r.Label) ?? r.Label))
                    .ToList();
            }

            fields.Add(new CaseFilterField(def.Key, def.Label, def.Kind, def.Parent, options));
        }

        return fields;
    }

    public Result<CaseFilterSql> BindLookup(string? valuesJson)
    {
        if (ReportFilterValues.Parse(valuesJson) is not { } values)
        {
            return Invalid();
        }

        var p = new DynamicParameters();
        var where = CaseReportFilters.Where(CaseReportFilters.Lookup, values, p);
        if (where.IsFailure)
        {
            return where.Error!;
        }

        // Dapper's parameters as plain name → value (the Cases module binds them as MySqlParameter)
        var parameters = p.ParameterNames.ToDictionary(n => "@" + n, n => (object?)p.Get<object>(n));
        return new CaseFilterSql(where.Value, parameters);
    }

    private static Error Invalid() =>
        Error.Validation("report.filters_invalid", "Filtrat duhet të jenë një objekt JSON.");
}
