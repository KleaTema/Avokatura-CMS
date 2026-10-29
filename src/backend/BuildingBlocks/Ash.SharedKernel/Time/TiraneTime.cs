namespace Ash.SharedKernel.Time;

/// <summary>Albania's calendar: "today", and the local date of a UTC instant, in Europe/Tirane.</summary>
public static class TiraneTime
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Tirane");

    public static DateOnly Today(IClock clock) => DateOf(clock.UtcNow);

    public static DateOnly DateOf(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone));
}
