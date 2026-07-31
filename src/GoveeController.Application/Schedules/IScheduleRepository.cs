using GoveeController.Domain.Schedules;

namespace GoveeController.Application.Schedules;

/// <summary>
/// Persistence abstraction for <see cref="Schedule"/> rows. Implemented by the Infrastructure
/// layer using EF Core over SQLite; kept as an interface here so the Application layer's
/// use-case services can be unit tested without a real database.
/// </summary>
public interface IScheduleRepository
{
    /// <summary>Returns all saved schedules, ordered by creation date (newest first).</summary>
    Task<IReadOnlyList<Schedule>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns one schedule by id, or null if it does not exist.</summary>
    Task<Schedule?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Persists a new schedule and returns it with its generated <see cref="Schedule.Id"/> populated.</summary>
    Task<Schedule> AddAsync(Schedule schedule, CancellationToken cancellationToken = default);

    /// <summary>
    /// Overwrites an existing schedule (identified by <see cref="Schedule.Id"/>) with the given
    /// values. Throws <see cref="KeyNotFoundException"/> if no schedule with that id exists.
    /// </summary>
    Task UpdateAsync(Schedule schedule, CancellationToken cancellationToken = default);

    /// <summary>Deletes a schedule by id. No-op if it does not exist.</summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
