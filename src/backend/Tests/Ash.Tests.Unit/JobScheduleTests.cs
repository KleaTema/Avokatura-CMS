using Ash.Infrastructure.Jobs;
using Shouldly;

namespace Ash.Tests.Unit;

/// <summary>Task 041: when the jobs run — Tirane wall clock, across both DST changes of 2026.</summary>
public class JobScheduleTests
{
    private static DateTime Utc(int y, int m, int d, int h, int min = 0) => new(y, m, d, h, min, 0, DateTimeKind.Utc);

    [Fact]
    public void Daily_at_02_00_runs_at_02_00_Tirane_in_winter_and_summer()
    {
        var schedule = (DailyTiraneSchedule)JobSchedule.DailyAtTirane(2, 0);

        schedule.OccurrenceOn(new DateOnly(2026, 1, 15)).ShouldBe(Utc(2026, 1, 15, 1)); // CET, +1
        schedule.OccurrenceOn(new DateOnly(2026, 7, 15)).ShouldBe(Utc(2026, 7, 15, 0)); // CEST, +2
        schedule.NextAfter(Utc(2026, 1, 15, 1)).ShouldBe(Utc(2026, 1, 16, 1)); // strictly after
        schedule.NextAfter(Utc(2026, 1, 15, 0, 59)).ShouldBe(Utc(2026, 1, 15, 1));
    }

    [Fact]
    public void On_29_March_2026_a_time_that_does_not_exist_runs_at_the_first_valid_minute_after()
    {
        // clocks go from 02:00 CET straight to 03:00 CEST: 02:00 and 02:30 do not exist that night
        var at0200 = (DailyTiraneSchedule)JobSchedule.DailyAtTirane(2, 0);
        var at0230 = (DailyTiraneSchedule)JobSchedule.DailyAtTirane(2, 30);

        at0200.OccurrenceOn(new DateOnly(2026, 3, 29)).ShouldBe(Utc(2026, 3, 29, 1)); // 03:00 CEST
        at0230.OccurrenceOn(new DateOnly(2026, 3, 29)).ShouldBe(Utc(2026, 3, 29, 1)); // 03:00 CEST
        // once that day: the day before at 02:00 CET, the day after at 02:00 CEST
        at0200.NextAfter(Utc(2026, 3, 28, 12)).ShouldBe(Utc(2026, 3, 29, 1));
        at0200.NextAfter(Utc(2026, 3, 29, 1)).ShouldBe(Utc(2026, 3, 30, 0));
        at0200.OccurrenceOn(new DateOnly(2026, 3, 28)).ShouldBe(Utc(2026, 3, 28, 1));
    }

    [Fact]
    public void On_25_October_2026_a_time_that_happens_twice_runs_once_at_its_first_occurrence()
    {
        // clocks go back from 03:00 CEST to 02:00 CET: 02:00–02:59 happens twice
        var at0200 = (DailyTiraneSchedule)JobSchedule.DailyAtTirane(2, 0);
        var at0230 = (DailyTiraneSchedule)JobSchedule.DailyAtTirane(2, 30);

        at0200.OccurrenceOn(new DateOnly(2026, 10, 25)).ShouldBe(Utc(2026, 10, 25, 0)); // 02:00 CEST, not 01:00 UTC
        at0230.OccurrenceOn(new DateOnly(2026, 10, 25)).ShouldBe(Utc(2026, 10, 25, 0, 30));
        // after the first 02:00 the next run is the next day — never the repeated 02:00 an hour later
        at0200.NextAfter(Utc(2026, 10, 25, 0)).ShouldBe(Utc(2026, 10, 26, 1));
        at0200.NextAfter(Utc(2026, 10, 24, 12)).ShouldBe(Utc(2026, 10, 25, 0));
    }

    [Fact]
    public void Latest_at_or_before_finds_the_run_a_restarted_scheduler_has_missed()
    {
        var daily = JobSchedule.DailyAtTirane(2, 0);
        daily.LatestAtOrBefore(Utc(2026, 11, 4, 9)).ShouldBe(Utc(2026, 11, 4, 1));
        daily.LatestAtOrBefore(Utc(2026, 11, 4, 0, 59)).ShouldBe(Utc(2026, 11, 3, 1));
        daily.LatestAtOrBefore(Utc(2026, 11, 4, 1)).ShouldBe(Utc(2026, 11, 4, 1));

        var hourly = JobSchedule.Hourly;
        hourly.LatestAtOrBefore(Utc(2026, 11, 4, 9, 42)).ShouldBe(Utc(2026, 11, 4, 9));
        hourly.NextAfter(Utc(2026, 11, 4, 9)).ShouldBe(Utc(2026, 11, 4, 10));
        hourly.Interval.ShouldBe(TimeSpan.FromHours(1));
        daily.Interval.ShouldBe(TimeSpan.FromDays(1));
    }
}
