using System.Globalization;
using Ash.Modules.Reports.Application;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace Ash.Modules.Reports.Export;

/// <summary>
/// Task 053h: the .pdf export (MigraDoc / PDFsharp, MIT) — A4 landscape; a header with the report's name, its filters
/// and "Gjeneruar më … nga …"; the chosen columns in a table whose header repeats on every page; page numbers.
/// The widths follow the columns' Excel widths, scaled to the page. A PDF is for reading and printing, so it is capped
/// at <see cref="MaxRows"/> rows (more → 400 "Ngushtoni filtrat ose përdorni Excel/CSV").
/// </summary>
public static class PdfReportWriter
{
    public const string ContentType = "application/pdf";
    public const int MaxRows = 5_000;
    private const string Font = ReportPdfFonts.Family;

    public static async Task<int> WriteAsync(
        Stream output, IReadOnlyList<ReportColumn> columns, IAsyncEnumerable<IReportRow> rows, ExportInfo info,
        IReadOnlyList<ReportTotal>? totals, CancellationToken ct)
    {
        ReportPdfFonts.EnsureRegistered();

        var document = new Document();
        document.Info.Title = info.ReportName;
        document.Styles[StyleNames.Normal]!.Font.Name = Font;
        document.Styles[StyleNames.Normal]!.Font.Size = 7;

        var section = document.AddSection();
        section.PageSetup = document.DefaultPageSetup.Clone();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.Orientation = Orientation.Landscape;
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromCentimeter(1.2);
        section.PageSetup.TopMargin = section.PageSetup.BottomMargin = Unit.FromCentimeter(1.2);

        // the header: name, filters, generated at / by
        var title = section.AddParagraph(info.ReportName);
        title.Format.Font.Size = 12;
        title.Format.Font.Bold = true;
        title.Format.SpaceAfter = Unit.FromPoint(4);
        foreach (var filter in info.Filters)
        {
            var line = section.AddParagraph();
            line.AddFormattedText($"{filter.Label}: ", TextFormat.Bold);
            line.AddText(filter.Value);
        }

        var generated = section.AddParagraph(
            $"Gjeneruar më {TimeZoneInfo.ConvertTimeFromUtc(info.GeneratedAtUtc, Ash.SharedKernel.Time.TiraneTime.Zone):dd.MM.yyyy HH:mm} nga {info.GeneratedBy}");
        generated.Format.SpaceAfter = Unit.FromPoint(8);
        generated.Format.Font.Italic = true;

        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Alignment = ParagraphAlignment.Right;
        footer.AddText("Faqja ");
        footer.AddPageField();
        footer.AddText(" / ");
        footer.AddNumPagesField();

        // the table: widths from the Excel widths, scaled to the printable width
        var table = section.AddTable();
        table.Borders.Width = 0.25;
        table.Borders.Color = Colors.Gray;
        var printable = Unit.FromCentimeter(29.7 - 2.4).Point;
        var sum = columns.Sum(c => c.Width);
        foreach (var column in columns)
        {
            var c = table.AddColumn(Unit.FromPoint(printable * column.Width / sum));
            c.Format.Alignment = IsNumber(column) ? ParagraphAlignment.Right : ParagraphAlignment.Left;
        }

        var header = table.AddRow();
        header.HeadingFormat = true;
        header.Shading.Color = new Color(235, 240, 248);
        header.Format.Font.Bold = true;
        for (var i = 0; i < columns.Count; i++)
        {
            header.Cells[i].AddParagraph(columns[i].Header);
        }

        var count = 0;
        await foreach (var row in rows.WithCancellation(ct))
        {
            if (++count > MaxRows)
            {
                break;
            }

            var r = table.AddRow();
            for (var i = 0; i < columns.Count; i++)
            {
                r.Cells[i].AddParagraph(CsvReportWriter.Format(columns[i], row.Get(columns[i].Key)));
            }
        }

        // the totals row ("Gjithsej"): the count under the first column, each sum under its column (per currency)
        if (totals is not null)
        {
            var t = table.AddRow();
            t.Format.Font.Bold = true;
            t.Shading.Color = new Color(245, 245, 245);
            t.Cells[0].AddParagraph($"Gjithsej: {count.ToString("N0", CultureInfo.GetCultureInfo("sq-AL"))}");
            for (var i = 1; i < columns.Count; i++)
            {
                var sums = totals.Where(s => s.Key == columns[i].Key).ToList();
                if (sums.Count > 0)
                {
                    t.Cells[i].AddParagraph(string.Join("\n", sums.Select(s =>
                        $"{s.Value.ToString("N2", CultureInfo.GetCultureInfo("sq-AL"))}{(s.Currency is null ? string.Empty : " " + s.Currency)}")));
                }
            }
        }

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        renderer.PdfDocument.Save(output, false);
        return Math.Min(count, MaxRows);
    }

    private static bool IsNumber(ReportColumn column) => column.Kind is ReportColumnKind.Decimal or ReportColumnKind.Integer;
}

/// <summary>
/// PDFsharp 6 does not look for system fonts by itself: this resolver gives it a sans-serif TrueType font with the
/// Albanian letters — Arial on Windows, Liberation Sans or DejaVu Sans on Linux (the server image needs one of the
/// packages fonts-liberation / fonts-dejavu-core; see docs/architecture.md). Registered once per process.
/// </summary>
internal sealed class ReportPdfFonts : IFontResolver
{
    public const string Family = "ReportSans";

    private static readonly object Gate = new();
    private static bool _registered;

    private static readonly string[][] Candidates =
    [
        [@"C:\Windows\Fonts\arial.ttf", @"C:\Windows\Fonts\arialbd.ttf", @"C:\Windows\Fonts\ariali.ttf", @"C:\Windows\Fonts\arialbi.ttf"],
        ["/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf", "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf",
         "/usr/share/fonts/truetype/liberation/LiberationSans-Italic.ttf", "/usr/share/fonts/truetype/liberation/LiberationSans-BoldItalic.ttf"],
        ["/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
         "/usr/share/fonts/truetype/dejavu/DejaVuSans-Oblique.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans-BoldOblique.ttf"],
    ];

    private readonly string[] _files;

    private ReportPdfFonts(string[] files) => _files = files;

    public static void EnsureRegistered()
    {
        lock (Gate)
        {
            if (_registered)
            {
                return;
            }

            var files = Candidates.FirstOrDefault(set => File.Exists(set[0]))
                        ?? throw new InvalidOperationException(
                            "No TrueType font for the PDF export: install fonts-liberation or fonts-dejavu-core.");
            // a missing bold / italic face falls back to the regular one
            GlobalFontSettings.FontResolver = new ReportPdfFonts(files.Select(f => File.Exists(f) ? f : files[0]).ToArray());
            _registered = true;
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
        new($"{Family}#{(bold ? 1 : 0) + (italic ? 2 : 0)}");

    public byte[]? GetFont(string faceName) =>
        File.ReadAllBytes(_files[int.Parse(faceName[(faceName.IndexOf('#') + 1)..], CultureInfo.InvariantCulture)]);
}
