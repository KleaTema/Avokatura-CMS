using System.Globalization;
using System.Text;

namespace Ash.Infrastructure.Storage;

/// <summary>
/// "attachment; filename=\"…\"; filename*=UTF-8''…" (RFC 6266 with the RFC 5987 encoding): browsers use filename*,
/// so Albanian letters (ë, ç) survive; filename= is an ASCII fallback for old clients (ë → e, ç → c, other characters
/// → "_"). Control characters, quotes and path separators never reach the header.
/// </summary>
public static class ContentDisposition
{
    public static string Attachment(string fileName)
    {
        var name = Clean(fileName);
        return $"attachment; filename=\"{AsciiFallback(name)}\"; filename*=UTF-8''{Uri.EscapeDataString(name)}";
    }

    /// <summary>The name without control characters or path parts ("..\a/b.pdf" → "b.pdf"); "file" when nothing remains.</summary>
    private static string Clean(string fileName)
    {
        var name = fileName.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return name.Length == 0 ? "file" : name;
    }

    private static string AsciiFallback(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var c in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue; // the accent of a decomposed letter: ë = e + ¨
            }

            builder.Append(c is >= ' ' and <= '~' && c != '"' && c != '\\' ? c : '_');
        }

        return builder.ToString();
    }
}
