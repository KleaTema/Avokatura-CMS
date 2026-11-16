namespace Ash.Modules.Reports.Application;

/// <summary>A report row as the export reads it: the value of a column by its key (<see cref="ReportColumn.Key"/>).</summary>
public interface IReportRow
{
    object? Get(string key);
}

/// <summary>One JSON page of a report: rows, the opaque cursor of the next page, the total (same filters), the columns.</summary>
public sealed record ReportPage<T>(IReadOnlyList<T> Items, string? NextCursor, int Total, IReadOnlyList<ReportColumn> Columns);

/// <summary>One sum of the totals row: a numeric column, per currency for money (null for m²).</summary>
public sealed record ReportTotal(string Key, string? Currency, decimal Value);

/// <summary>Task 053h: the totals row of a report over every row of its filters.</summary>
public sealed record ReportTotals(int Rows, IReadOnlyList<ReportTotal> Sums);

/// <summary>A filter as the export's "Filtrat" sheet and the audit log show it: Albanian label, readable value.</summary>
public sealed record ReportFilterText(string Label, string Value);

public static class ReportPaging
{
    public const int DefaultLimit = 10;
    public const int MaxLimit = 100;
    /// <summary>The export reads the rows in batches of this many (memory stays flat whatever the total).</summary>
    public const int ExportBatch = 1000;

    public static int Take(int? limit) => Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
}
