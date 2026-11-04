using Ash.SharedKernel.Time;

namespace Ash.Infrastructure.Jobs;

/// <summary>
/// When a job runs (task 041): every hour, or once a day at a wall-clock time in Europe/Tirane. All values are UTC
/// instants; only the daily one depends on the time zone.
/// </summary>
public abstract record JobSchedule
{
    public static JobSchedule Hourly { get; } = new HourlySchedule();

    public static JobSchedule DailyAtTirane(int hour, int minute) => new DailyTiraneSchedule(new TimeOnly(hour, minute));

    /// <summary>The normal gap between two runs (the health check allows twice this plus a grace).</summary>
    public abstract TimeSpan Interval { get; }

    /// <summary>The first scheduled instant strictly after <paramref name="afterUtc"/>.</summary>
    public abstract DateTime NextAfter(DateTime afterUtc);

    /// <summary>The latest scheduled instant at or before <paramref name="nowUtc"/> (a run missed since then is due).</summary>
    public abstract DateTime LatestAtOrBefore(DateTime nowUtc);
}

/// <summary>At the top of every hour (Tirane's offsets are whole hours, so it is the top of the local hour too).</summary>
public sealed record HourlySchedule : JobSchedule
{
    public override TimeSpan Interval => TimeSpan.FromHours(1);

    public override DateTime NextAfter(DateTime afterUtc) => TopOfHour(afterUtc).AddHours(1);

    public override DateTime LatestAtOrBefore(DateTime nowUtc) => TopOfHour(nowUtc);

    private static DateTime TopOfHour(DateTime utc) =>
        new(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc);
}

/// <summary>
/// Once a day at <see cref="At"/> Tirane time. On the night clocks go forward (last Sunday of March) a time that does
/// not exist runs at the first valid minute after it (02:30 → 03:00); on the night they go back (last Sunday of
/// October) a time that happens twice runs once, at its first occurrence (summer time).
/// </summary>
public sealed record DailyTiraneSchedule(TimeOnly At) : JobSchedule
{
    public override TimeSpan Interval => TimeSpan.FromDays(1);

    /// <summary>The UTC instant of this schedule on a Tirane calendar date.</summary>
    public DateTime OccurrenceOn(DateOnly date)
    {
        var zone = TiraneTime.Zone;
        var local = date.ToDateTime(At, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        if (zone.IsAmbiguousTime(local))
        {
            // the larger offset is summer time, i.e. the earlier of the two instants
            var offset = zone.GetAmbiguousTimeOffsets(local).Max();
            return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    public override DateTime NextAfter(DateTime afterUtc)
    {
        var date = TiraneTime.DateOf(afterUtc).AddDays(-1);
        while (true)
        {
            var at = OccurrenceOn(date);
            if (at > afterUtc)
            {
                return at;
            }

            date = date.AddDays(1);
        }
    }

    public override DateTime LatestAtOrBefore(DateTime nowUtc)
    {
        var date = TiraneTime.DateOf(nowUtc).AddDays(1);
        while (true)
        {
            var at = OccurrenceOn(date);
            if (at <= nowUtc)
            {
                return at;
            }

            date = date.AddDays(-1);
        }
    }
}
