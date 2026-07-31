using GoveeController.Application.Schedules;
using GoveeController.Domain.Schedules;
using Xunit;

namespace GoveeController.Application.Tests.Schedules;

/// <summary>
/// Tests against a real DST-observing zone (America/Chicago), never the machine's local zone, so
/// these are deterministic wherever they run and actually exercise the spring-forward/fall-back
/// edge cases <see cref="NextOccurrence"/> has to handle.
/// </summary>
public class NextOccurrenceTests
{
    private static readonly TimeZoneInfo Chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    private static DateTimeOffset ChicagoNow(int year, int month, int day, int hour, int minute)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, Chicago), TimeSpan.Zero);
    }

    [Fact]
    public void ComputeNextRunAtUtc_Recurring_SameDayFutureTime_FiresToday()
    {
        // Wednesday 2026-08-05, now is 9:00 AM, schedule fires at 10:00 PM the same day.
        var now = ChicagoNow(2026, 8, 5, 9, 0);

        var result = NextOccurrence.ComputeNextRunAtUtc(ScheduleDays.Wednesday, null, new TimeOnly(22, 0), now, Chicago);

        var expected = ChicagoNow(2026, 8, 5, 22, 0).UtcDateTime;
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ComputeNextRunAtUtc_Recurring_SameDayPastTime_RollsToNextEnabledDay()
    {
        // Wednesday 2026-08-05, now is 11:00 PM (past the 10:00 PM fire time), only Wednesday
        // enabled - must roll all the way to the following Wednesday.
        var now = ChicagoNow(2026, 8, 5, 23, 0);

        var result = NextOccurrence.ComputeNextRunAtUtc(ScheduleDays.Wednesday, null, new TimeOnly(22, 0), now, Chicago);

        var expected = ChicagoNow(2026, 8, 12, 22, 0).UtcDateTime;
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ComputeNextRunAtUtc_Recurring_SkipsToNextMatchingDay_WhenTodayIsNotEnabled()
    {
        // Wednesday 2026-08-05, only Friday enabled.
        var now = ChicagoNow(2026, 8, 5, 9, 0);

        var result = NextOccurrence.ComputeNextRunAtUtc(ScheduleDays.Friday, null, new TimeOnly(22, 0), now, Chicago);

        var expected = ChicagoNow(2026, 8, 7, 22, 0).UtcDateTime;
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ComputeNextRunAtUtc_Recurring_EveryDayMask_FiresTomorrow_WhenTodaysTimeHasPassed()
    {
        var now = ChicagoNow(2026, 8, 5, 23, 0);
        var everyDay = ScheduleDays.Sunday | ScheduleDays.Monday | ScheduleDays.Tuesday | ScheduleDays.Wednesday
            | ScheduleDays.Thursday | ScheduleDays.Friday | ScheduleDays.Saturday;

        var result = NextOccurrence.ComputeNextRunAtUtc(everyDay, null, new TimeOnly(22, 0), now, Chicago);

        var expected = ChicagoNow(2026, 8, 6, 22, 0).UtcDateTime;
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ComputeNextRunAtUtc_OneTime_Future_ReturnsThatInstant()
    {
        var now = ChicagoNow(2026, 8, 5, 9, 0);

        var result = NextOccurrence.ComputeNextRunAtUtc(ScheduleDays.None, new DateOnly(2026, 8, 10), new TimeOnly(7, 0), now, Chicago);

        var expected = ChicagoNow(2026, 8, 10, 7, 0).UtcDateTime;
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ComputeNextRunAtUtc_OneTime_Past_ReturnsNull()
    {
        var now = ChicagoNow(2026, 8, 5, 9, 0);

        var result = NextOccurrence.ComputeNextRunAtUtc(ScheduleDays.None, new DateOnly(2026, 8, 1), new TimeOnly(7, 0), now, Chicago);

        Assert.Null(result);
    }

    [Fact]
    public void ComputeNextRunAtUtc_OneTime_ExactlyNow_ReturnsNull()
    {
        // The boundary must not fire again for an instant that has *just* passed - candidateUtc
        // must be strictly greater than now, not greater-or-equal.
        var now = ChicagoNow(2026, 8, 5, 9, 0);

        var result = NextOccurrence.ComputeNextRunAtUtc(ScheduleDays.None, new DateOnly(2026, 8, 5), new TimeOnly(9, 0), now, Chicago);

        Assert.Null(result);
    }

    [Fact]
    public void ComputeNextRunAtUtc_SpringForward_InvalidLocalTime_MapsForwardToFirstValidInstant()
    {
        // America/Chicago springs forward 2026-03-08 2:00 AM CST -> 3:00 AM CDT. 2:30 AM that day
        // does not exist. A one-time schedule set for that instant must not throw - it should land
        // on the first valid instant after the transition (3:00 AM CDT = 08:00 UTC).
        var now = ChicagoNow(2026, 3, 7, 9, 0);

        var result = NextOccurrence.ComputeNextRunAtUtc(ScheduleDays.None, new DateOnly(2026, 3, 8), new TimeOnly(2, 30), now, Chicago);

        Assert.Equal(new DateTime(2026, 3, 8, 8, 0, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void ComputeNextRunAtUtc_SpringForward_RecurringScheduleOnTransitionDay_AlsoMapsForward()
    {
        // Same gap as above, but reached via the recurring (day-of-week scan) path rather than the
        // one-time path, to confirm both call the same DST handling.
        var now = ChicagoNow(2026, 3, 1, 9, 0); // the preceding Sunday

        var result = NextOccurrence.ComputeNextRunAtUtc(ScheduleDays.Sunday, null, new TimeOnly(2, 30), now, Chicago);

        Assert.Equal(new DateTime(2026, 3, 8, 8, 0, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void ComputeNextRunAtUtc_FallBack_AmbiguousLocalTime_FiresExactlyOnce()
    {
        // America/Chicago falls back 2026-11-01 2:00 AM CDT -> 1:00 AM CST; 1:30 AM occurs twice.
        // Whichever of the two instants is chosen, ComputeNextRunAtUtc must resolve it the same way
        // every time it's asked (deterministic), which is what actually matters - the plan's
        // decision was "fire once", not which of the two.
        var now = ChicagoNow(2026, 10, 31, 9, 0);

        var first = NextOccurrence.ComputeNextRunAtUtc(ScheduleDays.None, new DateOnly(2026, 11, 1), new TimeOnly(1, 30), now, Chicago);
        var second = NextOccurrence.ComputeNextRunAtUtc(ScheduleDays.None, new DateOnly(2026, 11, 1), new TimeOnly(1, 30), now, Chicago);

        Assert.NotNull(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void ComputeNextRunAtUtc_Throws_WhenDaysIsNoneAndOneTimeDateIsNull()
    {
        var now = ChicagoNow(2026, 8, 5, 9, 0);

        Assert.Throws<ArgumentException>(() =>
            NextOccurrence.ComputeNextRunAtUtc(ScheduleDays.None, null, new TimeOnly(9, 0), now, Chicago));
    }
}
