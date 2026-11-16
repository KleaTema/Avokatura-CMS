using System.Globalization;

namespace Ash.Api.Security;

/// <summary>
/// Task 052: the runtime must know the Albanian time zone and culture — the dates of hearings, the statistics' year
/// bounds (Europe/Tirane) and the reports' number formats (sq-AL) depend on them. A container image without ICU or
/// tzdata (an "invariant globalization" build) would compute wrong days silently; this refuses to start instead.
/// </summary>
public static class RuntimeSelfCheck
{
    public static void Assert()
    {
        var problems = new List<string>();
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Tirane");
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            problems.Add("the time zone Europe/Tirane is not available (install tzdata)");
        }

        try
        {
            var albanian = CultureInfo.GetCultureInfo("sq-AL", predefinedOnly: true);
            // ICU present: the Albanian month names exist (invariant mode gives the English / empty ones)
            if (albanian.DateTimeFormat.GetMonthName(1) is not { Length: > 0 } january || january.Equals("January", StringComparison.Ordinal))
            {
                problems.Add("the culture sq-AL has no Albanian data (install ICU)");
            }
        }
        catch (CultureNotFoundException)
        {
            // also what invariant globalization (DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true) gives
            problems.Add("the culture sq-AL is not available (install ICU, DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false)");
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException("Runtime self-check failed: " + string.Join("; ", problems) + ".");
        }
    }
}
