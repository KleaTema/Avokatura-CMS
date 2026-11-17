using Ash.SharedKernel.Results;

namespace Ash.Modules.Reports.Contracts;

/// <summary>One choice of a select filter (a code, a reference id or a public id — never an internal id).</summary>
public sealed record CaseFilterOption(string Value, string Label);

/// <summary>
/// One filter of the case filter registry (task 053h) as another module shows it: Kind is select, text, dateRange,
/// numberRange or yesNo; Parent: the filter whose choice narrows this one's options; Options: for a select, the values
/// that exist (under the parent's choice).
/// </summary>
public sealed record CaseFilterField(string Key, string Label, string Kind, string? Parent, IReadOnlyList<CaseFilterOption>? Options);

/// <summary>The chosen filters as SQL: "AND (…)" predicates over alias c (cases) and d (LEFT JOIN case_decisions).</summary>
public sealed record CaseFilterSql(string Sql, IReadOnlyDictionary<string, object?> Parameters);

/// <summary>
/// The report builder's filter registry (task 053h, defined ONCE in the Reports module) offered to the "Vazhdo çështje
/// ekzistuese" lookup (task 053j, "+ Shto filtër"): every case column that the lookup's own filters (code, act number,
/// level, court, judge, parties, object, category and type, result) do not cover. The values arrive as the report
/// builder's JSON object ({"office":"…","isFinal":false,"decisionDate":{"from":"2026-01-01"}}).
/// </summary>
public interface ICaseFilterCatalog
{
    /// <summary>The filters, with the options that exist among the latest levels of every dossier of the country.</summary>
    Task<Result<IReadOnlyList<CaseFilterField>>> DescribeLookupAsync(string? valuesJson, CancellationToken ct = default);

    /// <summary>The chosen values as SQL; a validation error for an unknown filter or an unreadable value.</summary>
    Result<CaseFilterSql> BindLookup(string? valuesJson);
}
