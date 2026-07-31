using GoveeController.Domain.Schedules;

namespace GoveeController.Application.Schedules;

/// <summary>
/// Pure calculation of a <see cref="Schedule"/>'s next due UTC instant from its local wall-clock
/// configuration. Kept as a static class with no dependencies (not even <see cref="TimeProvider"/>
/// - the caller passes the current instant in) so it's trivially unit-testable against arbitrary
/// clocks and time zones without any test doubles.
/// </summary>
public static class NextOccurrence
{
    // Recurring schedules scan at most one full week ahead. 8 (not 7) covers the edge case where
    // today's own day is enabled but its time has already passed today - the scan has to walk all
    // the way around to today next week, which is 7 days after the day *after* today, i.e. offset 7
    // from today. Kept as a loop bound rather than assumed correct - if this is ever exceeded it's a
    // bug, not a legitimate schedule, so ComputeNextRunAtUtc throws rather than returning a wrong answer.
    private const int MaxDaysToScan = 8;

    /// <summary>
    /// Computes the next UTC instant a schedule is due to fire, or null if there is nothing left to
    /// run (only possible for a one-time schedule whose date and time are already in the past).
    /// </summary>
    /// <param name="days">
    /// The recurring days mask, or <see cref="ScheduleDays.None"/> for a one-time schedule (in
    /// which case <paramref name="oneTimeDateLocal"/> must be set).
    /// </param>
    /// <param name="oneTimeDateLocal">The one-time schedule's date, or null for a recurring schedule.</param>
    /// <param name="timeOfDayLocal">Local wall-clock time of day, used in both modes.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <param name="localTimeZone">
    /// The time zone <paramref name="timeOfDayLocal"/> (and <paramref name="oneTimeDateLocal"/>) are
    /// expressed in - the container's <c>TZ</c> in production, an explicit zone in tests.
    /// </param>
    public static DateTime? ComputeNextRunAtUtc(
        ScheduleDays days,
        DateOnly? oneTimeDateLocal,
        TimeOnly timeOfDayLocal,
        DateTimeOffset nowUtc,
        TimeZoneInfo localTimeZone)
    {
        if (days == ScheduleDays.None)
        {
            if (oneTimeDateLocal is not { } date)
            {
                throw new ArgumentException(
                    "A schedule must specify either a days-of-week mask or a one-time date.",
                    nameof(oneTimeDateLocal));
            }

            var candidateUtc = ToUtc(date.ToDateTime(timeOfDayLocal), localTimeZone);
            return candidateUtc > nowUtc ? candidateUtc.UtcDateTime : null;
        }

        var nowLocalDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, localTimeZone).DateTime);
        for (var offset = 0; offset < MaxDaysToScan; offset++)
        {
            var candidateDate = nowLocalDate.AddDays(offset);
            if (!days.HasFlag(ToScheduleDays(candidateDate.DayOfWeek)))
            {
                continue;
            }

            var candidateUtc = ToUtc(candidateDate.ToDateTime(timeOfDayLocal), localTimeZone);
            if (candidateUtc > nowUtc)
            {
                return candidateUtc.UtcDateTime;
            }
        }

        // Unreachable for any valid recurring schedule: scanning a full week from today always
        // finds an enabled day whose time is still ahead, because "today, same time, next week" is
        // always in the future. Reaching here means days/timeOfDayLocal were corrupted somehow -
        // fail loudly rather than silently stop firing a schedule.
        throw new InvalidOperationException(
            $"Could not find a next occurrence for mask {days} within {MaxDaysToScan} days - this indicates corrupted schedule data.");
    }

    /// <summary>
    /// Converts a schedule's local wall-clock instant to UTC, resolving the two DST edge cases
    /// deliberately rather than letting them throw or silently misbehave (see
    /// SCHEDULED-SHORTCUTS-PLAN.md §3.1). Spring-forward (the local time falls inside the skipped
    /// hour and doesn't exist) is nudged forward minute-by-minute to the first valid instant after
    /// the transition - simpler and more robust than computing the exact gap size from the zone's
    /// adjustment rules, and this only ever runs for the handful of schedules whose time lands in
    /// that hour on the day of a transition. Fall-back (the local time occurs twice) is left to
    /// <see cref="TimeZoneInfo.ConvertTimeToUtc(DateTime, TimeZoneInfo)"/>'s own default resolution
    /// for an unspecified-kind DateTime, which is deterministic - verified empirically that .NET
    /// resolves it as standard time, i.e. the later of the two UTC instants, not "the first
    /// occurrence" as originally assumed when this feature was planned. Either choice satisfies the
    /// actual requirement (fire exactly once, not twice); which one was never a product decision.
    /// </summary>
    private static DateTimeOffset ToUtc(DateTime local, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(unspecified))
        {
            unspecified = unspecified.AddMinutes(1);
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(unspecified, zone), TimeSpan.Zero);
    }

    private static ScheduleDays ToScheduleDays(DayOfWeek day) => (ScheduleDays)(1 << (int)day);
}
