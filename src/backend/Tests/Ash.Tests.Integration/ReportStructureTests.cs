using System.Globalization;
using System.Net;
using System.Text;
using Ash.SharedKernel.Ids;
using Ash.Tests.Integration.Infrastructure;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 053o — the tables are the client's (docs/client/Tabelat e raportimit.xlsx, Tabelat e statistikave.xlsx), in the
/// web page and in the Excel export alike. The exports are read back with the Open XML SDK and their header rows (texts
/// and merges) compared to the client's files: every client column with the same groups above it, in the same order,
/// with the same letter (statistics). The styled template is checked too (header fill, borders, merges, frozen header,
/// autofilter, bold totals).
/// <para>Texts are compared normalized (case, ë / ç, punctuation, spaces); the client's few slips are mapped below.</para>
/// </summary>
public class ReportStructureTests(MySqlFixture db) : IntegrationTest(db)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The client's slips in the header texts (the app writes them correctly).</summary>
    private static readonly (string Slip, string Correct)[] ClientSlips =
    [
        ("kopetence", "kompetence"), ("juredik", "juridik"), ("qe ka nuk ka", "qe nuk ka"), ("rradhes", "radhes"),
        ("i ceshtjet", "i ceshtjes"), ("aokati", "avokati"), ("rregjistrimit", "regjistrimit"), ("arsyetur", "arsyetuar"),
    ];

    // ---------------------------------------------------------------- reading a workbook

    private sealed record Sheet(Dictionary<(int Row, int Col), string> Cells, List<(int R1, int C1, int R2, int C2)> Merges, WorksheetPart Part, WorkbookPart Workbook);

    private static (SpreadsheetDocument Document, Sheet Sheet) Open(Stream stream, int index = 0)
    {
        var document = SpreadsheetDocument.Open(stream, false);
        var workbook = document.WorkbookPart!;
        var sheetEntry = workbook.Workbook.Sheets!.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>().ElementAt(index);
        var part = (WorksheetPart)workbook.GetPartById(sheetEntry.Id!);
        var shared = workbook.SharedStringTablePart?.SharedStringTable.Elements<SharedStringItem>().Select(s => s.InnerText).ToList() ?? [];
        var cells = new Dictionary<(int, int), string>();
        foreach (var cell in part.Worksheet.Descendants<Cell>())
        {
            var text = cell.DataType?.Value == CellValues.SharedString ? shared[int.Parse(cell.CellValue!.Text, CultureInfo.InvariantCulture)]
                : cell.DataType?.Value == CellValues.InlineString ? cell.InlineString?.InnerText ?? string.Empty
                : cell.CellValue?.Text ?? string.Empty;
            cells[Ref(cell.CellReference!.Value!)] = text;
        }

        var merges = part.Worksheet.Descendants<MergeCell>().Select(m =>
        {
            var ends = m.Reference!.Value!.Split(':');
            var (r1, c1) = Ref(ends[0]);
            var (r2, c2) = Ref(ends[1]);
            return (r1, c1, r2, c2);
        }).ToList();
        return (document, new Sheet(cells, merges, part, workbook));
    }

    private static (int Row, int Col) Ref(string reference)
    {
        var letters = new string(reference.TakeWhile(char.IsLetter).ToArray());
        var col = letters.Aggregate(0, (n, ch) => n * 26 + (char.ToUpperInvariant(ch) - 'A' + 1));
        return (int.Parse(reference[letters.Length..], CultureInfo.InvariantCulture), col);
    }

    private static string Normal(string text, bool client)
    {
        var s = text.ToLowerInvariant().Replace('ë', 'e').Replace('ç', 'c').Replace('²', '2');
        s = new string(s.Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ').ToArray());
        s = string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (client)
        {
            foreach (var (slip, correct) in ClientSlips)
            {
                s = s.Replace(slip, correct, StringComparison.Ordinal);
            }
        }

        return s;
    }

    /// <summary>The header cell covering (row, col): the merge it is in (its top-left), or the cell itself.</summary>
    private static (int Row, int Col) Owner(Sheet sheet, int row, int col)
    {
        foreach (var m in sheet.Merges)
        {
            if (row >= m.R1 && row <= m.R2 && col >= m.C1 && col <= m.C2)
            {
                return (m.R1, m.C1);
            }
        }

        return (row, col);
    }

    /// <summary>The leaf columns of a header (rows top..bottom, columns from..to): each with its path of group texts.</summary>
    private static List<(int Col, string Path, string Owner)> Leaves(Sheet sheet, int top, int bottom, int from, int to, bool client)
    {
        var leaves = new List<(int, string, string)>();
        var seen = new HashSet<(int, int)>();
        for (var col = from; col <= to; col++)
        {
            var bottomOwner = Owner(sheet, bottom, col);
            if (!seen.Add(bottomOwner))
            {
                continue; // a leaf merged over several columns: counted once
            }

            var path = new List<string>();
            var owners = new HashSet<(int, int)>();
            for (var row = top; row <= bottom; row++)
            {
                var owner = Owner(sheet, row, col);
                if (owners.Add(owner) && sheet.Cells.TryGetValue(owner, out var text) && Normal(text, client) is { Length: > 0 } n)
                {
                    path.Add(n);
                }
            }

            if (path.Count > 0)
            {
                leaves.Add((col, string.Join(" / ", path), $"{bottomOwner.Item1}:{bottomOwner.Item2}"));
            }
        }

        return leaves;
    }

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine([dir.FullName, .. parts])))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull($"{string.Join('/', parts)} not found above the test output");
        return Path.Combine([dir.FullName, .. parts]);
    }

    private async Task<Guid> AnyInstitution()
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand("SELECT public_id FROM institutions WHERE is_active = 1 ORDER BY id LIMIT 1", connection);
        return PublicId.FromBytes((byte[])(await command.ExecuteScalarAsync(Ct))!);
    }

    private static async Task<MemoryStream> Download(HttpClient client, string url)
    {
        var response = await client.GetAsync(url, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var stream = new MemoryStream(await response.Content.ReadAsByteArrayAsync(Ct));
        return stream;
    }

    // ---------------------------------------------------------------- the statistics

    /// <summary>
    /// Tabela 3 (client Tabela 5: header rows 7–9, letters row 10) and Tabela 4 (client Tabela 6: rows 24–26, letters 27):
    /// every client column (after "Gjykata"; the ungrouped AJ / AK cells excluded — report_settings
    /// statistics.carried_orphan = HIDE) is in the export, in the same order, with the same groups above it and the
    /// same letter. The export may hold more (the provisional "Tjetër", one money column per currency).
    /// </summary>
    [Theory]
    [InlineData(5, 7, 9, 10)]
    [InlineData(6, 24, 26, 27)]
    public async Task The_statistics_export_has_the_client_header_rows_merges_and_letters(int table, int top, int bottom, int letterRow)
    {
        await using var factory = new AshApiFactory(Db);
        using var monitor = factory.CreateClient().AsUser("monitor@ash.local");
        var filters = table == 6 ? $"{{\"year\":\"2026\",\"institution\":\"{await AnyInstitution()}\"}}" : "{\"year\":\"2026\"}";
        await using var exported = await Download(monitor, $"/api/reports/builder/{table}/export?format=xlsx&filters={Uri.EscapeDataString(filters)}");

        await using var clientFile = File.OpenRead(RepoFile("docs", "client", "Tabelat e statistikave.xlsx"));
        var (clientDoc, client) = Open(clientFile);
        var (ourDoc, ours) = Open(exported);
        using (clientDoc)
        using (ourDoc)
        {
            const int orphanAj = 36, orphanAk = 37;
            var expected = Leaves(client, top, bottom, 2, 41, client: true).Where(l => l.Col is not (orphanAj or orphanAk)).ToList();
            // ours: 3 title lines, then 3 header rows, the letter row, the formula row
            var ourLeaves = Leaves(ours, 4, 6, 2, ours.Cells.Keys.Max(k => k.Col), client: false);

            var report = new StringBuilder();
            var at = 0;
            foreach (var leaf in expected)
            {
                var found = ourLeaves.FindIndex(at, l => l.Path == leaf.Path);
                if (found < 0)
                {
                    report.AppendLine($"missing in order: {leaf.Path}");
                    continue;
                }

                var clientLetter = client.Cells.GetValueOrDefault((letterRow, leaf.Col), string.Empty).Trim();
                var ourLetter = ours.Cells.GetValueOrDefault((7, ourLeaves[found].Col), string.Empty).Trim();
                if (!string.Equals(clientLetter, ourLetter, StringComparison.Ordinal))
                {
                    report.AppendLine($"letter of {leaf.Path}: client '{clientLetter}', ours '{ourLetter}'");
                }

                at = found + 1;
            }

            report.ToString().ShouldBeEmpty("client columns:\n" + string.Join('\n', expected.Select(l => l.Path))
                + "\n\nours:\n" + string.Join('\n', ourLeaves.Select(l => l.Path)));
            // the header's groups are merged cells (e.g. "Çështje të mbyllura" over its Avokat | Delegim/Relatim pair)
            ours.Merges.ShouldContain(m => m.R1 == 4 && m.C2 > m.C1);
            ours.Cells[(1, 1)].ShouldBe("AVOKATURA E SHTETIT");
            ours.Cells[(2, 1)].ShouldBe("STATISTIKA");
        }
    }

    // ---------------------------------------------------------------- the reports

    /// <summary>
    /// Tabela 1 (client Tabela 1: rows 4–5) and Tabela 2 (client Tabela 3: rows 29–30): the export of the default view
    /// has exactly the client's columns, in order, with "Vlera e padisë" / "Vlera" merged over M2 | Monedha.
    /// </summary>
    [Theory]
    [InlineData(1, 4, 5, 17)]
    [InlineData(3, 29, 30, 16)]
    public async Task The_report_export_has_the_client_columns_and_grouped_header(int table, int top, int bottom, int lastCol)
    {
        await using var factory = new AshApiFactory(Db);
        using var monitor = factory.CreateClient().AsUser("monitor@ash.local");
        await using var exported = await Download(monitor, $"/api/reports/builder/{table}/export?format=xlsx&filters={Uri.EscapeDataString("{}")}");

        await using var clientFile = File.OpenRead(RepoFile("docs", "client", "Tabelat e raportimit.xlsx"));
        var (clientDoc, client) = Open(clientFile);
        var (ourDoc, ours) = Open(exported);
        using (clientDoc)
        using (ourDoc)
        {
            var expected = Leaves(client, top, bottom, 2, lastCol, client: true).Select(l => l.Path).ToList();
            var actual = Leaves(ours, 1, 2, 1, ours.Cells.Keys.Where(k => k.Row <= 2).Max(k => k.Col), client: false).Select(l => l.Path).ToList();
            actual.ShouldBe(expected);
            // the group over M2 | Monedha is one merged cell of the first header row
            ours.Merges.ShouldContain(m => m.R1 == 1 && m.R2 == 1 && m.C2 == m.C1 + 1);
        }
    }

    // ---------------------------------------------------------------- the template

    /// <summary>
    /// The styled template of every export: header bold white on navy with borders, data cells with thin borders,
    /// the header frozen, an autofilter, merged grouped headers, the totals row bold.
    /// </summary>
    [Fact]
    public async Task Every_xlsx_uses_the_styled_template()
    {
        await using var factory = new AshApiFactory(Db);
        using var monitor = factory.CreateClient().AsUser("monitor@ash.local");
        await using var exported = await Download(monitor, $"/api/reports/builder/1/export?format=xlsx&filters={Uri.EscapeDataString("{}")}");
        var (document, sheet) = Open(exported);
        using (document)
        {
            var stylesheet = sheet.Workbook.WorkbookStylesPart!.Stylesheet;
            var formats = stylesheet.CellFormats!.Elements<CellFormat>().ToList();
            var fills = stylesheet.Fills!.Elements<Fill>().ToList();
            var fonts = stylesheet.Fonts!.Elements<Font>().ToList();
            var borders = stylesheet.Borders!.Elements<Border>().ToList();
            CellFormat FormatOf(int row, int col) =>
                formats[(int)(sheet.Part.Worksheet.Descendants<Cell>().First(c => c.CellReference!.Value == $"{(char)('A' + col - 1)}{row}").StyleIndex?.Value ?? 0)];

            var header = FormatOf(1, 1);
            fills[(int)header.FillId!.Value].PatternFill!.ForegroundColor!.Rgb!.Value.ShouldBe("FF1F3A5F");
            fonts[(int)header.FontId!.Value].Bold.ShouldNotBeNull();
            fonts[(int)header.FontId!.Value].Color!.Rgb!.Value.ShouldBe("FFFFFFFF");
            borders[(int)header.BorderId!.Value].LeftBorder!.Style!.Value.ShouldBe(BorderStyleValues.Thin);

            // the totals row (after the data; here the first row under the two header rows) is bold, with borders
            var totals = FormatOf(3, 1);
            fonts[(int)totals.FontId!.Value].Bold.ShouldNotBeNull();
            borders[(int)totals.BorderId!.Value].TopBorder!.Style!.Value.ShouldBe(BorderStyleValues.Medium);
            sheet.Cells[(3, 1)].ShouldStartWith("Gjithsej");

            var pane = sheet.Part.Worksheet.Descendants<Pane>().Single();
            pane.State!.Value.ShouldBe(PaneStateValues.Frozen);
            pane.VerticalSplit!.Value.ShouldBe(2); // both header rows stay
            sheet.Part.Worksheet.Descendants<AutoFilter>().ShouldHaveSingleItem();
            sheet.Merges.ShouldNotBeEmpty();
            // the "Filtrat" sheet
            sheet.Workbook.Workbook.Sheets!.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>().Select(s => s.Name!.Value).ShouldContain("Filtrat");
        }
    }
}
