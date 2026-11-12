using System.Globalization;
using System.Text;
using Ash.Modules.Reports.Application;
using Ash.SharedKernel.Time;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Ash.Modules.Reports.Export;

/// <summary>One data sheet of a workbook: its name, columns and rows.</summary>
public sealed record ExportSheet(string Name, IReadOnlyList<ReportColumn> Columns, IAsyncEnumerable<IReportRow> Rows);

/// <summary>A row of named values (the statistics' rows, whose columns depend on the currencies present).</summary>
public sealed record ValuesRow(IReadOnlyDictionary<string, object?> Values) : IReportRow
{
    public object? Get(string key) => Values.GetValueOrDefault(key);
}

/// <summary>What the "Filtrat" sheet says about an export.</summary>
public sealed record ExportInfo(string ReportName, DateTime GeneratedAtUtc, string GeneratedBy, IReadOnlyList<ReportFilterText> Filters);

/// <summary>
/// Writes a report as .xlsx with the Open XML SDK's SAX writer (<see cref="OpenXmlWriter"/>): one row at a time straight
/// into the package, so memory does not grow with the row count (ClosedXML builds the whole workbook in memory first).
/// Sheet 1 the data (frozen header row, autofilter, column widths, wrapped long text); sheet "Filtrat" the report name,
/// when and by whom, every filter and the row count.
/// <list type="bullet">
/// <item>All user text is an inline STRING cell — never a formula: a party called "=HYPERLINK(…)" stays text.</item>
/// <item>Dates are real Excel dates (serial numbers) in Europe/Tirane, shown dd.MM.yyyy (hearings dd.MM.yyyy HH:mm).</item>
/// <item>Money and m² are numbers with 2 decimals; the currency has its own column and nothing is totalled.</item>
/// </list>
/// </summary>
public static class XlsxReportWriter
{
    // cellXfs indexes of the stylesheet below
    private const uint StyleHeader = 1;
    private const uint StyleDate = 2;
    private const uint StyleDateTime = 3;
    private const uint StyleDecimal = 4;
    private const uint StyleWrap = 5;
    private const uint StyleText = 6;

    /// <summary>
    /// Excel keeps numbers as IEEE doubles: about 15 significant digits. A larger amount (DECIMAL(18,2) allows 18) written
    /// as a number would be silently rounded in the cell — a different amount than the case holds. Such a value is
    /// written as TEXT instead (exact, still readable), anything smaller as a real number.
    /// </summary>
    public const int ExcelSignificantDigits = 15;

    /// <summary>Writes the workbook to <paramref name="output"/> (seekable: the package format needs it); returns the row count.</summary>
    public static async Task<int> WriteAsync(
        Stream output, string sheetName, IReadOnlyList<ReportColumn> columns, IAsyncEnumerable<IReportRow> rows, ExportInfo info,
        CancellationToken ct) =>
        (await WriteAsync(output, [new ExportSheet(sheetName, columns, rows)], info, ct))[0];

    /// <summary>
    /// Several data sheets (the statistics, task 044: Tabela, Rezultati, Në dorë), then "Filtrat". Returns each data
    /// sheet's row count; "Filtrat" shows the first sheet's.
    /// </summary>
    public static async Task<IReadOnlyList<int>> WriteAsync(Stream output, IReadOnlyList<ExportSheet> sheets, ExportInfo info, CancellationToken ct)
    {
        using var document = SpreadsheetDocument.Create(output, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();
        var styles = workbookPart.AddNewPart<WorkbookStylesPart>();
        styles.Stylesheet = Stylesheet();
        styles.Stylesheet.Save();

        var written = new List<(WorksheetPart Part, ExportSheet Sheet, int Count)>();
        foreach (var sheet in sheets)
        {
            var part = workbookPart.AddNewPart<WorksheetPart>();
            written.Add((part, sheet, await WriteDataSheetAsync(part, sheet.Columns, sheet.Rows, ct)));
        }

        var count = written[0].Count;
        var filtersPart = workbookPart.AddNewPart<WorksheetPart>();
        WriteFilters(filtersPart, info, count);

        var sheetList = new Sheets(written.Select((w, i) =>
            new Sheet { Id = workbookPart.GetIdOfPart(w.Part), SheetId = (uint)(i + 1), Name = w.Sheet.Name }));
        sheetList.Append(new Sheet { Id = workbookPart.GetIdOfPart(filtersPart), SheetId = (uint)(written.Count + 1), Name = "Filtrat" });
        workbookPart.Workbook = new Workbook(
            sheetList,
            // Excel's own name for each sheet's autofilter range
            new DefinedNames(written.Select((w, i) =>
                new DefinedName($"'{w.Sheet.Name}'!$A$1:${ColumnName(w.Sheet.Columns.Count)}${w.Count + 1}")
                {
                    Name = "_xlnm._FilterDatabase", LocalSheetId = (uint)i, Hidden = true,
                })));
        workbookPart.Workbook.Save();
        return written.Select(w => w.Count).ToList();
    }

    private static async Task<int> WriteDataSheetAsync(
        WorksheetPart dataPart, IReadOnlyList<ReportColumn> columns, IAsyncEnumerable<IReportRow> rows, CancellationToken ct)
    {
        var count = 0;
        using (var writer = OpenXmlWriter.Create(dataPart))
        {
            writer.WriteStartElement(new Worksheet());
            // the header row stays on screen while scrolling
            writer.WriteElement(new SheetViews(new SheetView(
                new Pane { VerticalSplit = 1, TopLeftCell = "A2", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen },
                new Selection { Pane = PaneValues.BottomLeft, ActiveCell = "A2", SequenceOfReferences = new ListValue<StringValue> { InnerText = "A2" } })
            { WorkbookViewId = 0 }));
            writer.WriteElement(new Columns(columns.Select((c, i) => new Column
            {
                Min = (uint)(i + 1), Max = (uint)(i + 1), Width = c.Width, CustomWidth = true,
            })));
            writer.WriteStartElement(new SheetData());

            writer.WriteStartElement(new Row());
            foreach (var column in columns)
            {
                WriteText(writer, column.Header, StyleHeader);
            }

            writer.WriteEndElement();

            await foreach (var row in rows.WithCancellation(ct))
            {
                writer.WriteStartElement(new Row());
                foreach (var column in columns)
                {
                    WriteValue(writer, column, row.Get(column.Key));
                }

                writer.WriteEndElement();
                count++;
            }

            writer.WriteEndElement(); // SheetData
            writer.WriteElement(new AutoFilter { Reference = $"A1:{ColumnName(columns.Count)}{count + 1}" });
            writer.WriteEndElement(); // Worksheet
        }

        return count;
    }

    private static void WriteFilters(WorksheetPart filtersPart, ExportInfo info, int count)
    {
        using (var writer = OpenXmlWriter.Create(filtersPart))
        {
            writer.WriteStartElement(new Worksheet());
            writer.WriteElement(new Columns(
                new Column { Min = 1, Max = 1, Width = 26, CustomWidth = true },
                new Column { Min = 2, Max = 2, Width = 60, CustomWidth = true }));
            writer.WriteStartElement(new SheetData());
            Pair(writer, "Raporti", w => WriteText(w, info.ReportName, StyleText));
            Pair(writer, "Gjeneruar më", w => WriteDate(w, info.GeneratedAtUtc, StyleDateTime));
            Pair(writer, "Gjeneruar nga", w => WriteText(w, info.GeneratedBy, StyleText));
            Pair(writer, "Numri i rreshtave", w => w.WriteElement(new Cell { DataType = CellValues.Number, CellValue = new CellValue(count) }));
            writer.WriteElement(new Row());
            writer.WriteStartElement(new Row());
            WriteText(writer, "Filtrat", StyleHeader);
            WriteText(writer, string.Empty, StyleHeader);
            writer.WriteEndElement();
            if (info.Filters.Count == 0)
            {
                Pair(writer, "Pa filtra", w => WriteText(w, "Të gjitha çështjet që mund të shihni", StyleText));
            }

            foreach (var filter in info.Filters)
            {
                Pair(writer, filter.Label, w => WriteText(w, filter.Value, StyleText));
            }

            writer.WriteEndElement(); // SheetData
            writer.WriteEndElement(); // Worksheet
        }
    }

    private static void Pair(OpenXmlWriter writer, string label, Action<OpenXmlWriter> value)
    {
        writer.WriteStartElement(new Row());
        WriteText(writer, label, StyleHeader);
        value(writer);
        writer.WriteEndElement();
    }

    private static void WriteValue(OpenXmlWriter writer, ReportColumn column, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteElement(new Cell { StyleIndex = column.Kind == ReportColumnKind.LongText ? StyleWrap : StyleText });
                break;
            case bool flag:
                WriteText(writer, flag ? ReportColumns.Yes : ReportColumns.No, StyleText);
                break;
            case decimal number:
                WriteDecimal(writer, number);
                break;
            case int whole:
                // counts (the statistics): plain numbers
                writer.WriteElement(new Cell { DataType = CellValues.Number, CellValue = new CellValue(whole) });
                break;
            case DateTime at:
                WriteDate(writer, at, column.Kind == ReportColumnKind.DateTime ? StyleDateTime : StyleDate);
                break;
            case DateOnly date:
                writer.WriteElement(new Cell
                {
                    DataType = CellValues.Number, StyleIndex = StyleDate,
                    CellValue = new CellValue(date.ToDateTime(TimeOnly.MinValue).ToOADate()),
                });
                break;
            default:
                WriteText(writer, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
                    column.Kind == ReportColumnKind.LongText ? StyleWrap : StyleText);
                break;
        }
    }

    public static bool FitsExcelNumber(decimal value)
    {
        var digits = Math.Abs(value).ToString(CultureInfo.InvariantCulture);
        if (digits.Contains('.'))
        {
            digits = digits.TrimEnd('0').TrimEnd('.');
        }

        return digits.Replace(".", string.Empty).TrimStart('0').Length <= ExcelSignificantDigits;
    }

    private static void WriteDecimal(OpenXmlWriter writer, decimal value)
    {
        if (FitsExcelNumber(value))
        {
            writer.WriteElement(new Cell { DataType = CellValues.Number, StyleIndex = StyleDecimal, CellValue = new CellValue(value) });
        }
        else
        {
            // more digits than a double holds: exact as text (see ExcelSignificantDigits)
            WriteText(writer, value.ToString("0.00", CultureInfo.InvariantCulture), StyleText);
        }
    }

    /// <summary>A UTC instant as an Excel date in Tirane (Excel has no time zones: the serial is the local wall time).</summary>
    private static void WriteDate(OpenXmlWriter writer, DateTime utc, uint style)
    {
        var tirane = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TiraneTime.Zone);
        writer.WriteElement(new Cell { DataType = CellValues.Number, StyleIndex = style, CellValue = new CellValue(tirane.ToOADate()) });
    }

    /// <summary>An inline string cell (no shared-string table to keep in memory), never a formula.</summary>
    private static void WriteText(OpenXmlWriter writer, string text, uint style) =>
        writer.WriteElement(new Cell
        {
            DataType = CellValues.InlineString,
            StyleIndex = style,
            InlineString = new InlineString(new Text(XmlSafe(text)) { Space = SpaceProcessingModeValues.Preserve }),
        });

    /// <summary>XML 1.0 cannot carry most control characters (a pasted text may hold one): they are dropped, tabs and line breaks kept.</summary>
    private static string XmlSafe(string text)
    {
        if (!text.Any(ch => ch < 0x20 && ch is not ('\t' or '\n' or '\r')))
        {
            return text;
        }

        var safe = new StringBuilder(text.Length);
        foreach (var ch in text.Where(ch => ch >= 0x20 || ch is '\t' or '\n' or '\r'))
        {
            safe.Append(ch);
        }

        return safe.ToString();
    }

    public static string ColumnName(int number)
    {
        var name = string.Empty;
        while (number > 0)
        {
            var rest = (number - 1) % 26;
            name = (char)('A' + rest) + name;
            number = (number - 1) / 26;
        }

        return name;
    }

    private static Stylesheet Stylesheet() => new(
        new NumberingFormats(
            new NumberingFormat { NumberFormatId = 164, FormatCode = "dd.mm.yyyy" },
            new NumberingFormat { NumberFormatId = 165, FormatCode = "dd.mm.yyyy hh:mm" })
        { Count = 2 },
        new Fonts(
            new Font(new FontSize { Val = 11 }, new FontName { Val = "Calibri" }),
            new Font(new Bold(), new FontSize { Val = 11 }, new FontName { Val = "Calibri" }))
        { Count = 2 },
        new Fills(
            new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
            new Fill(new PatternFill(new ForegroundColor { Rgb = "FFE8EEF6" }) { PatternType = PatternValues.Solid }))
        { Count = 3 },
        new Borders(new Border(new LeftBorder(), new RightBorder(), new TopBorder(), new BottomBorder(), new DiagonalBorder())) { Count = 1 },
        new CellStyleFormats(new CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0 }) { Count = 1 },
        new CellFormats(
            new CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0 },
            // 1 header: bold on a light fill, wrapped
            new CellFormat(new Alignment { WrapText = true, Vertical = VerticalAlignmentValues.Center })
            { NumberFormatId = 0, FontId = 1, FillId = 2, BorderId = 0, ApplyFont = true, ApplyFill = true, ApplyAlignment = true },
            // 2 date, 3 date + time
            new CellFormat(new Alignment { Vertical = VerticalAlignmentValues.Top })
            { NumberFormatId = 164, FontId = 0, FillId = 0, BorderId = 0, ApplyNumberFormat = true, ApplyAlignment = true },
            new CellFormat(new Alignment { Vertical = VerticalAlignmentValues.Top })
            { NumberFormatId = 165, FontId = 0, FillId = 0, BorderId = 0, ApplyNumberFormat = true, ApplyAlignment = true },
            // 4 money / m²: #,##0.00
            new CellFormat(new Alignment { Vertical = VerticalAlignmentValues.Top })
            { NumberFormatId = 4, FontId = 0, FillId = 0, BorderId = 0, ApplyNumberFormat = true, ApplyAlignment = true },
            // 5 long text, wrapped; 6 text, top-aligned; both "@" (text) so typing in the cell later stays text too
            new CellFormat(new Alignment { WrapText = true, Vertical = VerticalAlignmentValues.Top })
            { NumberFormatId = 49, FontId = 0, FillId = 0, BorderId = 0, ApplyNumberFormat = true, ApplyAlignment = true },
            new CellFormat(new Alignment { Vertical = VerticalAlignmentValues.Top })
            { NumberFormatId = 49, FontId = 0, FillId = 0, BorderId = 0, ApplyNumberFormat = true, ApplyAlignment = true })
        { Count = 7 },
        new CellStyles(new CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }) { Count = 1 });
}
