using System.Globalization;
using System.Text.RegularExpressions;

namespace Ash.Modules.Cases.Application;

/// <summary>
/// Money and m² travel as STRINGS ("1250000.50"), never as JSON numbers or doubles: parsed into decimal, checked
/// against the column's DECIMAL(p,2), and written back with exactly two decimals (invariant culture).
/// </summary>
public static partial class DecimalText
{
    /// <summary>DECIMAL(18,2): up to 16 digits before the point.</summary>
    public const int MoneyIntegerDigits = 16;

    /// <summary>DECIMAL(14,2): up to 12 digits before the point.</summary>
    public const int AreaIntegerDigits = 12;

    /// <summary>
    /// True and the value for "0", "1250000", "1250000.5", "1250000.50"; false for a sign, more than 2 decimals,
    /// too many digits, exponents, grouping or a comma.
    /// </summary>
    public static bool TryParse(string text, int integerDigits, out decimal value)
    {
        value = 0;
        var match = Shape().Match(text);
        if (!match.Success || match.Groups["int"].Value.TrimStart('0').Length > integerDigits)
        {
            return false;
        }

        return decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }

    public static string? Format(decimal? value) => value?.ToString("0.00", CultureInfo.InvariantCulture);

    // [0-9], not \d: \d also matches other scripts' digits
    [GeneratedRegex(@"^(?<int>[0-9]+)(\.[0-9]{1,2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();
}
