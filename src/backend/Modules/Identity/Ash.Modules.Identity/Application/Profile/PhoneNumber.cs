using System.Text.RegularExpressions;

namespace Ash.Modules.Identity.Application.Profile;

/// <summary>
/// The profile phone (task 048), stored in E.164. Albanian numbers: a mobile as 06X XXX XXXX or +355 6X XXX XXXX, a
/// landline as +355 4XX XXXX; another country with a leading + (or 00). Spaces, dashes, dots and brackets are ignored.
/// </summary>
public static partial class PhoneNumber
{
    [GeneratedRegex(@"^06\d{8}$")]
    private static partial Regex NationalMobile();

    [GeneratedRegex(@"^\+355(6\d{8}|4\d{7})$")]
    private static partial Regex Albanian();

    [GeneratedRegex(@"^\+[1-9]\d{6,14}$")]
    private static partial Regex E164();

    [GeneratedRegex(@"[\s\-\.\(\)]")]
    private static partial Regex Separators();

    /// <summary>The E.164 form; null when it is not a valid number.</summary>
    public static string? Normalize(string input)
    {
        var s = Separators().Replace(input.Trim(), string.Empty);
        if (s.StartsWith("00", StringComparison.Ordinal))
        {
            s = "+" + s[2..];
        }

        if (NationalMobile().IsMatch(s))
        {
            return "+355" + s[1..];
        }

        if (s.StartsWith("+355", StringComparison.Ordinal))
        {
            return Albanian().IsMatch(s) ? s : null;
        }

        return E164().IsMatch(s) ? s : null;
    }

    /// <summary>For the audit log: every digit but the last 3 as *, e.g. +355*******567.</summary>
    public static string? Mask(string? phone)
    {
        if (phone is null)
        {
            return null;
        }

        var keepFrom = phone.Length - 3;
        return string.Concat(phone.Select((ch, i) => i < keepFrom && char.IsDigit(ch) ? '*' : ch));
    }
}
