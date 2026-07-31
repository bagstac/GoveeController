using GoveeController.Domain.Schedules;
using GoveeController.Domain.Shortcuts;
using GoveeController.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GoveeController.Application.Tests.Schedules;

/// <summary>
/// Tests against a real (in-memory) SQLite database, mirroring <c>ShortcutRepositoryTests</c> -
/// in particular to verify the FK/cascade behavior configured in <see cref="AppDbContext"/> for
/// real, not just trusted from the model configuration.
/// </summary>
public sealed class ScheduleRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly ScheduleRepository _repository;
    private readonly ShortcutRepository _shortcutRepository;

    public ScheduleRepositoryTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _repository = new ScheduleRepository(_db);
        _shortcutRepository = new ShortcutRepository(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private async Task<int> AddShortcutAsync() =>
        (await _shortcutRepository.AddAsync(new Shortcut { Name = "Movie Mode", PowerOn = true, CreatedAtUtc = DateTime.UtcNow })).Id;

    [Fact]
    public async Task AddAsync_ThenGetByIdAsync_RoundTripsAllFields()
    {
        var shortcutId = await AddShortcutAsync();
        var schedule = new Schedule
        {
            ShortcutId = shortcutId,
            DaysOfWeekMask = ScheduleDays.Monday | ScheduleDays.Wednesday,
            TimeOfDayLocal = new TimeOnly(22, 0),
            IsEnabled = true,
            NextRunAtUtc = new DateTime(2026, 8, 10, 3, 0, 0, DateTimeKind.Utc),
            CreatedAtUtc = DateTime.UtcNow
        };

        var added = await _repository.AddAsync(schedule);
        var loaded = await _repository.GetByIdAsync(added.Id);

        Assert.NotNull(loaded);
        Assert.Equal(shortcutId, loaded!.ShortcutId);
        Assert.Equal(ScheduleDays.Monday | ScheduleDays.Wednesday, loaded.DaysOfWeekMask);
        Assert.Null(loaded.OneTimeDateLocal);
        Assert.Equal(new TimeOnly(22, 0), loaded.TimeOfDayLocal);
        Assert.True(loaded.IsEnabled);
        Assert.Equal(new DateTime(2026, 8, 10, 3, 0, 0, DateTimeKind.Utc), loaded.NextRunAtUtc);
    }

    [Fact]
    public async Task AddAsync_RoundTripsOneTimeDate()
    {
        var shortcutId = await AddShortcutAsync();
        var schedule = new Schedule
        {
            ShortcutId = shortcutId,
            DaysOfWeekMask = ScheduleDays.None,
            OneTimeDateLocal = new DateOnly(2026, 8, 15),
            TimeOfDayLocal = new TimeOnly(7, 30),
            IsEnabled = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        var added = await _repository.AddAsync(schedule);
        var loaded = await _repository.GetByIdAsync(added.Id);

        Assert.NotNull(loaded);
        Assert.Equal(new DateOnly(2026, 8, 15), loaded!.OneTimeDateLocal);
        Assert.Equal(new TimeOnly(7, 30), loaded.TimeOfDayLocal);
    }

    [Fact]
    public async Task UpdateAsync_OverwritesFields_ButNotCreatedAtUtc()
    {
        var shortcutId = await AddShortcutAsync();
        var originalCreatedAt = DateTime.UtcNow.AddDays(-3);
        var added = await _repository.AddAsync(new Schedule
        {
            ShortcutId = shortcutId,
            DaysOfWeekMask = ScheduleDays.Monday,
            TimeOfDayLocal = new TimeOnly(9, 0),
            IsEnabled = true,
            CreatedAtUtc = originalCreatedAt
        });

        await _repository.UpdateAsync(new Schedule
        {
            Id = added.Id,
            ShortcutId = shortcutId,
            DaysOfWeekMask = ScheduleDays.Friday,
            TimeOfDayLocal = new TimeOnly(18, 0),
            IsEnabled = false,
            NextRunAtUtc = null
        });

        var loaded = await _repository.GetByIdAsync(added.Id);
        Assert.NotNull(loaded);
        Assert.Equal(ScheduleDays.Friday, loaded!.DaysOfWeekMask);
        Assert.Equal(new TimeOnly(18, 0), loaded.TimeOfDayLocal);
        Assert.False(loaded.IsEnabled);
        Assert.Equal(originalCreatedAt, loaded.CreatedAtUtc);
    }

    [Fact]
    public async Task UpdateAsync_Throws_WhenScheduleDoesNotExist()
    {
        var missing = new Schedule { Id = 999, ShortcutId = 1, TimeOfDayLocal = new TimeOnly(9, 0) };

        await Assert.ThrowsAsync<KeyNotFoundException>(() => _repository.UpdateAsync(missing));
    }

    [Fact]
    public async Task DeleteAsync_RemovesSchedule()
    {
        var shortcutId = await AddShortcutAsync();
        var added = await _repository.AddAsync(new Schedule
        {
            ShortcutId = shortcutId, DaysOfWeekMask = ScheduleDays.Monday, TimeOfDayLocal = new TimeOnly(9, 0), IsEnabled = true
        });

        await _repository.DeleteAsync(added.Id);

        Assert.Null(await _repository.GetByIdAsync(added.Id));
    }

    [Fact]
    public async Task DeletingAShortcut_CascadesToItsSchedules()
    {
        // The database-level backstop for "a schedule without its shortcut is meaningless" - see
        // SCHEDULED-SHORTCUTS-PLAN.md §3.6. ShortcutRepository.DeleteAsync uses ExecuteDeleteAsync,
        // a raw SQL DELETE that bypasses EF's change tracker, so this only actually cascades if
        // SQLite's own foreign-key enforcement is on for this connection - already verified
        // separately (see ShortcutRepositoryTests) that Microsoft.Data.Sqlite enables foreign_keys
        // by default on every connection it opens, including this one.
        var shortcutId = await AddShortcutAsync();
        var added = await _repository.AddAsync(new Schedule
        {
            ShortcutId = shortcutId, DaysOfWeekMask = ScheduleDays.Monday, TimeOfDayLocal = new TimeOnly(9, 0), IsEnabled = true
        });

        await _shortcutRepository.DeleteAsync(shortcutId);

        Assert.Null(await _repository.GetByIdAsync(added.Id));
    }

    [Fact]
    public async Task GetAllAsync_OrdersByCreatedAtDescending()
    {
        var shortcutId = await AddShortcutAsync();
        var older = new Schedule
        {
            ShortcutId = shortcutId, DaysOfWeekMask = ScheduleDays.Monday, TimeOfDayLocal = new TimeOnly(9, 0),
            IsEnabled = true, CreatedAtUtc = DateTime.UtcNow.AddMinutes(-10)
        };
        var newer = new Schedule
        {
            ShortcutId = shortcutId, DaysOfWeekMask = ScheduleDays.Tuesday, TimeOfDayLocal = new TimeOnly(9, 0),
            IsEnabled = true, CreatedAtUtc = DateTime.UtcNow
        };
        await _repository.AddAsync(older);
        await _repository.AddAsync(newer);

        var all = await _repository.GetAllAsync();

        Assert.Equal([ScheduleDays.Tuesday, ScheduleDays.Monday], all.Select(s => s.DaysOfWeekMask));
    }
}
