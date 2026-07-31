using GoveeController.Domain.Schedules;

namespace GoveeController.Application.Schedules;

/// <summary>
/// Use-case service for managing <see cref="Schedule"/> rules and firing the ones that are due.
/// </summary>
public interface IScheduleService
{
    /// <summary>Lists all saved schedules, newest first.</summary>
    Task<IReadOnlyList<Schedule>> ListSchedulesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates and persists a new schedule. Exactly one of <paramref name="daysOfWeek"/> (recurring)
    /// or <paramref name="oneTimeDateLocal"/> (one-time) must be set - <paramref name="daysOfWeek"/>
    /// non-<see cref="ScheduleDays.None"/> with a non-null <paramref name="oneTimeDateLocal"/>, or
    /// <see cref="ScheduleDays.None"/> with a null <paramref name="oneTimeDateLocal"/>, both throw
    /// <see cref="ArgumentException"/>. A one-time schedule's date and time must be in the future.
    /// Throws <see cref="KeyNotFoundException"/> if <paramref name="shortcutId"/> does not exist.
    /// </summary>
    Task<Schedule> CreateScheduleAsync(
        int shortcutId,
        ScheduleDays daysOfWeek,
        DateOnly? oneTimeDateLocal,
        TimeOnly timeOfDayLocal,
        bool isEnabled,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Overwrites an existing schedule's shortcut, timing, and enabled state. Same validation rules
    /// as <see cref="CreateScheduleAsync"/>. Throws <see cref="KeyNotFoundException"/> if the
    /// schedule (or the target shortcut) does not exist.
    /// </summary>
    Task UpdateScheduleAsync(
        int id,
        int shortcutId,
        ScheduleDays daysOfWeek,
        DateOnly? oneTimeDateLocal,
        TimeOnly timeOfDayLocal,
        bool isEnabled,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables or disables a schedule. Disabling clears <see cref="Schedule.NextRunAtUtc"/> to null;
    /// enabling recomputes it from the current instant, so a schedule left disabled for a long time
    /// does not immediately fire a stale past occurrence the moment it's re-enabled. Throws
    /// <see cref="KeyNotFoundException"/> if the schedule does not exist.
    /// </summary>
    Task SetEnabledAsync(int id, bool isEnabled, CancellationToken cancellationToken = default);

    /// <summary>Deletes a schedule by id.</summary>
    Task DeleteScheduleAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs every schedule whose <see cref="Schedule.NextRunAtUtc"/> is due. A schedule found within
    /// the grace window of its due instant is applied (via
    /// <see cref="Shortcuts.IShortcutService.ApplyShortcutAsync"/>) and then advanced: deleted if
    /// one-time, or has its next occurrence recomputed if recurring. A schedule found overdue beyond
    /// the grace window is treated as missed instead - skipped without applying, then advanced the
    /// same way. Due schedules are run sequentially, not in parallel, to stay within the shared
    /// Govee rate-limit budget. Called by the background runner on a fixed interval - not intended
    /// to be called from the UI.
    /// </summary>
    Task RunDueSchedulesAsync(CancellationToken cancellationToken = default);
}
