using GoveeController.Application.Schedules;
using GoveeController.Application.Shortcuts;
using GoveeController.Domain.Schedules;
using GoveeController.Domain.Shortcuts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace GoveeController.Application.Tests.Schedules;

/// <summary>
/// Uses a real DST-observing zone (America/Chicago) via <see cref="FakeTimeProvider"/>, never the
/// machine's local zone/clock, so these are deterministic wherever they run.
/// </summary>
public class ScheduleServiceTests
{
    private static readonly TimeZoneInfo Chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
    private static readonly Shortcut ExistingShortcut = new() { Id = 1, Name = "Movie Mode", PowerOn = true, CreatedAtUtc = DateTime.UtcNow };

    private static FakeTimeProvider MakeTimeProvider(DateTime utcNow)
    {
        var provider = new FakeTimeProvider(utcNow);
        provider.SetLocalTimeZone(Chicago);
        return provider;
    }

    private static Mock<IShortcutService> MockShortcutServiceWithExistingShortcut()
    {
        var shortcutService = new Mock<IShortcutService>();
        shortcutService.Setup(s => s.ListShortcutsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Shortcut> { ExistingShortcut });
        return shortcutService;
    }

    private static ScheduleService MakeService(
        Mock<IScheduleRepository> repository,
        Mock<IShortcutService> shortcutService,
        FakeTimeProvider timeProvider) =>
        new(repository.Object, shortcutService.Object, timeProvider, NullLogger<ScheduleService>.Instance);

    // --- Validation ---

    [Fact]
    public async Task CreateScheduleAsync_Throws_WhenBothDaysAndOneTimeDateSpecified()
    {
        var repository = new Mock<IScheduleRepository>();
        var shortcutService = MockShortcutServiceWithExistingShortcut();
        var service = MakeService(repository, shortcutService, MakeTimeProvider(new DateTime(2026, 8, 5, 9, 0, 0, DateTimeKind.Utc)));

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateScheduleAsync(
            1, ScheduleDays.Monday, new DateOnly(2026, 8, 10), new TimeOnly(9, 0), isEnabled: true));
    }

    [Fact]
    public async Task CreateScheduleAsync_Throws_WhenNeitherDaysNorOneTimeDateSpecified()
    {
        var repository = new Mock<IScheduleRepository>();
        var shortcutService = MockShortcutServiceWithExistingShortcut();
        var service = MakeService(repository, shortcutService, MakeTimeProvider(new DateTime(2026, 8, 5, 9, 0, 0, DateTimeKind.Utc)));

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateScheduleAsync(
            1, ScheduleDays.None, null, new TimeOnly(9, 0), isEnabled: true));
    }

    [Fact]
    public async Task CreateScheduleAsync_Throws_WhenShortcutDoesNotExist()
    {
        var repository = new Mock<IScheduleRepository>();
        var shortcutService = new Mock<IShortcutService>();
        shortcutService.Setup(s => s.ListShortcutsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut>());
        var service = MakeService(repository, shortcutService, MakeTimeProvider(new DateTime(2026, 8, 5, 9, 0, 0, DateTimeKind.Utc)));

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CreateScheduleAsync(
            999, ScheduleDays.Monday, null, new TimeOnly(9, 0), isEnabled: true));
    }

    [Fact]
    public async Task CreateScheduleAsync_Throws_WhenOneTimeDateIsInThePast()
    {
        var repository = new Mock<IScheduleRepository>();
        var shortcutService = MockShortcutServiceWithExistingShortcut();
        var service = MakeService(repository, shortcutService, MakeTimeProvider(new DateTime(2026, 8, 5, 9, 0, 0, DateTimeKind.Utc)));

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateScheduleAsync(
            1, ScheduleDays.None, new DateOnly(2026, 8, 1), new TimeOnly(9, 0), isEnabled: true));
    }

    [Fact]
    public async Task CreateScheduleAsync_ComputesNextRunAtUtc_ForEnabledRecurringSchedule()
    {
        Schedule? saved = null;
        var repository = new Mock<IScheduleRepository>();
        repository.Setup(r => r.AddAsync(It.IsAny<Schedule>(), It.IsAny<CancellationToken>()))
            .Callback<Schedule, CancellationToken>((s, _) => saved = s)
            .ReturnsAsync((Schedule s, CancellationToken _) => s);
        var shortcutService = MockShortcutServiceWithExistingShortcut();
        // Wednesday 2026-08-05, 9:00 AM Chicago time.
        var timeProvider = MakeTimeProvider(new DateTime(2026, 8, 5, 14, 0, 0, DateTimeKind.Utc));
        var service = MakeService(repository, shortcutService, timeProvider);

        var result = await service.CreateScheduleAsync(1, ScheduleDays.Wednesday, null, new TimeOnly(22, 0), isEnabled: true);

        Assert.NotNull(saved);
        Assert.NotNull(result.NextRunAtUtc);
        Assert.Equal(saved!.NextRunAtUtc, result.NextRunAtUtc);
        repository.Verify(r => r.AddAsync(It.IsAny<Schedule>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateScheduleAsync_LeavesNextRunAtUtcNull_WhenCreatedDisabled()
    {
        Schedule? saved = null;
        var repository = new Mock<IScheduleRepository>();
        repository.Setup(r => r.AddAsync(It.IsAny<Schedule>(), It.IsAny<CancellationToken>()))
            .Callback<Schedule, CancellationToken>((s, _) => saved = s)
            .ReturnsAsync((Schedule s, CancellationToken _) => s);
        var shortcutService = MockShortcutServiceWithExistingShortcut();
        var timeProvider = MakeTimeProvider(new DateTime(2026, 8, 5, 14, 0, 0, DateTimeKind.Utc));
        var service = MakeService(repository, shortcutService, timeProvider);

        await service.CreateScheduleAsync(1, ScheduleDays.Wednesday, null, new TimeOnly(22, 0), isEnabled: false);

        Assert.NotNull(saved);
        Assert.Null(saved!.NextRunAtUtc);
        Assert.False(saved.IsEnabled);
    }

    [Fact]
    public async Task UpdateScheduleAsync_Throws_WhenBothDaysAndOneTimeDateSpecified()
    {
        var repository = new Mock<IScheduleRepository>();
        var shortcutService = MockShortcutServiceWithExistingShortcut();
        var service = MakeService(repository, shortcutService, MakeTimeProvider(new DateTime(2026, 8, 5, 9, 0, 0, DateTimeKind.Utc)));

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateScheduleAsync(
            1, 1, ScheduleDays.Monday, new DateOnly(2026, 8, 10), new TimeOnly(9, 0), isEnabled: true));
    }

    // --- SetEnabledAsync ---

    [Fact]
    public async Task SetEnabledAsync_RecomputesNextRunAtUtc_WhenEnabling()
    {
        var existing = new Schedule
        {
            Id = 1, ShortcutId = 1, DaysOfWeekMask = ScheduleDays.Wednesday, TimeOfDayLocal = new TimeOnly(22, 0),
            IsEnabled = false, NextRunAtUtc = null, CreatedAtUtc = DateTime.UtcNow
        };
        Schedule? saved = null;
        var repository = new Mock<IScheduleRepository>();
        repository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        repository.Setup(r => r.UpdateAsync(It.IsAny<Schedule>(), It.IsAny<CancellationToken>()))
            .Callback<Schedule, CancellationToken>((s, _) => saved = s)
            .Returns(Task.CompletedTask);
        var timeProvider = MakeTimeProvider(new DateTime(2026, 8, 5, 14, 0, 0, DateTimeKind.Utc));
        var service = MakeService(repository, new Mock<IShortcutService>(), timeProvider);

        await service.SetEnabledAsync(1, isEnabled: true);

        Assert.NotNull(saved);
        Assert.True(saved!.IsEnabled);
        Assert.NotNull(saved.NextRunAtUtc);
    }

    [Fact]
    public async Task SetEnabledAsync_ClearsNextRunAtUtc_WhenDisabling()
    {
        var existing = new Schedule
        {
            Id = 1, ShortcutId = 1, DaysOfWeekMask = ScheduleDays.Wednesday, TimeOfDayLocal = new TimeOnly(22, 0),
            IsEnabled = true, NextRunAtUtc = new DateTime(2026, 8, 12, 3, 0, 0, DateTimeKind.Utc), CreatedAtUtc = DateTime.UtcNow
        };
        Schedule? saved = null;
        var repository = new Mock<IScheduleRepository>();
        repository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        repository.Setup(r => r.UpdateAsync(It.IsAny<Schedule>(), It.IsAny<CancellationToken>()))
            .Callback<Schedule, CancellationToken>((s, _) => saved = s)
            .Returns(Task.CompletedTask);
        var timeProvider = MakeTimeProvider(new DateTime(2026, 8, 5, 14, 0, 0, DateTimeKind.Utc));
        var service = MakeService(repository, new Mock<IShortcutService>(), timeProvider);

        await service.SetEnabledAsync(1, isEnabled: false);

        Assert.NotNull(saved);
        Assert.False(saved!.IsEnabled);
        Assert.Null(saved.NextRunAtUtc);
    }

    [Fact]
    public async Task SetEnabledAsync_Throws_WhenScheduleDoesNotExist()
    {
        var repository = new Mock<IScheduleRepository>();
        repository.Setup(r => r.GetByIdAsync(999, It.IsAny<CancellationToken>())).ReturnsAsync((Schedule?)null);
        var service = MakeService(repository, new Mock<IShortcutService>(), MakeTimeProvider(new DateTime(2026, 8, 5, 9, 0, 0, DateTimeKind.Utc)));

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.SetEnabledAsync(999, isEnabled: true));
    }

    // --- RunDueSchedulesAsync ---

    [Fact]
    public async Task RunDueSchedulesAsync_AppliesShortcut_WhenDueWithinGraceWindow()
    {
        var dueAt = new DateTime(2026, 8, 5, 14, 0, 0, DateTimeKind.Utc);
        var schedule = new Schedule
        {
            Id = 1, ShortcutId = 42, DaysOfWeekMask = ScheduleDays.None, OneTimeDateLocal = new DateOnly(2026, 8, 5),
            TimeOfDayLocal = new TimeOnly(9, 0), IsEnabled = true, NextRunAtUtc = dueAt, CreatedAtUtc = DateTime.UtcNow
        };
        var repository = new Mock<IScheduleRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Schedule> { schedule });
        var shortcutService = new Mock<IShortcutService>();
        // Two minutes late - within the 5-minute grace window.
        var timeProvider = MakeTimeProvider(dueAt.AddMinutes(2));
        var service = MakeService(repository, shortcutService, timeProvider);

        await service.RunDueSchedulesAsync();

        shortcutService.Verify(s => s.ApplyShortcutAsync(42, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunDueSchedulesAsync_AppliesShortcut_WhenNextRunAtUtcHasUnspecifiedKind()
    {
        // SQLite has no concept of DateTimeKind - a value written with Kind=Utc always comes back
        // from a real read as Kind=Unspecified (AppDbContext's value converter re-tags it, but this
        // test exists specifically so a regression in that converter, or a new code path that
        // bypasses it, gets caught here rather than only live against a real database). Comparing
        // an Unspecified-Kind DateTime against a DateTimeOffset via the implicit conversion
        // reinterprets it as *local* time, which silently made a genuinely-due schedule look not-yet-due.
        var dueAt = DateTime.SpecifyKind(new DateTime(2026, 8, 5, 14, 0, 0), DateTimeKind.Unspecified);
        var schedule = new Schedule
        {
            Id = 1, ShortcutId = 42, DaysOfWeekMask = ScheduleDays.None, OneTimeDateLocal = new DateOnly(2026, 8, 5),
            TimeOfDayLocal = new TimeOnly(9, 0), IsEnabled = true, NextRunAtUtc = dueAt, CreatedAtUtc = DateTime.UtcNow
        };
        var repository = new Mock<IScheduleRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Schedule> { schedule });
        var shortcutService = new Mock<IShortcutService>();
        // Two minutes after the (Unspecified-Kind) due instant, expressed as a real Utc-Kind now -
        // Chicago is UTC-5/-6, so if this were ever wrongly treated as local, "due" would appear to
        // be hours in the future and this assertion would fail.
        var timeProvider = MakeTimeProvider(new DateTime(2026, 8, 5, 14, 2, 0, DateTimeKind.Utc));
        var service = MakeService(repository, shortcutService, timeProvider);

        await service.RunDueSchedulesAsync();

        shortcutService.Verify(s => s.ApplyShortcutAsync(42, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunDueSchedulesAsync_SkipsWithoutApplying_WhenOverdueBeyondGraceWindow()
    {
        var dueAt = new DateTime(2026, 8, 5, 14, 0, 0, DateTimeKind.Utc);
        var schedule = new Schedule
        {
            Id = 1, ShortcutId = 42, DaysOfWeekMask = ScheduleDays.Wednesday,
            TimeOfDayLocal = new TimeOnly(9, 0), IsEnabled = true, NextRunAtUtc = dueAt, CreatedAtUtc = DateTime.UtcNow
        };
        var repository = new Mock<IScheduleRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Schedule> { schedule });
        repository.Setup(r => r.UpdateAsync(It.IsAny<Schedule>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var shortcutService = new Mock<IShortcutService>();
        // Ten minutes late - beyond the 5-minute grace window.
        var timeProvider = MakeTimeProvider(dueAt.AddMinutes(10));
        var service = MakeService(repository, shortcutService, timeProvider);

        await service.RunDueSchedulesAsync();

        shortcutService.Verify(s => s.ApplyShortcutAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunDueSchedulesAsync_DeletesOneTimeSchedule_AfterFiring()
    {
        var dueAt = new DateTime(2026, 8, 5, 14, 0, 0, DateTimeKind.Utc);
        var schedule = new Schedule
        {
            Id = 7, ShortcutId = 42, DaysOfWeekMask = ScheduleDays.None, OneTimeDateLocal = new DateOnly(2026, 8, 5),
            TimeOfDayLocal = new TimeOnly(9, 0), IsEnabled = true, NextRunAtUtc = dueAt, CreatedAtUtc = DateTime.UtcNow
        };
        var repository = new Mock<IScheduleRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Schedule> { schedule });
        var shortcutService = new Mock<IShortcutService>();
        var timeProvider = MakeTimeProvider(dueAt.AddMinutes(1));
        var service = MakeService(repository, shortcutService, timeProvider);

        await service.RunDueSchedulesAsync();

        repository.Verify(r => r.DeleteAsync(7, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.UpdateAsync(It.IsAny<Schedule>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunDueSchedulesAsync_DeletesOneTimeSchedule_WhenMissedBeyondGrace_WithoutApplying()
    {
        var dueAt = new DateTime(2026, 8, 5, 14, 0, 0, DateTimeKind.Utc);
        var schedule = new Schedule
        {
            Id = 7, ShortcutId = 42, DaysOfWeekMask = ScheduleDays.None, OneTimeDateLocal = new DateOnly(2026, 8, 5),
            TimeOfDayLocal = new TimeOnly(9, 0), IsEnabled = true, NextRunAtUtc = dueAt, CreatedAtUtc = DateTime.UtcNow
        };
        var repository = new Mock<IScheduleRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Schedule> { schedule });
        var shortcutService = new Mock<IShortcutService>();
        var timeProvider = MakeTimeProvider(dueAt.AddHours(2));
        var service = MakeService(repository, shortcutService, timeProvider);

        await service.RunDueSchedulesAsync();

        shortcutService.Verify(s => s.ApplyShortcutAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.DeleteAsync(7, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunDueSchedulesAsync_SwallowsShortcutApplyException_AndStillAdvancesSchedule()
    {
        var dueAt = new DateTime(2026, 8, 5, 14, 0, 0, DateTimeKind.Utc);
        var schedule = new Schedule
        {
            Id = 7, ShortcutId = 42, DaysOfWeekMask = ScheduleDays.None, OneTimeDateLocal = new DateOnly(2026, 8, 5),
            TimeOfDayLocal = new TimeOnly(9, 0), IsEnabled = true, NextRunAtUtc = dueAt, CreatedAtUtc = DateTime.UtcNow
        };
        var repository = new Mock<IScheduleRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Schedule> { schedule });
        var shortcutService = new Mock<IShortcutService>();
        shortcutService.Setup(s => s.ApplyShortcutAsync(42, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ShortcutApplyException(1, 2, [new ShortcutTargetFailure("SKU", "id", "offline", 42, "Movie Mode")]));
        var timeProvider = MakeTimeProvider(dueAt.AddMinutes(1));
        var service = MakeService(repository, shortcutService, timeProvider);

        // Must not throw - a partial-failure apply still counts as "handled" for advancement.
        await service.RunDueSchedulesAsync();

        repository.Verify(r => r.DeleteAsync(7, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunDueSchedulesAsync_SkipsDisabledSchedules()
    {
        var dueAt = new DateTime(2026, 8, 5, 14, 0, 0, DateTimeKind.Utc);
        var schedule = new Schedule
        {
            Id = 1, ShortcutId = 42, DaysOfWeekMask = ScheduleDays.Wednesday,
            TimeOfDayLocal = new TimeOnly(9, 0), IsEnabled = false, NextRunAtUtc = dueAt, CreatedAtUtc = DateTime.UtcNow
        };
        var repository = new Mock<IScheduleRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Schedule> { schedule });
        var shortcutService = new Mock<IShortcutService>();
        var timeProvider = MakeTimeProvider(dueAt.AddMinutes(1));
        var service = MakeService(repository, shortcutService, timeProvider);

        await service.RunDueSchedulesAsync();

        shortcutService.Verify(s => s.ApplyShortcutAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunDueSchedulesAsync_SkipsSchedulesNotYetDue()
    {
        var notYetDue = new DateTime(2026, 8, 5, 14, 0, 0, DateTimeKind.Utc);
        var schedule = new Schedule
        {
            Id = 1, ShortcutId = 42, DaysOfWeekMask = ScheduleDays.Wednesday,
            TimeOfDayLocal = new TimeOnly(9, 0), IsEnabled = true, NextRunAtUtc = notYetDue, CreatedAtUtc = DateTime.UtcNow
        };
        var repository = new Mock<IScheduleRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Schedule> { schedule });
        var shortcutService = new Mock<IShortcutService>();
        var timeProvider = MakeTimeProvider(notYetDue.AddMinutes(-1));
        var service = MakeService(repository, shortcutService, timeProvider);

        await service.RunDueSchedulesAsync();

        shortcutService.Verify(s => s.ApplyShortcutAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunDueSchedulesAsync_RecomputesNextRunAtUtc_ForRecurringScheduleAfterFiring()
    {
        var dueAt = new DateTime(2026, 8, 5, 14, 0, 0, DateTimeKind.Utc); // Wednesday 9:00 AM Chicago
        var schedule = new Schedule
        {
            Id = 1, ShortcutId = 42, DaysOfWeekMask = ScheduleDays.Wednesday,
            TimeOfDayLocal = new TimeOnly(9, 0), IsEnabled = true, NextRunAtUtc = dueAt, CreatedAtUtc = DateTime.UtcNow
        };
        Schedule? saved = null;
        var repository = new Mock<IScheduleRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Schedule> { schedule });
        repository.Setup(r => r.UpdateAsync(It.IsAny<Schedule>(), It.IsAny<CancellationToken>()))
            .Callback<Schedule, CancellationToken>((s, _) => saved = s)
            .Returns(Task.CompletedTask);
        var shortcutService = new Mock<IShortcutService>();
        var timeProvider = MakeTimeProvider(dueAt.AddMinutes(1));
        var service = MakeService(repository, shortcutService, timeProvider);

        await service.RunDueSchedulesAsync();

        Assert.NotNull(saved);
        Assert.NotNull(saved!.NextRunAtUtc);
        // Must have moved forward, not left pointing at the occurrence that just fired.
        Assert.True(saved.NextRunAtUtc > dueAt);
        repository.Verify(r => r.DeleteAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunDueSchedulesAsync_RunsMultipleDueSchedulesSequentially()
    {
        var dueAt = new DateTime(2026, 8, 5, 14, 0, 0, DateTimeKind.Utc);
        var a = new Schedule
        {
            Id = 1, ShortcutId = 10, DaysOfWeekMask = ScheduleDays.None, OneTimeDateLocal = new DateOnly(2026, 8, 5),
            TimeOfDayLocal = new TimeOnly(9, 0), IsEnabled = true, NextRunAtUtc = dueAt, CreatedAtUtc = DateTime.UtcNow
        };
        var b = new Schedule
        {
            Id = 2, ShortcutId = 20, DaysOfWeekMask = ScheduleDays.None, OneTimeDateLocal = new DateOnly(2026, 8, 5),
            TimeOfDayLocal = new TimeOnly(9, 0), IsEnabled = true, NextRunAtUtc = dueAt, CreatedAtUtc = DateTime.UtcNow
        };
        var repository = new Mock<IScheduleRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Schedule> { a, b });
        var shortcutService = new Mock<IShortcutService>();
        var callOrder = new List<int>();
        shortcutService.Setup(s => s.ApplyShortcutAsync(10, It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add(10)).Returns(Task.CompletedTask);
        shortcutService.Setup(s => s.ApplyShortcutAsync(20, It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add(20)).Returns(Task.CompletedTask);
        var timeProvider = MakeTimeProvider(dueAt.AddMinutes(1));
        var service = MakeService(repository, shortcutService, timeProvider);

        await service.RunDueSchedulesAsync();

        Assert.Equal([10, 20], callOrder);
    }
}
