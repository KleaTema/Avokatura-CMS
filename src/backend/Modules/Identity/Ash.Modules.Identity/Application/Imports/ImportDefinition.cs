using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Ash.Modules.Identity.Application.Imports;

/// <summary>Whether a column must be filled: always, never, or one of a group (e.g. zyra_kodi OSE institucioni).</summary>
public static class ImportRequirement
{
    public const string Required = "REQUIRED";
    public const string Optional = "OPTIONAL";
    public const string OneOf = "ONE_OF";
}

/// <summary>One column of an import sheet: its header, whether required, what it holds (format), an example value.</summary>
public sealed record ImportColumn(string Name, string Requirement, string Description, string Example);

/// <summary>
/// Task 053l — ONE definition per import kind: the parser reads the sheet with it, the template is written from it, and
/// the page shows its instructions (GET …/definition) — so the columns, the rules and the texts can never drift.
/// </summary>
public sealed record ImportDefinition(
    string Kind,
    string Title,
    string SheetName,
    string TemplateFileName,
    string Purpose,
    IReadOnlyList<ImportColumn> Columns,
    IReadOnlyList<string> Rules)
{
    public const int MaxRows = 5000;
    public const long MaxFileBytes = 5 * 1024 * 1024;
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string InstructionsSheet = "Udhëzime";

    /// <summary>The accepted formats, the same for every kind.</summary>
    public static readonly IReadOnlyList<string> Formats =
    [
        "Excel (.xlsx) — lexohet vetëm fleta e parë.",
        "CSV (.csv) me kodim UTF-8, me ndarës pikëpresje (;) ose presje (,).",
    ];

    /// <summary>The rules every kind shares (shown before the kind's own).</summary>
    public static readonly IReadOnlyList<string> CommonRules =
    [
        "Rreshti i parë mban titujt e kolonave, saktësisht si më sipër (shkronjat e mëdha/të vogla dhe ë/ç nuk kanë rëndësi).",
        "Një rresht për çdo person; pa qeliza të bashkuara dhe pa rreshta bosh ndërmjet të dhënave.",
        "Rreshti i dytë i shabllonit është shembull: zëvendësojeni me të dhënat tuaja ose fshijeni.",
        $"Deri në {MaxRows} rreshta dhe deri në {MaxFileBytes / 1024 / 1024} MB.",
        "Asgjë nuk ruhet para se të shtypni „Apliko\": fillimisht shihni pamjen paraprake me gabimet për çdo qelizë.",
    ];

    public IEnumerable<string> AllRules => CommonRules.Concat(Rules);

    // ---------------------------------------------------------------- reading

    /// <summary>The data rows (header → cell, trimmed, empty → null), or the reason the file cannot be read.</summary>
    public (IReadOnlyList<ImportRow>? Rows, string? Error) Read(string fileName, byte[] content)
    {
        List<string[]> table;
        try
        {
            table = fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? ReadCsv(content)
                : fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ? ReadXlsx(content)
                : throw new FormatException("Lejohen vetëm skedarë .xlsx ose .csv.");
        }
        catch (FormatException ex)
        {
            return (null, ex.Message);
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or IOException)
        {
            return (null, "Skedari nuk mund të lexohet si Excel (.xlsx).");
        }

        if (table.Count == 0 || table[0].All(string.IsNullOrWhiteSpace))
        {
            return (null, "Skedari është bosh: rreshti i parë duhet të ketë titujt e kolonave.");
        }

        var header = table[0].Select(Normalize).ToList();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var column in Columns)
        {
            var exact = header.IndexOf(column.Name);
            index[column.Name] = exact >= 0 ? exact : header.FindIndex(h => h.Length > 0 && h.StartsWith(column.Name, StringComparison.Ordinal));
        }

        var missing = Columns.Where(c => c.Requirement != ImportRequirement.Optional && index[c.Name] < 0).Select(c => c.Name).ToList();
        if (missing.Count > 0)
        {
            return (null, $"Rreshti i parë nuk ka kolonat: {string.Join(", ", missing)}. Titujt duhet të jenë: {string.Join(", ", Columns.Select(c => c.Name))}.");
        }

        var rows = new List<ImportRow>();
        for (var i = 1; i < table.Count; i++)
        {
            var cells = table[i];
            if (cells.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var values = Columns.ToDictionary(c => c.Name, c =>
            {
                var at = index[c.Name];
                return at >= 0 && at < cells.Length && !string.IsNullOrWhiteSpace(cells[at]) ? cells[at].Trim() : null;
            });
            rows.Add(new ImportRow(i + 1, values));
        }

        if (rows.Count > MaxRows)
        {
            return (null, $"Skedari mund të ketë deri në {MaxRows} rreshta të dhënash.");
        }

        return (rows, null);
    }

    /// <summary>"Njësia/Zyra" → "njesia_zyra": lower case, no accents, every other sign as _.</summary>
    public static string Normalize(string text)
    {
        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var result = new StringBuilder();
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            result.Append(char.IsLetterOrDigit(c) ? c : '_');
        }

        return result.ToString().Trim('_');
    }

    private static List<string[]> ReadXlsx(byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var document = SpreadsheetDocument.Open(stream, false);
        var workbook = document.WorkbookPart ?? throw new FormatException("Skedari Excel nuk ka fletë.");
        var sheet = workbook.Workbook.Sheets?.Elements<Sheet>().FirstOrDefault() ?? throw new FormatException("Skedari Excel nuk ka fletë.");
        var part = (WorksheetPart)workbook.GetPartById(sheet.Id!);
        var shared = workbook.SharedStringTablePart?.SharedStringTable.Elements<SharedStringItem>().Select(s => s.InnerText).ToList() ?? [];

        var result = new List<string[]>();
        foreach (var row in part.Worksheet.Descendants<Row>())
        {
            var cells = new List<string>();
            foreach (var cell in row.Elements<Cell>())
            {
                var column = ColumnIndex(cell.CellReference?.Value);
                while (cells.Count < column)
                {
                    cells.Add(string.Empty);
                }

                var value = cell.DataType?.Value == CellValues.SharedString && int.TryParse(cell.CellValue?.Text, out var s) && s < shared.Count
                    ? shared[s]
                    : cell.DataType?.Value == CellValues.InlineString ? cell.InlineString?.InnerText ?? string.Empty
                    : cell.CellValue?.Text ?? string.Empty;
                cells.Add(value);
            }

            result.Add([.. cells]);
        }

        return result;
    }

    /// <summary>"C7" → 2 (zero-based); a cell without a reference (rare) is treated as the next one.</summary>
    private static int ColumnIndex(string? reference)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return 0;
        }

        var index = 0;
        foreach (var c in reference.TakeWhile(char.IsLetter))
        {
            index = index * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
        }

        return index - 1;
    }

    /// <summary>RFC 4180-ish: quotes ("" inside), ; or , (whichever the header line uses more), UTF-8 (BOM ok).</summary>
    private static List<string[]> ReadCsv(byte[] content)
    {
        var text = Encoding.UTF8.GetString(content).TrimStart('﻿');
        var firstLine = text.Split('\n')[0];
        var separator = firstLine.Count(c => c == ';') > firstLine.Count(c => c == ',') ? ';' : ',';
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == separator)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                row.Add(field.ToString());
                field.Clear();
                rows.Add([.. row]);
                row.Clear();
            }
            else
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add([.. row]);
        }

        return rows;
    }

    // ---------------------------------------------------------------- writing

    /// <summary>
    /// The template: the first sheet with the headers and the example row; the sheet "Udhëzime" with the same text the
    /// page shows (purpose, formats, columns, rules).
    /// </summary>
    public byte[] Template()
    {
        List<string[]> instructions = [[Title], [Purpose], [], ["Formatet e pranuara"]];
        instructions.AddRange(Formats.Select(f => new[] { f }));
        instructions.Add([]);
        instructions.Add(["Kolona", "E detyrueshme", "Përmbajtja", "Shembull"]);
        instructions.AddRange(Columns.Select(c => new[] { c.Name, RequirementText(c.Requirement), c.Description, c.Example }));
        instructions.Add([]);
        instructions.Add(["Rregullat"]);
        instructions.AddRange(AllRules.Select(r => new[] { r }));

        return Workbook(
            (SheetName, [Columns.Select(c => c.Name).ToArray(), Columns.Select(c => c.Example).ToArray()]),
            (InstructionsSheet, instructions));
    }

    public static string RequirementText(string requirement) => requirement switch
    {
        ImportRequirement.Required => "Po",
        ImportRequirement.OneOf => "Njëra nga të dyja",
        _ => "Jo",
    };

    /// <summary>A workbook of text sheets (inline strings), in the order given.</summary>
    public static byte[] Workbook(params (string Name, IReadOnlyList<string[]> Rows)[] sheets)
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbook = document.AddWorkbookPart();
            workbook.Workbook = new Workbook();
            var sheetList = workbook.Workbook.AppendChild(new Sheets());
            uint id = 1;
            foreach (var (name, rows) in sheets)
            {
                var part = workbook.AddNewPart<WorksheetPart>();
                var data = new SheetData();
                uint index = 1;
                foreach (var cells in rows)
                {
                    var row = new Row { RowIndex = index++ };
                    foreach (var text in cells)
                    {
                        row.Append(new Cell { DataType = CellValues.InlineString, InlineString = new InlineString(new Text(text ?? string.Empty)) });
                    }

                    data.Append(row);
                }

                part.Worksheet = new Worksheet(data);
                sheetList.Append(new Sheet { Id = workbook.GetIdOfPart(part), SheetId = id++, Name = name });
            }

            workbook.Workbook.Save();
        }

        return stream.ToArray();
    }
}

/// <summary>One data row: its line in the sheet and the value of each column (null = empty).</summary>
public sealed record ImportRow(int Line, IReadOnlyDictionary<string, string?> Cells)
{
    public string? this[string column] => Cells.GetValueOrDefault(column);
}
