using GoveeController.Application.Shortcuts;
using GoveeController.Domain.Shortcuts;
using Microsoft.EntityFrameworkCore;

namespace GoveeController.Infrastructure.Persistence;

/// <inheritdoc cref="IShortcutRepository" />
public sealed class ShortcutRepository : IShortcutRepository
{
    private readonly AppDbContext _db;

    /// <summary>Creates the repository.</summary>
    public ShortcutRepository(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Shortcut>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _db.Shortcuts
            .Include(s => s.Targets)
            .Include(s => s.ReferencedShortcuts)
            .AsSplitQuery()
            .AsNoTracking()
            .OrderByDescending(s => s.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task<Shortcut?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        _db.Shortcuts
            .Include(s => s.Targets)
            .Include(s => s.ReferencedShortcuts)
            .AsSplitQuery()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<Shortcut> AddAsync(Shortcut shortcut, CancellationToken cancellationToken = default)
    {
        _db.Shortcuts.Add(shortcut);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return shortcut;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(Shortcut shortcut, CancellationToken cancellationToken = default)
    {
        var existing = await _db.Shortcuts
            .Include(s => s.Targets)
            .Include(s => s.ReferencedShortcuts)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Id == shortcut.Id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"No shortcut with id {shortcut.Id} exists.");

        existing.Name = shortcut.Name;
        existing.PowerOn = shortcut.PowerOn;
        existing.Brightness = shortcut.Brightness;
        existing.ColorRgbPacked = shortcut.ColorRgbPacked;
        existing.ColorTemperatureKelvin = shortcut.ColorTemperatureKelvin;
        existing.NextShortcutId = shortcut.NextShortcutId;
        existing.NextShortcutDelaySeconds = shortcut.NextShortcutDelaySeconds;

        // ShortcutTarget.ShortcutId is a required (non-nullable) FK, so EF Core's default cascade
        // behavior deletes entities removed from this collection rather than orphaning them —
        // clearing and re-adding is the simplest correct way to replace the target set.
        existing.Targets.Clear();
        foreach (var target in shortcut.Targets)
        {
            existing.Targets.Add(new ShortcutTarget { DeviceSku = target.DeviceSku, DeviceId = target.DeviceId });
        }

        // Same pattern for the composite reference set. ReferencedShortcutId is a plain FK, so a
        // removed reference is simply dropped (EF Core's default delete-orphan behavior); we never
        // touch the referenced shortcut itself. Only non-null references are carried over — a
        // broken reference (referenced shortcut deleted) is cleaned up on the next save.
        existing.ReferencedShortcuts.Clear();
        foreach (var reference in shortcut.ReferencedShortcuts)
        {
            if (reference.ReferencedShortcutId is not { } referencedId)
            {
                continue;
            }
            existing.ReferencedShortcuts.Add(new ShortcutReference
            {
                ReferencedShortcutId = referencedId,
                DelaySeconds = reference.DelaySeconds,
                Order = reference.Order
            });
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await _db.Shortcuts
            .Where(s => s.Id == id)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
