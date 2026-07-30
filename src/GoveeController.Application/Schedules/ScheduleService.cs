using GoveeController.Application.Shortcuts;
using GoveeController.Domain.Schedules;
using Microsoft.Extensions.Logging;

namespace GoveeController.Application.Schedules;

/// <inheritdoc cref="IScheduleService" />
public sealed class ScheduleService : IScheduleService
{
    private readonly IScheduleRepository _repository;
    private readonly IShortcutService _shortcutService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ScheduleService> _logger;

    // How late a due schedule can be found and still fire, e.g. because the app was restarting
    // right at its due instant. Beyond this, the occurrence is treated as missed and skipped
    // rather than fired late - see SCHEDULED-SHORTCUTS-PLAN.md §2 for why this value was chosen.
    private static readonly TimeSpan GraceWindow = TimeSpan.FromMinutes(5);

    /// <summary>Creates the service.</summary>
    public ScheduleService(
        IScheduleRepository repository,
        IShortcutService shortcutService,
        TimeProvider timeProvider,
        ILogger<ScheduleService> logger)
    {
        _repository = repository;
        _shortcutService = shortcutService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Schedule>> ListSchedulesAsync(CancellationToken cancellationToken = default) =>
        _repository.GetAllAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<Schedule> CreateScheduleAsync(
        int shortcutId,
        ScheduleDays daysOfWeek,
        DateOnly? oneTimeDateLocal,
        TimeOnly timeOfDayLocal,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        ValidateMode(daysOfWeek, oneTimeDateLocal);
        await EnsureShortcutExistsAsync(shortcutId, cancellationToken).ConfigureAwait(false);

        var nowUtc = _timeProvider.GetUtcNow();
        var nextRunAtUtc = ComputeNextRunOrThrow(daysOfWeek, oneTimeDateLocal, timeOfDayLocal, nowUtc);

        var schedule = new Schedule
        {
            ShortcutId = shortcutId,
            DaysOfWeekMask = daysOfWeek,
            OneTimeDateLocal = oneTimeDateLocal,
            TimeOfDayLocal = timeOfDayLocal,
            IsEnabled = isEnabled,
            CreatedAtUtc = nowUtc.UtcDateTime,
            NextRunAtUtc = isEnabled ? nextRunAtUtc : null
        };

        return await _repository.AddAsync(schedule, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateScheduleAsync(
        int id,
        int shortcutId,
        ScheduleDays daysOfWeek,
        DateOnly? oneTimeDateLocal,
        TimeOnly timeOfDayLocal,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        ValidateMode(daysOfWeek, oneTimeDateLocal);
        await EnsureShortcutExistsAsync(shortcutId, cancellationToken).ConfigureAwait(false);

        var nowUtc = _timeProvider.GetUtcNow();
        var nextRunAtUtc = ComputeNextRunOrThrow(daysOfWeek, oneTimeDateLocal, timeOfDayLocal, nowUtc);

        var schedule = new Schedule
        {
            Id = id,
            ShortcutId = shortcutId,
            DaysOfWeekMask = daysOfWeek,
            OneTimeDateLocal = oneTimeDateLocal,
            TimeOfDayLocal = timeOfDayLocal,
            IsEnabled = isEnabled,
            NextRunAtUtc = isEnabled ? nextRunAtUtc : null
        };

        await _repository.UpdateAsync(schedule, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetEnabledAsync(int id, bool isEnabled, CancellationToken cancellationToken = default)
    {
        var existing = await _repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"No schedule with id {id} exists.");

        existing.IsEnabled = isEnabled;
        // Recomputed from *now*, not left at whatever stale value it had before being disabled -
        // otherwise a schedule disabled for a week would immediately fire the moment it's
        // re-enabled, per SCHEDULED-SHORTCUTS-PLAN.md §3.3.
        existing.NextRunAtUtc = isEnabled
            ? NextOccurrence.ComputeNextRunAtUtc(
                existing.DaysOfWeekMask, existing.OneTimeDateLocal, existing.TimeOfDayLocal,
                _timeProvider.GetUtcNow(), _timeProvider.LocalTimeZone)
            : null;

        await _repository.UpdateAsync(existing, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task DeleteScheduleAsync(int id, CancellationToken cancellationToken = default) =>
        _repository.DeleteAsync(id, cancellationToken);

    /// <inheritdoc />
    public async Task RunDueSchedulesAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = _timeProvider.GetUtcNow();
        var all = await _repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        // Compare via .UtcDateTime (DateTime <= DateTime) rather than "next <= nowUtc" directly -
        // see the comment on the same pattern in RunOneAsync for why the DateTime/DateTimeOffset
        // implicit conversion is not safe to rely on here.
        var due = all.Where(s => s.IsEnabled && s.NextRunAtUtc is { } next && next <= nowUtc.UtcDateTime).ToList();

        // Sequential, not parallel - see SCHEDULED-SHORTCUTS-PLAN.md §3.5. Multiple schedules
        // landing on the same tick is rare, and running them one after another keeps this within
        // the same shared Govee rate-limit budget that a single manual Apply already respects.
        foreach (var schedule in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RunOneAsync(schedule, nowUtc, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void ValidateMode(ScheduleDays daysOfWeek, DateOnly? oneTimeDateLocal)
    {
        var isRecurring = daysOfWeek != ScheduleDays.None;
        var isOneTime = oneTimeDateLocal is not null;
        if (isRecurring == isOneTime)
        {
            throw new ArgumentException(
                "A schedule must be either recurring (at least one day of the week) or one-time (a specific date), not both or neither.",
                nameof(daysOfWeek));
        }
    }

    private async Task EnsureShortcutExistsAsync(int shortcutId, CancellationToken cancellationToken)
    {
        var shortcuts = await _shortcutService.ListShortcutsAsync(cancellationToken).ConfigureAwait(false);
        if (!shortcuts.Any(s => s.Id == shortcutId))
        {
            throw new KeyNotFoundException($"No shortcut with id {shortcutId} exists.");
        }
    }

    private DateTime ComputeNextRunOrThrow(ScheduleDays daysOfWeek, DateOnly? oneTimeDateLocal, TimeOnly timeOfDayLocal, DateTimeOffset nowUtc)
    {
        var computed = NextOccurrence.ComputeNextRunAtUtc(daysOfWeek, oneTimeDateLocal, timeOfDayLocal, nowUtc, _timeProvider.LocalTimeZone);
        // Only a one-time schedule can compute to null (its date/time already passed); a recurring
        // schedule always finds a future occurrence within a week.
        return computed ?? throw new ArgumentException(
            "A one-time schedule's date and time must be in the future.", nameof(oneTimeDateLocal));
    }

    private async Task RunOneAsync(Schedule schedule, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var dueAtUtc = schedule.NextRunAtUtc!.Value; // non-null: guaranteed by RunDueSchedulesAsync's filter
        // DateTime - DateTimeOffset (or vice versa) uses an implicit conversion that reinterprets an
        // Unspecified-Kind DateTime as *local* time - dangerous here since dueAtUtc came from the
        // database. AppDbContext re-tags it Utc on read, but comparing via .UtcDateTime (DateTime -
        // DateTime, which compares ticks only and ignores Kind entirely) doesn't depend on that
        // conversion behaving correctly, so it's kept even with the converter in place.
        var overdueBy = nowUtc.UtcDateTime - dueAtUtc;

        if (overdueBy > GraceWindow)
        {
            _logger.LogWarning(
                "Schedule {ScheduleId} for shortcut {ShortcutId} was due at {DueAtUtc:o} but is {OverdueBy} overdue, " +
                "beyond the {GraceWindow} grace window - skipping this occurrence without applying it.",
                schedule.Id, schedule.ShortcutId, dueAtUtc, overdueBy, GraceWindow);
        }
        else
        {
            try
            {
                await _shortcutService.ApplyShortcutAsync(schedule.ShortcutId, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Schedule {ScheduleId} applied shortcut {ShortcutId}.", schedule.Id, schedule.ShortcutId);
            }
            catch (ShortcutApplyException ex)
            {
                // Best-effort, same as a manual Apply click from the UI - some targets failing
                // (e.g. an offline bulb) doesn't mean the schedule itself failed to run, so it still
                // advances below like a normal firing. There is no UI session to surface this to,
                // so the per-target detail goes to the log instead.
                _logger.LogWarning(ex, "Schedule {ScheduleId} applied shortcut {ShortcutId} with partial failures.", schedule.Id, schedule.ShortcutId);
            }
        }

        await AdvanceAsync(schedule, nowUtc, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Moves a schedule past the occurrence that was just handled (fired or missed - both count as
    /// "handled" for advancement purposes, per the decision table in SCHEDULED-SHORTCUTS-PLAN.md
    /// §2). A one-time schedule is deleted either way; a recurring schedule has its next occurrence
    /// recomputed from <paramref name="nowUtc"/>, which is always at or after the occurrence that
    /// was just handled, so the same occurrence can never be recomputed a second time.
    /// </summary>
    private async Task AdvanceAsync(Schedule schedule, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        if (schedule.DaysOfWeekMask == ScheduleDays.None)
        {
            await _repository.DeleteAsync(schedule.Id, cancellationToken).ConfigureAwait(false);
            return;
        }

        schedule.NextRunAtUtc = NextOccurrence.ComputeNextRunAtUtc(
            schedule.DaysOfWeekMask, schedule.OneTimeDateLocal, schedule.TimeOfDayLocal, nowUtc, _timeProvider.LocalTimeZone);
        await _repository.UpdateAsync(schedule, cancellationToken).ConfigureAwait(false);
    }
}
