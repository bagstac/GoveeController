using GoveeController.Domain.Shortcuts;
using GoveeController.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GoveeController.Application.Tests.Persistence;

/// <summary>
/// Tests against a real (in-memory) SQLite database rather than mocks — <see cref="ShortcutRepository.UpdateAsync"/>
/// relies on EF Core's cascade-delete-orphan behavior when the <see cref="Shortcut.Targets"/>
/// collection is cleared and re-populated, which is subtle enough to be worth verifying for real.
/// </summary>
public sealed class ShortcutRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly ShortcutRepository _repository;

    public ShortcutRepositoryTests()
    {
        // SQLite's in-memory mode only persists for as long as a connection to it stays open, so
        // the connection is opened here and held for the test's lifetime rather than per-operation.
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _repository = new ShortcutRepository(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task AddAsync_ThenGetByIdAsync_RoundTripsTargets()
    {
        var shortcut = new Shortcut
        {
            Name = "Movie Mode",
            PowerOn = true,
            Brightness = 50,
            Targets =
            [
                new ShortcutTarget { DeviceSku = "H6159", DeviceId = "AA:BB:CC:DD:EE:FF:00:11" },
                new ShortcutTarget { DeviceSku = "H6159", DeviceId = "11:22:33:44:55:66:77:88" }
            ],
            CreatedAtUtc = DateTime.UtcNow
        };

        await _repository.AddAsync(shortcut);
        var loaded = await _repository.GetByIdAsync(shortcut.Id);

        Assert.NotNull(loaded);
        Assert.Equal("Movie Mode", loaded!.Name);
        Assert.Equal(2, loaded.Targets.Count);
    }

    [Fact]
    public async Task UpdateAsync_ReplacesTargetSet_WithoutLeavingOrphans()
    {
        var shortcut = new Shortcut
        {
            Name = "All Off",
            PowerOn = false,
            Targets =
            [
                new ShortcutTarget { DeviceSku = "H6159", DeviceId = "AA:BB:CC:DD:EE:FF:00:11" },
                new ShortcutTarget { DeviceSku = "H6159", DeviceId = "11:22:33:44:55:66:77:88" }
            ],
            CreatedAtUtc = DateTime.UtcNow
        };
        var added = await _repository.AddAsync(shortcut);

        var updated = new Shortcut
        {
            Id = added.Id,
            Name = "All Off (renamed)",
            PowerOn = false,
            Targets = [new ShortcutTarget { DeviceSku = "H6159", DeviceId = "99:88:77:66:55:44:33:22" }]
        };
        await _repository.UpdateAsync(updated);

        var loaded = await _repository.GetByIdAsync(added.Id);
        Assert.NotNull(loaded);
        Assert.Equal("All Off (renamed)", loaded!.Name);
        var target = Assert.Single(loaded.Targets);
        Assert.Equal("99:88:77:66:55:44:33:22", target.DeviceId);

        // The two original targets must be gone entirely, not orphaned rows with a null/dangling FK.
        var remainingTargetCount = await _db.Set<ShortcutTarget>().CountAsync();
        Assert.Equal(1, remainingTargetCount);
    }

    [Fact]
    public async Task UpdateAsync_Throws_WhenShortcutDoesNotExist()
    {
        var missing = new Shortcut { Id = 999, Name = "Ghost", PowerOn = true };

        await Assert.ThrowsAsync<KeyNotFoundException>(() => _repository.UpdateAsync(missing));
    }

    [Fact]
    public async Task DeleteAsync_CascadesToTargets()
    {
        var shortcut = new Shortcut
        {
            Name = "Sunset",
            PowerOn = true,
            Targets = [new ShortcutTarget { DeviceSku = "H6159", DeviceId = "AA:BB:CC:DD:EE:FF:00:11" }],
            CreatedAtUtc = DateTime.UtcNow
        };
        var added = await _repository.AddAsync(shortcut);

        await _repository.DeleteAsync(added.Id);

        Assert.Null(await _repository.GetByIdAsync(added.Id));
        Assert.Equal(0, await _db.Set<ShortcutTarget>().CountAsync());
    }

    [Fact]
    public async Task GetAllAsync_OrdersByCreatedAtDescending()
    {
        var older = new Shortcut { Name = "Older", PowerOn = true, CreatedAtUtc = DateTime.UtcNow.AddMinutes(-10) };
        var newer = new Shortcut { Name = "Newer", PowerOn = true, CreatedAtUtc = DateTime.UtcNow };
        await _repository.AddAsync(older);
        await _repository.AddAsync(newer);

        var all = await _repository.GetAllAsync();

        Assert.Equal(["Newer", "Older"], all.Select(s => s.Name));
    }

    [Fact]
    public async Task UpdateAsync_RoundTripsNextShortcutFields()
    {
        var target = await _repository.AddAsync(new Shortcut { Name = "Target", PowerOn = true, CreatedAtUtc = DateTime.UtcNow });
        var source = await _repository.AddAsync(new Shortcut { Name = "Source", PowerOn = true, CreatedAtUtc = DateTime.UtcNow });

        var updated = new Shortcut
        {
            Id = source.Id,
            Name = "Source",
            PowerOn = true,
            NextShortcutId = target.Id,
            NextShortcutDelaySeconds = 15
        };
        await _repository.UpdateAsync(updated);

        var loaded = await _repository.GetByIdAsync(source.Id);
        Assert.NotNull(loaded);
        Assert.Equal(target.Id, loaded!.NextShortcutId);
        Assert.Equal(15, loaded.NextShortcutDelaySeconds);
    }

    [Fact]
    public async Task DeletingAFollowedShortcut_ClearsThePredecessorsLink_InsteadOfDeletingOrThrowing()
    {
        // ExecuteDeleteAsync (used by ShortcutRepository.DeleteAsync) issues a raw SQL DELETE that
        // bypasses EF's change tracker entirely, so the SetNull behavior configured in AppDbContext
        // only actually fires if SQLite's own foreign-key enforcement is on for this connection.
        // No explicit PRAGMA is needed here - verified separately that Microsoft.Data.Sqlite enables
        // foreign_keys by default on every connection it opens, including this one.
        var follower = await _repository.AddAsync(new Shortcut { Name = "Follower", PowerOn = true, CreatedAtUtc = DateTime.UtcNow });
        var predecessor = await _repository.AddAsync(new Shortcut
        {
            Name = "Predecessor",
            PowerOn = true,
            CreatedAtUtc = DateTime.UtcNow,
            NextShortcutId = follower.Id,
            NextShortcutDelaySeconds = 5
        });

        await _repository.DeleteAsync(follower.Id);

        Assert.Null(await _repository.GetByIdAsync(follower.Id));
        var reloadedPredecessor = await _repository.GetByIdAsync(predecessor.Id);
        Assert.NotNull(reloadedPredecessor);
        Assert.Null(reloadedPredecessor!.NextShortcutId);
    }

    [Fact]
    public async Task UniqueIndex_RejectsASecondShortcutPointingAtTheSameFollower()
    {
        var follower = await _repository.AddAsync(new Shortcut { Name = "Follower", PowerOn = true, CreatedAtUtc = DateTime.UtcNow });
        await _repository.AddAsync(new Shortcut
        {
            Name = "First predecessor",
            PowerOn = true,
            CreatedAtUtc = DateTime.UtcNow,
            NextShortcutId = follower.Id
        });

        var secondPredecessor = new Shortcut
        {
            Name = "Second predecessor",
            PowerOn = true,
            CreatedAtUtc = DateTime.UtcNow,
            NextShortcutId = follower.Id
        };

        // This is the database-level backstop for the rule ShortcutService.ValidateChainLink
        // already enforces at the application level - both must independently hold, since the
        // service is not the only possible writer of this table over the app's lifetime.
        await Assert.ThrowsAsync<DbUpdateException>(() => _repository.AddAsync(secondPredecessor));
    }

    // --- Composite references (COMPOSITE-SHORTCUTS-PLAN.md) ---

    [Fact]
    public async Task AddAsync_ThenGetByIdAsync_RoundTripsReferencedShortcuts()
    {
        var a = await _repository.AddAsync(new Shortcut { Name = "A", PowerOn = true, CreatedAtUtc = DateTime.UtcNow });
        var b = await _repository.AddAsync(new Shortcut { Name = "B", PowerOn = true, CreatedAtUtc = DateTime.UtcNow });

        var composite = new Shortcut
        {
            Name = "Movie Night",
            PowerOn = false,
            CreatedAtUtc = DateTime.UtcNow,
            ReferencedShortcuts =
            [
                new ShortcutReference { ReferencedShortcutId = b.Id, DelaySeconds = 5, Order = 0 },
                new ShortcutReference { ReferencedShortcutId = a.Id, DelaySeconds = 0, Order = 1 }
            ]
        };
        var added = await _repository.AddAsync(composite);

        var loaded = await _repository.GetByIdAsync(added.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Movie Night", loaded!.Name);
        Assert.Empty(loaded.Targets);
        var references = loaded.ReferencedShortcuts.OrderBy(r => r.Order).ToList();
        Assert.Equal(2, references.Count);
        Assert.Equal(b.Id, references[0].ReferencedShortcutId);
        Assert.Equal(5, references[0].DelaySeconds);
        Assert.Equal(a.Id, references[1].ReferencedShortcutId);
    }

    [Fact]
    public async Task UpdateAsync_ReplacesReferencedShortcuts_WithoutLeavingOrphans()
    {
        var a = await _repository.AddAsync(new Shortcut { Name = "A", PowerOn = true, CreatedAtUtc = DateTime.UtcNow });
        var b = await _repository.AddAsync(new Shortcut { Name = "B", PowerOn = true, CreatedAtUtc = DateTime.UtcNow });
        var c = await _repository.AddAsync(new Shortcut { Name = "C", PowerOn = true, CreatedAtUtc = DateTime.UtcNow });
        var composite = await _repository.AddAsync(new Shortcut
        {
            Name = "Composite",
            PowerOn = false,
            CreatedAtUtc = DateTime.UtcNow,
            ReferencedShortcuts =
            [
                new ShortcutReference { ReferencedShortcutId = a.Id, DelaySeconds = 0, Order = 0 },
                new ShortcutReference { ReferencedShortcutId = b.Id, DelaySeconds = 0, Order = 1 }
            ]
        });

        var updated = new Shortcut
        {
            Id = composite.Id,
            Name = "Composite (renamed)",
            PowerOn = false,
            ReferencedShortcuts =
            [
                new ShortcutReference { ReferencedShortcutId = c.Id, DelaySeconds = 10, Order = 0 }
            ]
        };
        await _repository.UpdateAsync(updated);

        var loaded = await _repository.GetByIdAsync(composite.Id);
        Assert.NotNull(loaded);
        var reference = Assert.Single(loaded!.ReferencedShortcuts);
        Assert.Equal(c.Id, reference.ReferencedShortcutId);
        Assert.Equal(10, reference.DelaySeconds);

        // The two original references must be gone entirely, not orphaned rows.
        Assert.Equal(1, await _db.Set<ShortcutReference>().CountAsync());
    }

    [Fact]
    public async Task DeletingAReferencedShortcut_ClearsTheReference_InsteadOfDeletingTheComposite()
    {
        // ExecuteDeleteAsync issues a raw SQL DELETE, so the SetNull behavior configured on
        // ShortcutReference.ReferencedShortcutId relies on SQLite's own FK enforcement (enabled by
        // default on every Microsoft.Data.Sqlite connection — see the equivalent chain-link test).
        var referenced = await _repository.AddAsync(new Shortcut { Name = "Referenced", PowerOn = true, CreatedAtUtc = DateTime.UtcNow });
        var composite = await _repository.AddAsync(new Shortcut
        {
            Name = "Composite",
            PowerOn = false,
            CreatedAtUtc = DateTime.UtcNow,
            ReferencedShortcuts = [new ShortcutReference { ReferencedShortcutId = referenced.Id, DelaySeconds = 0, Order = 0 }]
        });

        await _repository.DeleteAsync(referenced.Id);

        Assert.Null(await _repository.GetByIdAsync(referenced.Id));
        var reloadedComposite = await _repository.GetByIdAsync(composite.Id);
        Assert.NotNull(reloadedComposite);
        var reference = Assert.Single(reloadedComposite!.ReferencedShortcuts);
        Assert.Null(reference.ReferencedShortcutId);
    }

    [Fact]
    public async Task DeleteAsync_CascadesToReferencedShortcuts()
    {
        var a = await _repository.AddAsync(new Shortcut { Name = "A", PowerOn = true, CreatedAtUtc = DateTime.UtcNow });
        var composite = await _repository.AddAsync(new Shortcut
        {
            Name = "Composite",
            PowerOn = false,
            CreatedAtUtc = DateTime.UtcNow,
            ReferencedShortcuts = [new ShortcutReference { ReferencedShortcutId = a.Id, DelaySeconds = 0, Order = 0 }]
        });

        await _repository.DeleteAsync(composite.Id);

        Assert.Null(await _repository.GetByIdAsync(composite.Id));
        Assert.Equal(0, await _db.Set<ShortcutReference>().CountAsync());
        // The referenced shortcut itself must survive — deleting the composite never deletes what
        // it references.
        Assert.NotNull(await _repository.GetByIdAsync(a.Id));
    }
}
