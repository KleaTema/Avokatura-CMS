using System.Globalization;
using System.Text;
using Ash.Modules.Reports.Application;
using Ash.SharedKernel.Time;

namespace Ash.Modules.Reports.Export;

/// <summary>
/// Task 053h: the .csv export — UTF-8 WITH a BOM so Excel opens ë / ç correctly; ';' between fields and a decimal
/// comma, as Excel expects them with Albanian regional settings; dates dd.MM.yyyy (hearings with the time, Tirane);
/// yes / no as "Po" / "Jo". Only the header line and the rows (the filters are in the xlsx / pdf header and the audit).
/// Streamed row by row — memory stays flat whatever the size.
/// </summary>
public static class CsvReportWriter
{
    public const string ContentType = "text/csv; charset=utf-8";
    public const char Separator = ';';

    private static readonly CultureInfo Albanian = CultureInfo.GetCultureInfo("sq-AL");

    public static async Task<int> WriteAsync(
        Stream output, IReadOnlyList<ReportColumn> columns, IAsyncEnumerable<IReportRow> rows, CancellationToken ct)
    {
        await using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 65536, leaveOpen: true);
        await writer.WriteLineAsync(string.Join(Separator, columns.Select(c => Quote(c.Header))));
        var count = 0;
        await foreach (var row in rows.WithCancellation(ct))
        {
            await writer.WriteLineAsync(string.Join(Separator, columns.Select(c => Quote(Cell(c, row.Get(c.Key))))));
            count++;
        }

        await writer.FlushAsync(ct);
        return count;
    }

    /// <summary>The text of one cell (shared with the PDF writer).</summary>
    public static string Format(ReportColumn column, object? value) => value switch
    {
        null => string.Empty,
        bool b => b ? ReportColumns.Yes : ReportColumns.No,
        DateTime d when column.Kind == ReportColumnKind.DateTime => Tirane(d).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture),
        DateTime d => (d.Kind == DateTimeKind.Utc ? Tirane(d) : d).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
        decimal m => m.ToString("0.##", Albanian),
        int i => i.ToString(CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static DateTime Tirane(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TiraneTime.Zone);

    /// <summary>
    /// RFC 4180: a field with the separator, a quote or a line break goes in quotes, quotes doubled. Task 051 (CSV
    /// injection): a text that starts with = + - @ TAB or CR gets a leading single quote, so Excel shows it as text and
    /// never runs it as a formula (a party named =HYPERLINK(…)). Numbers are written by <see cref="Format"/> and never
    /// pass through here with such a start, except a negative amount — which stays a number (see <see cref="Cell"/>).
    /// </summary>
    private static string Quote(string text) =>
        text.IndexOfAny([Separator, '"', '\n', '\r']) >= 0 ? $"\"{text.Replace("\"", "\"\"")}\"" : text;

    private static readonly char[] FormulaStarts = ['=', '+', '-', '@', '\t', '\r'];

    /// <summary>The text of a cell as written to the file: formula-like TEXT neutralised, numbers untouched.</summary>
    public static string Cell(ReportColumn column, object? value)
    {
        var text = Format(column, value);
        var numeric = value is decimal or int or long or double;
        return !numeric && text.Length > 0 && FormulaStarts.Contains(text[0]) ? "'" + text : text;
    }
}
