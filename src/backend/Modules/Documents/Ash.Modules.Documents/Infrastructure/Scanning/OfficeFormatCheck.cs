using System.IO.Compression;

namespace Ash.Modules.Documents.Infrastructure.Scanning;

/// <summary>
/// docx / xlsx / pptx are ZIP files: the magic bytes "PK\x03\x04" alone would let a renamed .jar (or any zip) through.
/// A real Office Open XML file has [Content_Types].xml and its application folder (word/, xl/, ppt/).
/// </summary>
internal static class OfficeFormatCheck
{
    public const string InvalidReason = "format i pavlefshëm";

    private static readonly Dictionary<string, string> Folders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["docx"] = "word/",
        ["xlsx"] = "xl/",
        ["pptx"] = "ppt/",
    };

    public static bool AppliesTo(string extension) => Folders.ContainsKey(extension);

    /// <summary>Reads only the zip's central directory (the stream must be seekable).</summary>
    public static bool IsValid(Stream content, string extension)
    {
        try
        {
            using var zip = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
            var names = zip.Entries.Select(e => e.FullName).ToList();
            return names.Contains("[Content_Types].xml")
                   && names.Any(n => n.StartsWith(Folders[extension], StringComparison.Ordinal));
        }
        catch (InvalidDataException)
        {
            return false; // not a zip at all behind the magic bytes
        }
    }
}
