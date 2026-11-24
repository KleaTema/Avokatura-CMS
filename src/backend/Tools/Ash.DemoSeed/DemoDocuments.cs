using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace Ash.DemoSeed;

/// <summary>Small, valid documents with real-looking Albanian text: PDFs (PDFsharp, as the report export) and one .docx.</summary>
internal static class DemoDocuments
{
    private const string Family = "DemoSans";

    public static byte[] Pdf(string title, IReadOnlyList<string> lines)
    {
        EnsureFonts();
        using var document = new PdfDocument();
        document.Info.Title = title;
        var page = document.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        using (var g = XGraphics.FromPdfPage(page))
        {
            var titleFont = new XFont(Family, 14, XFontStyleEx.Bold);
            var bodyFont = new XFont(Family, 10.5, XFontStyleEx.Regular);
            double y = 60;
            g.DrawString("REPUBLIKA E SHQIPËRISË", bodyFont, XBrushes.Black, new XPoint(60, y));
            y += 30;
            g.DrawString(title, titleFont, XBrushes.Black, new XPoint(60, y));
            y += 28;
            foreach (var line in lines.SelectMany(Wrap))
            {
                g.DrawString(line, bodyFont, XBrushes.Black, new XPoint(60, y));
                y += 16;
            }
        }

        using var output = new MemoryStream();
        document.Save(output, false);
        return output.ToArray();
    }

    public static byte[] Docx(string title, IReadOnlyList<string> paragraphs)
    {
        using var output = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(output, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var body = new Body();
            body.Append(new Paragraph(new Run(new RunProperties(new Bold()), new Text(title))));
            foreach (var p in paragraphs)
            {
                body.Append(new Paragraph(new Run(new Text(p) { Space = SpaceProcessingModeValues.Preserve })));
            }

            main.Document = new Document(body);
            main.Document.Save();
        }

        return output.ToArray();
    }

    private static IEnumerable<string> Wrap(string text)
    {
        const int width = 95;
        var line = string.Empty;
        foreach (var word in text.Split(' '))
        {
            if (line.Length + word.Length + 1 > width)
            {
                yield return line;
                line = word;
            }
            else
            {
                line = line.Length == 0 ? word : $"{line} {word}";
            }
        }

        yield return line;
    }

    /// <summary>
    /// PDFsharp 6 looks for no system font by itself. When the report export already registered its resolver in this
    /// process (it maps every family to a sans-serif face), that one is used; otherwise ours (Arial / Liberation / DejaVu).
    /// </summary>
    private static void EnsureFonts()
    {
        if (GlobalFontSettings.FontResolver is null)
        {
            GlobalFontSettings.FontResolver = new Resolver();
        }
    }

    private sealed class Resolver : IFontResolver
    {
        private static readonly string[][] Candidates =
        [
            [@"C:\Windows\Fonts\arial.ttf", @"C:\Windows\Fonts\arialbd.ttf"],
            ["/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf", "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf"],
            ["/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"],
        ];

        private readonly string[] _files = (Candidates.FirstOrDefault(set => File.Exists(set[0]))
                                            ?? throw new InvalidOperationException("No TrueType font for the demo PDFs."))
            .Select((f, i) => File.Exists(f) ? f : Candidates.First(set => File.Exists(set[0]))[0]).ToArray();

        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) => new($"{Family}#{(bold ? 1 : 0)}");

        public byte[]? GetFont(string faceName) =>
            File.ReadAllBytes(_files[int.Parse(faceName[(faceName.IndexOf('#') + 1)..], CultureInfo.InvariantCulture)]);
    }
}
