using GoveeController.Domain.Shortcuts;

namespace GoveeController.Application.Shortcuts;

/// <summary>
/// Persistence abstraction for user-defined <see cref="Shortcut"/> presets. Implemented by the
/// Infrastructure layer using EF Core over SQLite; kept as an interface here so the Application
/// layer's use-case services can be unit tested without a real database.
/// </summary>
public interface IShortcutRepository
{
    /// <summary>Returns all saved shortcuts, ordered by creation date (newest first).</summary>
    Task<IReadOnlyList<Shortcut>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one shortcut by id, or null if it does not exist. No production code calls this as
    /// of the linked-shortcuts feature - <see cref="ShortcutService.ApplyShortcutAsync"/> (its last
    /// caller) now loads the full set via <see cref="GetAllAsync"/> to resolve chains in memory
    /// (see LINKED-SHORTCUTS-PLAN.md §3.4). Kept on the interface because it's still useful for
    /// tests to look up a single row by id without re-deriving it from a full-list query.
    /// </summary>
    Task<Shortcut?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Persists a new shortcut and returns it with its generated <see cref="Shortcut.Id"/> populated.</summary>
    Task<Shortcut> AddAsync(Shortcut shortcut, CancellationToken cancellationToken = default);

    /// <summary>
    /// Overwrites an existing shortcut (identified by <see cref="Shortcut.Id"/>) with the given values,
    /// including replacing its full set of <see cref="Shortcut.Targets"/>. Throws <see cref="KeyNotFoundException"/>
    /// if no shortcut with that id exists.
    /// </summary>
    Task UpdateAsync(Shortcut shortcut, CancellationToken cancellationToken = default);

    /// <summary>Deletes a shortcut by id. No-op if it does not exist.</summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
