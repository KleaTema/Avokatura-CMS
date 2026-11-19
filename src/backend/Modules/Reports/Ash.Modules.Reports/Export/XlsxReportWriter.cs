using Ash.Infrastructure.Excel;
using Ash.Modules.Reports.Application;

namespace Ash.Modules.Reports.Export;

/// <summary>
/// One data sheet of a workbook: its name, columns and rows. Task 053o: Title (lines above the header), HeaderRows
/// (explicit merged header rows — the statistics), Totals (bold rows after the data).
/// </summary>
public sealed record ExportSheet(string Name, IReadOnlyList<ReportColumn> Columns, IAsyncEnumerable<IReportRow> Rows)
{
    public IReadOnlyList<string> Title { get; init; } = [];
    public IReadOnlyList<IReadOnlyList<XlsxHeaderCell>>? HeaderRows { get; init; }
    public IReadOnlyList<IReportRow> Totals { get; init; } = [];
}

/// <summary>A row of named values (the statistics' rows, whose columns depend on the currencies present). Total: bold.</summary>
public sealed record ValuesRow(IReadOnlyDictionary<string, object?> Values, bool Total = false) : IReportRow
{
    public object? Get(string key) => Values.GetValueOrDefault(key);
}

/// <summary>What the "Filtrat" sheet says about an export.</summary>
public sealed record ExportInfo(string ReportName, DateTime GeneratedAtUtc, string GeneratedBy, IReadOnlyList<ReportFilterText> Filters);

/// <summary>
/// The reports' .xlsx: THE styled template of every export (<see cref="StyledXlsx"/>, task 053o) — header band, merged
/// grouped headers, borders, number formats, frozen header, autofilter, totals rows, "Filtrat". This class only maps the
/// reports' columns and rows onto it.
/// </summary>
public static class XlsxReportWriter
{
    /// <summary>Excel keeps about 15 significant digits; a larger amount is written exactly, as text (<see cref="StyledXlsx"/>).</summary>
    public const int ExcelSignificantDigits = StyledXlsx.ExcelSignificantDigits;

    public static bool FitsExcelNumber(decimal value) => StyledXlsx.FitsExcelNumber(value);

    public static string ColumnName(int number) => StyledXlsx.ColumnName(number);

    /// <summary>Writes the workbook to <paramref name="output"/> (seekable); returns the row count.</summary>
    public static async Task<int> WriteAsync(
        Stream output, string sheetName, IReadOnlyList<ReportColumn> columns, IAsyncEnumerable<IReportRow> rows, ExportInfo info,
        CancellationToken ct, IReadOnlyList<IReportRow>? totals = null) =>
        (await WriteAsync(output, [new ExportSheet(sheetName, columns, rows) { Totals = totals ?? [] }], info, ct))[0];

    /// <summary>Several data sheets, then "Filtrat". Returns each data sheet's row count; "Filtrat" shows the first sheet's.</summary>
    public static Task<IReadOnlyList<int>> WriteAsync(Stream output, IReadOnlyList<ExportSheet> sheets, ExportInfo info, CancellationToken ct) =>
        StyledXlsx.WriteAsync(output, sheets.Select(s => new XlsxSheet(s.Name, s.Columns.Select(Column).ToList(), Rows(s.Rows, ct))
            {
                Title = s.Title,
                HeaderRows = s.HeaderRows,
                Totals = s.Totals.Select(t => (IXlsxRow)new Row(t)).ToList(),
            }).ToList(),
            new XlsxInfo(info.ReportName, info.GeneratedAtUtc, info.GeneratedBy, info.Filters.Select(f => (f.Label, f.Value)).ToList()), ct);

    public static XlsxColumn Column(ReportColumn c) => new(c.Key, c.Header, c.Kind switch
    {
        ReportColumnKind.LongText => XlsxKind.LongText,
        ReportColumnKind.Date => XlsxKind.Date,
        ReportColumnKind.DateTime => XlsxKind.DateTime,
        ReportColumnKind.Decimal => XlsxKind.Decimal,
        ReportColumnKind.Integer => XlsxKind.Integer,
        ReportColumnKind.Money => XlsxKind.Money,
        ReportColumnKind.YesNo => XlsxKind.YesNo,
        _ => XlsxKind.Text,
    }, c.Width)
    {
        Group = c.Group,
        CurrencyKey = c.CurrencyKey,
    };

    /// <summary>
    /// The totals rows of a report (task 053o): the first row says how many rows and carries the m² sums; each money
    /// column's sum goes on its currency's row (one row per currency, never summed across currencies).
    /// </summary>
    public static IReadOnlyList<IReportRow> TotalsRows(IReadOnlyList<ReportColumn> columns, ReportTotals totals)
    {
        var currencies = totals.Sums.Where(s => s.Currency is not null).Select(s => s.Currency!).Distinct().Order(StringComparer.Ordinal).ToList();
        var rowCount = Math.Max(1, currencies.Count);
        var rows = Enumerable.Range(0, rowCount).Select(_ => new Dictionary<string, object?>()).ToList();
        var first = columns.Count > 0 ? columns[0].Key : string.Empty;
        rows[0][first] = $"Gjithsej: {totals.Rows:N0} rreshta";
        foreach (var sum in totals.Sums)
        {
            if (sum.Currency is null)
            {
                rows[0][sum.Key] = sum.Value;
                continue;
            }

            var index = currencies.IndexOf(sum.Currency);
            rows[index][sum.Key] = sum.Value;
            // the money column's currency key carries the code for the cell's format
            foreach (var column in columns.Where(c => c.Key == sum.Key && c.CurrencyKey is not null))
            {
                rows[index][column.CurrencyKey!] = sum.Currency;
            }
        }

        return rows.Select(r => (IReportRow)new ValuesRow(r, true)).ToList();
    }

    private static async IAsyncEnumerable<IXlsxRow> Rows(IAsyncEnumerable<IReportRow> rows, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var row in rows.WithCancellation(ct))
        {
            yield return new Row(row);
        }
    }

    private sealed class Row(IReportRow row) : IXlsxRow
    {
        public object? Get(string key) => row.Get(key);

        public bool IsTotal => row is ValuesRow { Total: true };
    }
}
