using System.Net.Http.Headers;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Shouldly;

namespace Ash.Tests.Integration.Infrastructure;

/// <summary>Task 053l: the files and calls the import tests share (Organika, users).</summary>
public static class ImportFiles
{
    public const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static async Task<HttpResponseMessage> Upload(HttpClient client, string basePath, string fileName, byte[] content, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(fileName.EndsWith(".csv") ? "text/csv" : XlsxType);
        form.Add(file, "file", fileName);
        return await client.PostAsync($"{basePath}/", form, ct);
    }

    public static async Task<JsonElement> Json(HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        response.IsSuccessStatusCode.ShouldBeTrue(text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>The rows of the first sheet of a workbook (shared or inline strings).</summary>
    public static List<string[]> ReadSheet(byte[] xlsx, int sheet = 0)
    {
        using var stream = new MemoryStream(xlsx);
        using var document = SpreadsheetDocument.Open(stream, false);
        var workbook = document.WorkbookPart!;
        var sheets = workbook.Workbook.Sheets!.Elements<Sheet>().ToList();
        var part = (WorksheetPart)workbook.GetPartById(sheets[sheet].Id!);
        var shared = workbook.SharedStringTablePart?.SharedStringTable.Elements<SharedStringItem>().Select(s => s.InnerText).ToList() ?? [];
        return part.Worksheet.Descendants<Row>().Select(r => r.Elements<Cell>().Select(c =>
                c.DataType?.Value == CellValues.SharedString ? shared[int.Parse(c.CellValue!.Text)]
                : c.DataType?.Value == CellValues.InlineString ? c.InlineString!.InnerText : c.CellValue?.Text ?? string.Empty)
            .ToArray()).ToList();
    }

    public static IReadOnlyList<string> SheetNames(byte[] xlsx)
    {
        using var stream = new MemoryStream(xlsx);
        using var document = SpreadsheetDocument.Open(stream, false);
        return document.WorkbookPart!.Workbook.Sheets!.Elements<Sheet>().Select(s => s.Name!.Value!).ToList();
    }

    /// <summary>A one-sheet workbook with shared strings, as Excel writes it (empty cells left out).</summary>
    public static byte[] Xlsx(params string[][] rows)
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbook = document.AddWorkbookPart();
            workbook.Workbook = new Workbook();
            var strings = workbook.AddNewPart<SharedStringTablePart>();
            strings.SharedStringTable = new SharedStringTable();
            var sheetPart = workbook.AddNewPart<WorksheetPart>();
            var data = new SheetData();
            var index = 0;
            for (var r = 0; r < rows.Length; r++)
            {
                var row = new Row { RowIndex = (uint)(r + 1) };
                for (var c = 0; c < rows[r].Length; c++)
                {
                    if (rows[r][c].Length == 0)
                    {
                        continue;
                    }

                    strings.SharedStringTable.Append(new SharedStringItem(new Text(rows[r][c])));
                    row.Append(new Cell
                    {
                        CellReference = $"{(char)('A' + c)}{r + 1}", DataType = CellValues.SharedString,
                        CellValue = new CellValue((index++).ToString()),
                    });
                }

                data.Append(row);
            }

            sheetPart.Worksheet = new Worksheet(data);
            workbook.Workbook.AppendChild(new Sheets()).Append(new Sheet { Id = workbook.GetIdOfPart(sheetPart), SheetId = 1, Name = "S" });
            workbook.Workbook.Save();
        }

        return stream.ToArray();
    }
}
