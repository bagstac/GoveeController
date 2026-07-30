using GoveeController.Application.Schedules;
using GoveeController.Domain.Schedules;
using Microsoft.EntityFrameworkCore;

namespace GoveeController.Infrastructure.Persistence;

/// <inheritdoc cref="IScheduleRepository" />
public sealed class ScheduleRepository : IScheduleRepository
{
    private readonly AppDbContext _db;

    /// <summary>Creates the repository.</summary>
    public ScheduleRepository(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Schedule>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _db.Schedules
            .AsNoTracking()
            .OrderByDescending(s => s.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task<Schedule?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        _db.Schedules.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<Schedule> AddAsync(Schedule schedule, CancellationToken cancellationToken = default)
    {
        _db.Schedules.Add(schedule);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return schedule;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(Schedule schedule, CancellationToken cancellationToken = default)
    {
        var existing = await _db.Schedules
            .FirstOrDefaultAsync(s => s.Id == schedule.Id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"No schedule with id {schedule.Id} exists.");

        existing.ShortcutId = schedule.ShortcutId;
        existing.DaysOfWeekMask = schedule.DaysOfWeekMask;
        existing.OneTimeDateLocal = schedule.OneTimeDateLocal;
        existing.TimeOfDayLocal = schedule.TimeOfDayLocal;
        existing.IsEnabled = schedule.IsEnabled;
        existing.NextRunAtUtc = schedule.NextRunAtUtc;
        // CreatedAtUtc is deliberately not overwritten - same as ShortcutRepository.UpdateAsync,
        // since callers never populate it on the incoming update object.

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await _db.Schedules
            .Where(s => s.Id == id)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
