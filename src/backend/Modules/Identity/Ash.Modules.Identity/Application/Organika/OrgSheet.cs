using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Ash.Modules.Identity.Application.Organika;

/// <summary>One data row of the sheet, as typed (trimmed; empty → null). Line = the sheet's row number.</summary>
public sealed record OrgSheetRow(int Line, string? Email, string? Position, string? Unit, string? ManagerEmail);

/// <summary>
/// The Organika sheet (task 053g): a fixed template — email, pozicioni, njesia, eprori_email (optional) — as .xlsx or
/// .csv (comma or semicolon). Headers are matched case- and accent-insensitively ("Njësia/zyra" = "njesia"). No
/// diagrams or images: text columns only (client decision E).
/// </summary>
public static class OrgSheet
{
    public const string EmailColumn = "email";
    public const string PositionColumn = "pozicioni";
    public const string UnitColumn = "njesia";
    public const string ManagerColumn = "eprori_email";
    public static readonly string[] Headers = [EmailColumn, PositionColumn, UnitColumn, ManagerColumn];

    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string TemplateFileName = "organika-shabllon.xlsx";

    /// <summary>The template: one sheet "Organika" with the header row.</summary>
    public static byte[] Template()
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbook = document.AddWorkbookPart();
            workbook.Workbook = new Workbook();
            var sheetPart = workbook.AddNewPart<WorksheetPart>();
            var row = new Row { RowIndex = 1 };
            foreach (var header in Headers)
            {
                row.Append(new Cell { DataType = CellValues.InlineString, InlineString = new InlineString(new Text(header)) });
            }

            sheetPart.Worksheet = new Worksheet(new SheetData(row));
            workbook.Workbook.AppendChild(new Sheets()).Append(new Sheet
            {
                Id = workbook.GetIdOfPart(sheetPart), SheetId = 1, Name = "Organika",
            });
            workbook.Workbook.Save();
        }

        return stream.ToArray();
    }

    /// <summary>The rows, or the reason the file cannot be read (wrong format, missing email/pozicioni headers).</summary>
    public static (IReadOnlyList<OrgSheetRow>? Rows, string? Error) Read(string fileName, byte[] content)
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

        if (table.Count == 0)
        {
            return (null, "Skedari është bosh.");
        }

        var header = table[0].Select(Normalize).ToList();
        int Index(string name) => header.FindIndex(h => h == name || h.StartsWith(name, StringComparison.Ordinal));
        var email = Index(EmailColumn);
        var position = Index(PositionColumn);
        var unit = Index(UnitColumn);
        var manager = Index("eprori");
        if (email < 0 || position < 0)
        {
            return (null, "Rreshti i parë duhet të ketë kolonat e shabllonit: email, pozicioni, njesia, eprori_email.");
        }

        string? At(string[] cells, int i) => i >= 0 && i < cells.Length && !string.IsNullOrWhiteSpace(cells[i]) ? cells[i].Trim() : null;
        var rows = new List<OrgSheetRow>();
        for (var i = 1; i < table.Count; i++)
        {
            var cells = table[i];
            if (cells.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            rows.Add(new OrgSheetRow(i + 1, At(cells, email), At(cells, position), At(cells, unit), At(cells, manager)));
        }

        return (rows, null);
    }

    /// <summary>"Njësia/zyra" → "njesia_zyra": lower case, no accents, spaces and slashes as _.</summary>
    private static string Normalize(string header)
    {
        var decomposed = header.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var text = new StringBuilder();
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            text.Append(char.IsLetterOrDigit(c) ? c : '_');
        }

        return text.ToString().Trim('_');
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

    /// <summary>"C7" → 2 (zero-based); a cell without a reference (rare) is treated as the first column.</summary>
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

    /// <summary>RFC 4180-ish: quotes ("" inside), comma or semicolon (whichever the header line uses more), UTF-8 (BOM ok).</summary>
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
}
