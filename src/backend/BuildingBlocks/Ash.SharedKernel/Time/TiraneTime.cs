namespace Ash.SharedKernel.Time;

/// <summary>Albania's calendar: "today", and the local date of a UTC instant, in Europe/Tirane.</summary>
public static class TiraneTime
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Tirane");

    public static DateOnly Today(IClock clock) => DateOf(clock.UtcNow);

    public static DateOnly DateOf(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone));

    /// <summary>The UTC instant of 00:00 in Tirane on that calendar date (23:00 or 22:00 UTC the day before).</summary>
    public static DateTime StartOfDayUtc(DateOnly date) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), Zone);
}
