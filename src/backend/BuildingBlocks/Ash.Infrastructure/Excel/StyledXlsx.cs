using System.Globalization;
using System.Text;
using Ash.SharedKernel.Time;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Ash.Infrastructure.Excel;

/// <summary>How a cell is written: text, a date, a date and time, a decimal (money / m²), a count, money in a currency, yes / no.</summary>
public enum XlsxKind
{
    Text,
    LongText,
    Date,
    DateTime,
    Decimal,
    Integer,
    /// <summary>A number with 2 decimals whose ISO currency comes from the row's <see cref="XlsxColumn.CurrencyKey"/>:
    /// the cell stays a number (it sums) and its format shows the code ("8.000,00 ALL").</summary>
    Money,
    YesNo,
}

/// <summary>
/// A column: Key (the row's value), Header, Kind, Width. Group: the header above it, merged over consecutive columns of
/// the same group (e.g. "Vlera e padisë" over M2 | Monedha); a column without a group spans both header rows.
/// </summary>
public sealed record XlsxColumn(string Key, string Header, XlsxKind Kind, double Width)
{
    public string? Group { get; init; }
    public string? CurrencyKey { get; init; }
}

/// <summary>An explicit header cell (the statistics' header rows): merged over ColSpan columns and RowSpan rows.</summary>
public sealed record XlsxHeaderCell(string Text, int ColSpan = 1, int RowSpan = 1);

/// <summary>A row: the value of a column by its key. IsTotal: written bold with a top border (a totals row).</summary>
public interface IXlsxRow
{
    object? Get(string key);

    bool IsTotal => false;
}

/// <summary>A row of named values.</summary>
public sealed record XlsxValuesRow(IReadOnlyDictionary<string, object?> Values, bool Total = false) : IXlsxRow
{
    public object? Get(string key) => Values.GetValueOrDefault(key);

    public bool IsTotal => Total;
}

/// <summary>
/// One data sheet. Title: lines above the header (merged over the table's width, bold). HeaderRows: explicit header
/// rows (else built from the columns' groups). Totals: rows written after the data, bold.
/// </summary>
public sealed record XlsxSheet(string Name, IReadOnlyList<XlsxColumn> Columns, IAsyncEnumerable<IXlsxRow> Rows)
{
    public IReadOnlyList<string> Title { get; init; } = [];
    public IReadOnlyList<IReadOnlyList<XlsxHeaderCell>>? HeaderRows { get; init; }
    public IReadOnlyList<IXlsxRow> Totals { get; init; } = [];
}

/// <summary>What the "Filtrat" sheet says: the report, when and by whom, the filters (label, value).</summary>
public sealed record XlsxInfo(string ReportName, DateTime GeneratedAtUtc, string GeneratedBy, IReadOnlyList<(string Label, string Value)> Filters);

/// <summary>
/// Task 053o — THE Excel template of every export (reports, statistics, import templates and results): written with
/// the Open XML SDK's SAX writer, one row at a time (memory does not grow with the rows).
/// <list type="bullet">
/// <item>Header row(s): bold white text on navy, borders, centered; grouped headers merged and centered over their columns.</item>
/// <item>Data: thin borders on every cell; dates dd.mm.yyyy (Tirane); numbers with a thousands separator, money and m²
/// with 2 decimals; money keeps its ISO code in the cell's format (never summed across currencies); yes / no as Po / Jo.</item>
/// <item>Totals rows bold with a top border; frozen header; autofilter; column widths; the "Filtrat" sheet.</item>
/// <item>User text is always an inline STRING cell — never a formula ("=HYPERLINK(…)" stays text); an amount with more
/// digits than a double keeps (15) is written exactly, as text.</item>
/// </list>
/// The stylesheet is written LAST: a style (e.g. a currency's format) is added the first time a cell needs it.
/// </summary>
public static class StyledXlsx
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const int ExcelSignificantDigits = 15;
    public const string Yes = "Po";
    public const string No = "Jo";

    /// <summary>The navy of the header band (the app's primary) and its text.</summary>
    public const string HeaderFill = "FF1F3A5F";
    public const string HeaderText = "FFFFFFFF";

    /// <summary>Writes the workbook (seekable stream); returns each data sheet's row count (totals not counted).</summary>
    public static async Task<IReadOnlyList<int>> WriteAsync(Stream output, IReadOnlyList<XlsxSheet> sheets, XlsxInfo? info, CancellationToken ct)
    {
        using var document = SpreadsheetDocument.Create(output, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();
        var styles = new StyleRegistry();

        var written = new List<(WorksheetPart Part, XlsxSheet Sheet, int Count, string FilterRange)>();
        foreach (var sheet in sheets)
        {
            var part = workbookPart.AddNewPart<WorksheetPart>();
            var (count, range) = await WriteDataSheetAsync(part, sheet, styles, ct);
            written.Add((part, sheet, count, range));
        }

        WorksheetPart? filtersPart = null;
        if (info is not null)
        {
            filtersPart = workbookPart.AddNewPart<WorksheetPart>();
            WriteFilters(filtersPart, info, written.Count > 0 ? written[0].Count : 0, styles);
        }

        var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
        stylesPart.Stylesheet = styles.Build();
        stylesPart.Stylesheet.Save();

        var sheetList = new Sheets(written.Select((w, i) =>
            new Sheet { Id = workbookPart.GetIdOfPart(w.Part), SheetId = (uint)(i + 1), Name = SheetName(w.Sheet.Name) }));
        if (filtersPart is not null)
        {
            sheetList.Append(new Sheet { Id = workbookPart.GetIdOfPart(filtersPart), SheetId = (uint)(written.Count + 1), Name = "Filtrat" });
        }

        workbookPart.Workbook = new Workbook(
            sheetList,
            // Excel's own name for each sheet's autofilter range
            new DefinedNames(written.Select((w, i) =>
                new DefinedName($"'{SheetName(w.Sheet.Name)}'!{w.FilterRange}") { Name = "_xlnm._FilterDatabase", LocalSheetId = (uint)i, Hidden = true })));
        workbookPart.Workbook.Save();
        return written.Select(w => w.Count).ToList();
    }

    /// <summary>Excel's sheet names: at most 31 characters, none of : \ / ? * [ ].</summary>
    public static string SheetName(string name)
    {
        var clean = new string(name.Where(ch => ch is not (':' or '\\' or '/' or '?' or '*' or '[' or ']')).ToArray());
        return clean.Length <= 31 ? clean : clean[..31];
    }

    /// <summary>The header rows: explicit, or from the columns' groups (one row when no column has a group).</summary>
    public static IReadOnlyList<IReadOnlyList<XlsxHeaderCell>> HeaderRowsOf(IReadOnlyList<XlsxColumn> columns)
    {
        if (columns.All(c => c.Group is null))
        {
            return [columns.Select(c => new XlsxHeaderCell(c.Header)).ToList()];
        }

        var top = new List<XlsxHeaderCell>();
        var bottom = new List<XlsxHeaderCell>();
        for (var i = 0; i < columns.Count;)
        {
            if (columns[i].Group is not { } group)
            {
                top.Add(new XlsxHeaderCell(columns[i].Header, 1, 2));
                i++;
                continue;
            }

            var span = 0;
            while (i + span < columns.Count && columns[i + span].Group == group)
            {
                bottom.Add(new XlsxHeaderCell(columns[i + span].Header));
                span++;
            }

            top.Add(new XlsxHeaderCell(group, span));
            i += span;
        }

        return [top, bottom];
    }

    private static async Task<(int Count, string FilterRange)> WriteDataSheetAsync(
        WorksheetPart part, XlsxSheet sheet, StyleRegistry styles, CancellationToken ct)
    {
        var columns = sheet.Columns;
        var width = Math.Max(1, columns.Count);
        var header = sheet.HeaderRows ?? HeaderRowsOf(columns);
        var merges = new List<string>();
        var rowIndex = 0;
        var count = 0;
        var lastColumn = ColumnName(width);

        using (var writer = OpenXmlWriter.Create(part))
        {
            writer.WriteStartElement(new Worksheet());
            var frozen = sheet.Title.Count + header.Count;
            var topLeft = $"A{frozen + 1}";
            writer.WriteElement(new SheetViews(new SheetView(
                new Pane { VerticalSplit = frozen, TopLeftCell = topLeft, ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen },
                new Selection { Pane = PaneValues.BottomLeft, ActiveCell = topLeft, SequenceOfReferences = new ListValue<StringValue> { InnerText = topLeft } })
            { WorkbookViewId = 0 }));
            writer.WriteElement(new Columns(columns.Select((c, i) => new Column
            {
                Min = (uint)(i + 1), Max = (uint)(i + 1), Width = c.Width, CustomWidth = true,
            })));
            writer.WriteStartElement(new SheetData());

            // the title lines, merged over the table's width
            foreach (var line in sheet.Title)
            {
                rowIndex++;
                writer.WriteStartElement(new Row { RowIndex = (uint)rowIndex });
                WriteText(writer, line, styles.Title, rowIndex, 1);
                writer.WriteEndElement();
                if (width > 1)
                {
                    merges.Add($"A{rowIndex}:{lastColumn}{rowIndex}");
                }
            }

            // the header rows: each cell at its column (cells covered by a merge from above are skipped), then merged
            var occupied = new HashSet<(int Row, int Column)>();
            foreach (var cells in header)
            {
                rowIndex++;
                writer.WriteStartElement(new Row { RowIndex = (uint)rowIndex });
                var column = 1;
                var written = new SortedDictionary<int, XlsxHeaderCell>();
                foreach (var cell in cells)
                {
                    while (occupied.Contains((rowIndex, column)))
                    {
                        column++;
                    }

                    written[column] = cell;
                    for (var r = 0; r < cell.RowSpan; r++)
                    {
                        for (var c = 0; c < cell.ColSpan; c++)
                        {
                            occupied.Add((rowIndex + r, column + c));
                        }
                    }

                    if (cell.ColSpan > 1 || cell.RowSpan > 1)
                    {
                        merges.Add($"{ColumnName(column)}{rowIndex}:{ColumnName(column + cell.ColSpan - 1)}{rowIndex + cell.RowSpan - 1}");
                    }

                    column += cell.ColSpan;
                }

                // every cell of the row gets the header style (borders and fill under the merges too)
                for (var c = 1; c <= width; c++)
                {
                    WriteText(writer, written.TryGetValue(c, out var cell) ? cell.Text : string.Empty, styles.Header, rowIndex, c);
                }

                writer.WriteEndElement();
            }

            var lastHeaderRow = rowIndex;
            await foreach (var row in sheet.Rows.WithCancellation(ct))
            {
                rowIndex++;
                WriteRow(writer, columns, row, styles, rowIndex, row.IsTotal);
                count++;
            }

            foreach (var total in sheet.Totals)
            {
                rowIndex++;
                WriteRow(writer, columns, total, styles, rowIndex, true);
            }

            writer.WriteEndElement(); // SheetData
            var filterRange = $"$A${lastHeaderRow}:${lastColumn}${Math.Max(lastHeaderRow, lastHeaderRow + count)}";
            writer.WriteElement(new AutoFilter { Reference = $"A{lastHeaderRow}:{lastColumn}{Math.Max(lastHeaderRow, lastHeaderRow + count)}" });
            if (merges.Count > 0)
            {
                writer.WriteElement(new MergeCells(merges.Select(m => new MergeCell { Reference = m })) { Count = (uint)merges.Count });
            }

            writer.WriteEndElement(); // Worksheet
            return (count, filterRange);
        }
    }

    private static void WriteRow(OpenXmlWriter writer, IReadOnlyList<XlsxColumn> columns, IXlsxRow row, StyleRegistry styles, int rowIndex, bool total)
    {
        writer.WriteStartElement(new Row { RowIndex = (uint)rowIndex });
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            var currency = column.CurrencyKey is { } key ? row.Get(key) as string : null;
            WriteValue(writer, column, row.Get(column.Key), currency, styles, total, rowIndex, i + 1);
        }

        writer.WriteEndElement();
    }

    private static void WriteFilters(WorksheetPart part, XlsxInfo info, int count, StyleRegistry styles)
    {
        using var writer = OpenXmlWriter.Create(part);
        var row = 0;
        writer.WriteStartElement(new Worksheet());
        writer.WriteElement(new Columns(
            new Column { Min = 1, Max = 1, Width = 26, CustomWidth = true },
            new Column { Min = 2, Max = 2, Width = 60, CustomWidth = true }));
        writer.WriteStartElement(new SheetData());
        void Pair(string label, Action<int> value)
        {
            row++;
            writer.WriteStartElement(new Row { RowIndex = (uint)row });
            WriteText(writer, label, styles.Header, row, 1);
            value(row);
            writer.WriteEndElement();
        }

        Pair("Raporti", r => WriteText(writer, info.ReportName, styles.Get(new StyleKey(XlsxKind.Text, false, null)), r, 2));
        Pair("Gjeneruar më", r => WriteDate(writer, info.GeneratedAtUtc, styles.Get(new StyleKey(XlsxKind.DateTime, false, null)), r, 2));
        Pair("Gjeneruar nga", r => WriteText(writer, info.GeneratedBy, styles.Get(new StyleKey(XlsxKind.Text, false, null)), r, 2));
        Pair("Numri i rreshtave", r => writer.WriteElement(new Cell
        {
            CellReference = $"B{r}", DataType = CellValues.Number, StyleIndex = styles.Get(new StyleKey(XlsxKind.Integer, false, null)),
            CellValue = new CellValue(count),
        }));
        row++;
        writer.WriteElement(new Row { RowIndex = (uint)row });
        Pair("Filtrat", r => WriteText(writer, string.Empty, styles.Header, r, 2));
        if (info.Filters.Count == 0)
        {
            Pair("Pa filtra", r => WriteText(writer, "Të gjitha çështjet që mund të shihni", styles.Get(new StyleKey(XlsxKind.Text, false, null)), r, 2));
        }

        foreach (var (label, value) in info.Filters)
        {
            Pair(label, r => WriteText(writer, value, styles.Get(new StyleKey(XlsxKind.Text, false, null)), r, 2));
        }

        writer.WriteEndElement(); // SheetData
        writer.WriteEndElement(); // Worksheet
    }

    private static void WriteValue(
        OpenXmlWriter writer, XlsxColumn column, object? value, string? currency, StyleRegistry styles, bool bold, int row, int col)
    {
        var textStyle = styles.Get(new StyleKey(column.Kind == XlsxKind.LongText ? XlsxKind.LongText : XlsxKind.Text, bold, null));
        switch (value)
        {
            case null:
                writer.WriteElement(new Cell { CellReference = $"{ColumnName(col)}{row}", StyleIndex = textStyle });
                break;
            case bool flag:
                WriteText(writer, flag ? Yes : No, textStyle, row, col);
                break;
            case decimal number:
                WriteNumber(writer, number, column.Kind == XlsxKind.Money ? XlsxKind.Money : column.Kind == XlsxKind.Integer ? XlsxKind.Integer : XlsxKind.Decimal,
                    column.Kind == XlsxKind.Money ? currency : null, styles, bold, row, col, textStyle);
                break;
            case int or long:
                WriteNumber(writer, Convert.ToDecimal(value, CultureInfo.InvariantCulture), column.Kind == XlsxKind.Decimal ? XlsxKind.Decimal : XlsxKind.Integer,
                    null, styles, bold, row, col, textStyle);
                break;
            case double d:
                WriteNumber(writer, (decimal)d, XlsxKind.Decimal, null, styles, bold, row, col, textStyle);
                break;
            case DateTime at:
                WriteDate(writer, at, styles.Get(new StyleKey(column.Kind == XlsxKind.DateTime ? XlsxKind.DateTime : XlsxKind.Date, bold, null)), row, col);
                break;
            case DateOnly date:
                writer.WriteElement(new Cell
                {
                    CellReference = $"{ColumnName(col)}{row}", DataType = CellValues.Number,
                    StyleIndex = styles.Get(new StyleKey(XlsxKind.Date, bold, null)),
                    CellValue = new CellValue(date.ToDateTime(TimeOnly.MinValue).ToOADate()),
                });
                break;
            default:
                WriteText(writer, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, textStyle, row, col);
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

    private static void WriteNumber(
        OpenXmlWriter writer, decimal value, XlsxKind kind, string? currency, StyleRegistry styles, bool bold, int row, int col, uint textStyle)
    {
        if (!FitsExcelNumber(value))
        {
            // more digits than a double holds: exact, as text
            WriteText(writer, value.ToString("0.00", CultureInfo.InvariantCulture) + (currency is null ? string.Empty : " " + currency), textStyle, row, col);
            return;
        }

        writer.WriteElement(new Cell
        {
            CellReference = $"{ColumnName(col)}{row}", DataType = CellValues.Number,
            StyleIndex = styles.Get(new StyleKey(kind, bold, currency)), CellValue = new CellValue(value),
        });
    }

    /// <summary>A UTC instant as an Excel date in Tirane (Excel has no time zones: the serial is the local wall time).</summary>
    private static void WriteDate(OpenXmlWriter writer, DateTime utc, uint style, int row, int col)
    {
        var tirane = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TiraneTime.Zone);
        writer.WriteElement(new Cell
        {
            CellReference = $"{ColumnName(col)}{row}", DataType = CellValues.Number, StyleIndex = style, CellValue = new CellValue(tirane.ToOADate()),
        });
    }

    /// <summary>An inline string cell (no shared-string table to keep in memory), never a formula.</summary>
    private static void WriteText(OpenXmlWriter writer, string text, uint style, int row, int col) =>
        writer.WriteElement(new Cell
        {
            CellReference = $"{ColumnName(col)}{row}",
            DataType = CellValues.InlineString,
            StyleIndex = style,
            InlineString = new InlineString(new Text(XmlSafe(text)) { Space = SpaceProcessingModeValues.Preserve }),
        });

    /// <summary>XML 1.0 cannot carry most control characters (a pasted text may hold one): dropped; tabs and line breaks kept.</summary>
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

    /// <summary>A data cell's style: its kind, bold (a totals row), and for money the currency in the format.</summary>
    private readonly record struct StyleKey(XlsxKind Kind, bool Bold, string? Currency);

    /// <summary>
    /// The workbook's cell formats, added as cells need them (a new currency → a new number format); written last.
    /// Fonts: 0 normal, 1 bold, 2 bold white (header), 3 bold 13 pt (title). Fills: 2 navy. Borders: 1 thin all round,
    /// 2 thin with a medium top (totals).
    /// </summary>
    private sealed class StyleRegistry
    {
        private readonly List<CellFormat> _formats =
        [
            new CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0 },
        ];

        private readonly Dictionary<StyleKey, uint> _data = [];
        private readonly List<NumberingFormat> _numberFormats =
        [
            new NumberingFormat { NumberFormatId = 164, FormatCode = "dd.mm.yyyy" },
            new NumberingFormat { NumberFormatId = 165, FormatCode = "dd.mm.yyyy hh:mm" },
        ];

        private readonly Dictionary<string, uint> _currencyFormats = new(StringComparer.Ordinal);

        public StyleRegistry()
        {
            // 1 header: bold white on navy, centered, wrapped, borders
            Header = Add(new CellFormat(new Alignment
            {
                Horizontal = HorizontalAlignmentValues.Center, Vertical = VerticalAlignmentValues.Center, WrapText = true,
            })
            { NumberFormatId = 0, FontId = 2, FillId = 2, BorderId = 1, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true });
            // 2 title: bold 13 pt, centered
            Title = Add(new CellFormat(new Alignment { Horizontal = HorizontalAlignmentValues.Center })
            { NumberFormatId = 0, FontId = 3, FillId = 0, BorderId = 0, ApplyFont = true, ApplyAlignment = true });
        }

        public uint Header { get; }
        public uint Title { get; }

        private uint Add(CellFormat format)
        {
            _formats.Add(format);
            return (uint)(_formats.Count - 1);
        }

        public uint Get(StyleKey key)
        {
            if (_data.TryGetValue(key, out var index))
            {
                return index;
            }

            uint numberFormat = key.Kind switch
            {
                XlsxKind.Date => 164,
                XlsxKind.DateTime => 165,
                XlsxKind.Decimal => 4, // #,##0.00
                XlsxKind.Integer => 3, // #,##0
                XlsxKind.Money => CurrencyFormat(key.Currency),
                _ => 49, // "@": typing in the cell later stays text too
            };
            var alignment = new Alignment
            {
                Vertical = VerticalAlignmentValues.Top,
                WrapText = key.Kind == XlsxKind.LongText,
                Horizontal = key.Kind is XlsxKind.Decimal or XlsxKind.Integer or XlsxKind.Money ? HorizontalAlignmentValues.Right : null,
            };
            index = Add(new CellFormat(alignment)
            {
                NumberFormatId = numberFormat, FontId = key.Bold ? 1U : 0U, FillId = 0, BorderId = key.Bold ? 2U : 1U,
                ApplyNumberFormat = true, ApplyFont = true, ApplyBorder = true, ApplyAlignment = true,
            });
            _data[key] = index;
            return index;
        }

        /// <summary>#,##0.00 "ALL" — the code is part of the format, the cell stays a number.</summary>
        private uint CurrencyFormat(string? currency)
        {
            if (string.IsNullOrWhiteSpace(currency))
            {
                return 4;
            }

            var code = new string(currency.Where(char.IsLetter).ToArray()).ToUpperInvariant();
            if (_currencyFormats.TryGetValue(code, out var id))
            {
                return id;
            }

            id = (uint)(166 + _currencyFormats.Count);
            _numberFormats.Add(new NumberingFormat { NumberFormatId = id, FormatCode = $"#,##0.00 \"{code}\"" });
            _currencyFormats[code] = id;
            return id;
        }

        public Stylesheet Build()
        {
            var thin = () => new Border(
                new LeftBorder(new Color { Rgb = "FF9AA5B1" }) { Style = BorderStyleValues.Thin },
                new RightBorder(new Color { Rgb = "FF9AA5B1" }) { Style = BorderStyleValues.Thin },
                new TopBorder(new Color { Rgb = "FF9AA5B1" }) { Style = BorderStyleValues.Thin },
                new BottomBorder(new Color { Rgb = "FF9AA5B1" }) { Style = BorderStyleValues.Thin },
                new DiagonalBorder());
            var totals = new Border(
                new LeftBorder(new Color { Rgb = "FF9AA5B1" }) { Style = BorderStyleValues.Thin },
                new RightBorder(new Color { Rgb = "FF9AA5B1" }) { Style = BorderStyleValues.Thin },
                new TopBorder(new Color { Rgb = "FF1F3A5F" }) { Style = BorderStyleValues.Medium },
                new BottomBorder(new Color { Rgb = "FF9AA5B1" }) { Style = BorderStyleValues.Thin },
                new DiagonalBorder());
            return new Stylesheet(
                new NumberingFormats(_numberFormats.Select(f => (NumberingFormat)f.CloneNode(true))) { Count = (uint)_numberFormats.Count },
                new Fonts(
                    new Font(new FontSize { Val = 11 }, new FontName { Val = "Calibri" }),
                    new Font(new Bold(), new FontSize { Val = 11 }, new FontName { Val = "Calibri" }),
                    new Font(new Bold(), new FontSize { Val = 11 }, new Color { Rgb = HeaderText }, new FontName { Val = "Calibri" }),
                    new Font(new Bold(), new FontSize { Val = 13 }, new FontName { Val = "Calibri" }))
                { Count = 4 },
                new Fills(
                    new Fill(new PatternFill { PatternType = PatternValues.None }),
                    new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
                    new Fill(new PatternFill(new ForegroundColor { Rgb = HeaderFill }) { PatternType = PatternValues.Solid }))
                { Count = 3 },
                new Borders(new Border(new LeftBorder(), new RightBorder(), new TopBorder(), new BottomBorder(), new DiagonalBorder()), thin(), totals) { Count = 3 },
                new CellStyleFormats(new CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0 }) { Count = 1 },
                new CellFormats(_formats.Select(f => (CellFormat)f.CloneNode(true))) { Count = (uint)_formats.Count },
                new CellStyles(new CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }) { Count = 1 });
        }
    }
}
