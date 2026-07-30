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
}
