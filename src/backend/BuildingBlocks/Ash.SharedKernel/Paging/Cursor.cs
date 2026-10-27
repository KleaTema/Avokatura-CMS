using System.Buffers.Text;
using System.Text;

namespace Ash.SharedKernel.Paging;

/// <summary>Opaque keyset cursor: URL-safe base64 of "sortKey|id".</summary>
public static class Cursor
{
    private const char Separator = '|';

    public static string Encode(string sortKey, long id)
    {
        var raw = Encoding.UTF8.GetBytes($"{sortKey}{Separator}{id}");
        return Base64Url.EncodeToString(raw);
    }

    public static bool TryDecode(string? cursor, out string sortKey, out long id)
    {
        sortKey = string.Empty;
        id = 0;

        if (string.IsNullOrWhiteSpace(cursor))
        {
            return false;
        }

        try
        {
            var text = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
            var index = text.LastIndexOf(Separator);
            if (index < 0 || !long.TryParse(text.AsSpan(index + 1), out id))
            {
                id = 0;
                return false;
            }

            sortKey = text[..index];
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
