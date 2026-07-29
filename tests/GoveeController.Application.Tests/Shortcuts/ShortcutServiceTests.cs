using GoveeController.Application.Devices;
using GoveeController.Application.Shortcuts;
using GoveeController.Domain.Devices;
using GoveeController.Domain.Shortcuts;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace GoveeController.Application.Tests.Shortcuts;

public class ShortcutServiceTests
{
    private const string Sku = "H6159";
    private const string DeviceId = "AA:BB:CC:DD:EE:FF:00:11";
    private const string Sku2 = "H6159";
    private const string DeviceId2 = "11:22:33:44:55:66:77:88";
    private const string Sku3 = "H6159";
    private const string DeviceId3 = "22:33:44:55:66:77:88:99";

    private static readonly IReadOnlyList<(string Sku, string DeviceId)> OneTarget = [(Sku, DeviceId)];
    private static readonly IReadOnlyList<(string Sku, string DeviceId)> TwoTargets = [(Sku, DeviceId), (Sku2, DeviceId2)];

    [Fact]
    public async Task CreateShortcutAsync_Throws_WhenColorAndTemperatureBothSpecified()
    {
        var repository = new Mock<IShortcutRepository>();
        var deviceControl = new Mock<IDeviceControlService>();
        var service = new ShortcutService(repository.Object, deviceControl.Object, TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateShortcutAsync(
            "Movie Mode", OneTarget, powerOn: true, brightness: 50,
            color: new RgbColor(255, 0, 0), colorTemperatureKelvin: 4000,
            nextShortcutId: null, nextShortcutDelaySeconds: 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task CreateShortcutAsync_Throws_WhenBrightnessOutOfRange(int brightness)
    {
        var repository = new Mock<IShortcutRepository>();
        var deviceControl = new Mock<IDeviceControlService>();
        var service = new ShortcutService(repository.Object, deviceControl.Object, TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateShortcutAsync(
            "Bad Brightness", OneTarget, powerOn: true, brightness: brightness, color: null, colorTemperatureKelvin: null,
            nextShortcutId: null, nextShortcutDelaySeconds: 0));
    }

    [Theory]
    [InlineData(1999)]
    [InlineData(9001)]
    public async Task CreateShortcutAsync_Throws_WhenColorTemperatureOutOfRange(int kelvin)
    {
        var repository = new Mock<IShortcutRepository>();
        var deviceControl = new Mock<IDeviceControlService>();
        var service = new ShortcutService(repository.Object, deviceControl.Object, TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateShortcutAsync(
            "Bad Temp", OneTarget, powerOn: true, brightness: null, color: null, colorTemperatureKelvin: kelvin,
            nextShortcutId: null, nextShortcutDelaySeconds: 0));
    }

    [Fact]
    public async Task CreateShortcutAsync_Throws_WhenNoTargetsSpecified()
    {
        var repository = new Mock<IShortcutRepository>();
        var deviceControl = new Mock<IDeviceControlService>();
        var service = new ShortcutService(repository.Object, deviceControl.Object, TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateShortcutAsync(
            "Empty", targets: [], powerOn: true, brightness: null, color: null, colorTemperatureKelvin: null,
            nextShortcutId: null, nextShortcutDelaySeconds: 0));
    }

    [Fact]
    public async Task CreateShortcutAsync_PacksColorAndPersistsAllTargets()
    {
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut>());
        repository.Setup(r => r.AddAsync(It.IsAny<Shortcut>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Shortcut s, CancellationToken _) => s);
        var deviceControl = new Mock<IDeviceControlService>();
        var service = new ShortcutService(repository.Object, deviceControl.Object, TimeProvider.System);

        var color = new RgbColor(255, 128, 0);
        var result = await service.CreateShortcutAsync(
            "Sunset", TwoTargets, true, 60, color, null, nextShortcutId: null, nextShortcutDelaySeconds: 0);

        Assert.Equal(color.ToPackedInt(), result.ColorRgbPacked);
        Assert.Null(result.ColorTemperatureKelvin);
        Assert.Equal(2, result.Targets.Count);
        Assert.Contains(result.Targets, t => t.DeviceId == DeviceId);
        Assert.Contains(result.Targets, t => t.DeviceId == DeviceId2);
        Assert.Null(result.NextShortcutId);
        Assert.Equal(0, result.NextShortcutDelaySeconds);
        repository.Verify(r => r.AddAsync(It.IsAny<Shortcut>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateShortcutAsync_Throws_WhenColorAndTemperatureBothSpecified()
    {
        var repository = new Mock<IShortcutRepository>();
        var deviceControl = new Mock<IDeviceControlService>();
        var service = new ShortcutService(repository.Object, deviceControl.Object, TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateShortcutAsync(
            1, "Movie Mode", OneTarget, powerOn: true, brightness: 50,
            color: new RgbColor(255, 0, 0), colorTemperatureKelvin: 4000,
            nextShortcutId: null, nextShortcutDelaySeconds: 0));
    }

    [Fact]
    public async Task UpdateShortcutAsync_Throws_WhenNoTargetsSpecified()
    {
        var repository = new Mock<IShortcutRepository>();
        var deviceControl = new Mock<IDeviceControlService>();
        var service = new ShortcutService(repository.Object, deviceControl.Object, TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateShortcutAsync(
            1, "Empty", targets: [], powerOn: true, brightness: null, color: null, colorTemperatureKelvin: null,
            nextShortcutId: null, nextShortcutDelaySeconds: 0));
    }

    [Fact]
    public async Task UpdateShortcutAsync_PassesIdAndPackedColorToRepository()
    {
        Shortcut? saved = null;
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut>());
        repository.Setup(r => r.UpdateAsync(It.IsAny<Shortcut>(), It.IsAny<CancellationToken>()))
            .Callback<Shortcut, CancellationToken>((s, _) => saved = s)
            .Returns(Task.CompletedTask);
        var deviceControl = new Mock<IDeviceControlService>();
        var service = new ShortcutService(repository.Object, deviceControl.Object, TimeProvider.System);

        var color = new RgbColor(1, 2, 3);
        await service.UpdateShortcutAsync(
            7, "Renamed", TwoTargets, false, null, color, null, nextShortcutId: null, nextShortcutDelaySeconds: 0);

        repository.Verify(r => r.UpdateAsync(It.IsAny<Shortcut>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(saved);
        Assert.Equal(7, saved!.Id);
        Assert.Equal("Renamed", saved.Name);
        Assert.False(saved.PowerOn);
        Assert.Equal(color.ToPackedInt(), saved.ColorRgbPacked);
        Assert.Equal(2, saved.Targets.Count);
    }

    // --- Chain-link validation (LINKED-SHORTCUTS-PLAN.md §6) ---

    [Fact]
    public async Task UpdateShortcutAsync_Throws_WhenLinkingToItself()
    {
        var a = new Shortcut { Id = 1, Name = "A", PowerOn = true, CreatedAtUtc = DateTime.UtcNow };
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut> { a });
        var service = new ShortcutService(repository.Object, new Mock<IDeviceControlService>().Object, TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateShortcutAsync(
            1, "A", OneTarget, powerOn: true, brightness: null, color: null, colorTemperatureKelvin: null,
            nextShortcutId: 1, nextShortcutDelaySeconds: 0));
    }

    [Fact]
    public async Task UpdateShortcutAsync_Throws_WhenTargetAlreadyHasADifferentPredecessor()
    {
        var a = new Shortcut { Id = 1, Name = "A", PowerOn = true, CreatedAtUtc = DateTime.UtcNow, NextShortcutId = 2 };
        var b = new Shortcut { Id = 2, Name = "B", PowerOn = true, CreatedAtUtc = DateTime.UtcNow };
        var c = new Shortcut { Id = 3, Name = "C", PowerOn = true, CreatedAtUtc = DateTime.UtcNow };
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut> { a, b, c });
        var service = new ShortcutService(repository.Object, new Mock<IDeviceControlService>().Object, TimeProvider.System);

        // B already follows A - C should not be able to also claim B as its next step.
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateShortcutAsync(
            3, "C", OneTarget, powerOn: true, brightness: null, color: null, colorTemperatureKelvin: null,
            nextShortcutId: 2, nextShortcutDelaySeconds: 0));
    }

    [Fact]
    public async Task UpdateShortcutAsync_Throws_WhenLinkWouldCreateCycle()
    {
        var a = new Shortcut { Id = 1, Name = "A", PowerOn = true, CreatedAtUtc = DateTime.UtcNow, NextShortcutId = 2 };
        var b = new Shortcut { Id = 2, Name = "B", PowerOn = true, CreatedAtUtc = DateTime.UtcNow };
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut> { a, b });
        var service = new ShortcutService(repository.Object, new Mock<IDeviceControlService>().Object, TimeProvider.System);

        // A already runs B next - making B run A next would form a 2-cycle.
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateShortcutAsync(
            2, "B", OneTarget, powerOn: true, brightness: null, color: null, colorTemperatureKelvin: null,
            nextShortcutId: 1, nextShortcutDelaySeconds: 0));
    }

    [Fact]
    public async Task UpdateShortcutAsync_Throws_WhenChainWouldExceedThreeShortcuts()
    {
        var a = new Shortcut { Id = 1, Name = "A", PowerOn = true, CreatedAtUtc = DateTime.UtcNow, NextShortcutId = 2 };
        var b = new Shortcut { Id = 2, Name = "B", PowerOn = true, CreatedAtUtc = DateTime.UtcNow, NextShortcutId = 3 };
        var c = new Shortcut { Id = 3, Name = "C", PowerOn = true, CreatedAtUtc = DateTime.UtcNow };
        var d = new Shortcut { Id = 4, Name = "D", PowerOn = true, CreatedAtUtc = DateTime.UtcNow };
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut> { a, b, c, d });
        var service = new ShortcutService(repository.Object, new Mock<IDeviceControlService>().Object, TimeProvider.System);

        // A -> B -> C already has 3 shortcuts; having C also run D would make a chain of 4.
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateShortcutAsync(
            3, "C", OneTarget, powerOn: true, brightness: null, color: null, colorTemperatureKelvin: null,
            nextShortcutId: 4, nextShortcutDelaySeconds: 0));
    }

    [Fact]
    public async Task UpdateShortcutAsync_Succeeds_WhenChainIsExactlyThreeShortcuts()
    {
        var a = new Shortcut { Id = 1, Name = "A", PowerOn = true, CreatedAtUtc = DateTime.UtcNow, NextShortcutId = 2 };
        var b = new Shortcut { Id = 2, Name = "B", PowerOn = true, CreatedAtUtc = DateTime.UtcNow };
        var c = new Shortcut { Id = 3, Name = "C", PowerOn = true, CreatedAtUtc = DateTime.UtcNow };
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut> { a, b, c });
        repository.Setup(r => r.UpdateAsync(It.IsAny<Shortcut>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var service = new ShortcutService(repository.Object, new Mock<IDeviceControlService>().Object, TimeProvider.System);

        // A -> B -> C is exactly 3 shortcuts - the boundary case must NOT throw.
        await service.UpdateShortcutAsync(
            2, "B", OneTarget, powerOn: true, brightness: null, color: null, colorTemperatureKelvin: null,
            nextShortcutId: 3, nextShortcutDelaySeconds: 0);

        repository.Verify(r => r.UpdateAsync(It.IsAny<Shortcut>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(61)]
    public async Task CreateShortcutAsync_Throws_WhenChainDelayOutOfRange(int delaySeconds)
    {
        var target = new Shortcut { Id = 5, Name = "Target", PowerOn = true, CreatedAtUtc = DateTime.UtcNow };
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut> { target });
        var service = new ShortcutService(repository.Object, new Mock<IDeviceControlService>().Object, TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateShortcutAsync(
            "New", OneTarget, powerOn: true, brightness: null, color: null, colorTemperatureKelvin: null,
            nextShortcutId: 5, nextShortcutDelaySeconds: delaySeconds));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(60)]
    public async Task CreateShortcutAsync_Succeeds_WhenChainDelayAtBounds(int delaySeconds)
    {
        var target = new Shortcut { Id = 5, Name = "Target", PowerOn = true, CreatedAtUtc = DateTime.UtcNow };
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut> { target });
        repository.Setup(r => r.AddAsync(It.IsAny<Shortcut>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Shortcut s, CancellationToken _) => s);
        var service = new ShortcutService(repository.Object, new Mock<IDeviceControlService>().Object, TimeProvider.System);

        var result = await service.CreateShortcutAsync(
            "New", OneTarget, powerOn: true, brightness: null, color: null, colorTemperatureKelvin: null,
            nextShortcutId: 5, nextShortcutDelaySeconds: delaySeconds);

        Assert.Equal(delaySeconds, result.NextShortcutDelaySeconds);
    }

    [Fact]
    public async Task CreateShortcutAsync_Throws_WhenNextShortcutDoesNotExist()
    {
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut>());
        var service = new ShortcutService(repository.Object, new Mock<IDeviceControlService>().Object, TimeProvider.System);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CreateShortcutAsync(
            "New", OneTarget, powerOn: true, brightness: null, color: null, colorTemperatureKelvin: null,
            nextShortcutId: 999, nextShortcutDelaySeconds: 0));
    }

    [Fact]
    public async Task CreateShortcutAsync_NormalizesDelayToZero_WhenNotLinked()
    {
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut>());
        repository.Setup(r => r.AddAsync(It.IsAny<Shortcut>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Shortcut s, CancellationToken _) => s);
        var service = new ShortcutService(repository.Object, new Mock<IDeviceControlService>().Object, TimeProvider.System);

        // A stray non-zero delay with no link must not be persisted - it would reappear if this
        // shortcut were later linked, which is exactly the staleness the plan calls out.
        var result = await service.CreateShortcutAsync(
            "New", OneTarget, powerOn: true, brightness: null, color: null, colorTemperatureKelvin: null,
            nextShortcutId: null, nextShortcutDelaySeconds: 45);

        Assert.Equal(0, result.NextShortcutDelaySeconds);
    }

    // --- ListEligibleNextShortcutsAsync ---

    [Fact]
    public async Task ListEligibleNextShortcutsAsync_ExcludesShortcutsThatAlreadyHaveADifferentPredecessor()
    {
        var a = new Shortcut { Id = 1, Name = "A", PowerOn = true, CreatedAtUtc = DateTime.UtcNow, NextShortcutId = 2 };
        var b = new Shortcut { Id = 2, Name = "B", PowerOn = true, CreatedAtUtc = DateTime.UtcNow };
        var c = new Shortcut { Id = 3, Name = "C", PowerOn = true, CreatedAtUtc = DateTime.UtcNow };
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut> { a, b, c });
        var service = new ShortcutService(repository.Object, new Mock<IDeviceControlService>().Object, TimeProvider.System);

        var eligible = await service.ListEligibleNextShortcutsAsync(forShortcutId: 3);

        Assert.Equal([1], eligible.Select(s => s.Id));
    }

    [Fact]
    public async Task ListEligibleNextShortcutsAsync_ExcludesSelf()
    {
        var a = new Shortcut { Id = 1, Name = "A", PowerOn = true, CreatedAtUtc = DateTime.UtcNow };
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut> { a });
        var service = new ShortcutService(repository.Object, new Mock<IDeviceControlService>().Object, TimeProvider.System);

        var eligible = await service.ListEligibleNextShortcutsAsync(forShortcutId: 1);

        Assert.Empty(eligible);
    }

    [Fact]
    public async Task ListEligibleNextShortcutsAsync_ForNewShortcut_OffersAnyShortcutWithRoomInItsChain()
    {
        var a = new Shortcut { Id = 1, Name = "A", PowerOn = true, CreatedAtUtc = DateTime.UtcNow, NextShortcutId = 2 };
        var b = new Shortcut { Id = 2, Name = "B", PowerOn = true, CreatedAtUtc = DateTime.UtcNow };
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut> { a, b });
        var service = new ShortcutService(repository.Object, new Mock<IDeviceControlService>().Object, TimeProvider.System);

        var eligible = await service.ListEligibleNextShortcutsAsync(forShortcutId: null);

        Assert.DoesNotContain(eligible, s => s.Id == 2); // B already has a predecessor (A).
        Assert.Contains(eligible, s => s.Id == 1); // A has no predecessor - still eligible.
    }

    // --- ApplyShortcutAsync ---

    [Fact]
    public async Task ApplyShortcutAsync_Throws_WhenShortcutMissing()
    {
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut>());
        var deviceControl = new Mock<IDeviceControlService>();
        var service = new ShortcutService(repository.Object, deviceControl.Object, TimeProvider.System);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ApplyShortcutAsync(1));
    }

    [Fact]
    public async Task ApplyShortcutAsync_TurnsOffAndSkipsFurtherCommands_WhenPowerOffShortcut()
    {
        var shortcut = new Shortcut
        {
            Id = 1,
            Name = "Bedtime",
            Targets = [new ShortcutTarget { DeviceSku = Sku, DeviceId = DeviceId }],
            PowerOn = false,
            Brightness = 50,
            CreatedAtUtc = DateTime.UtcNow
        };
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut> { shortcut });
        var deviceControl = new Mock<IDeviceControlService>();
        var service = new ShortcutService(repository.Object, deviceControl.Object, TimeProvider.System);

        await service.ApplyShortcutAsync(1);

        deviceControl.Verify(d => d.TurnOffAsync(Sku, DeviceId, It.IsAny<CancellationToken>()), Times.Once);
        deviceControl.Verify(d => d.SetBrightnessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApplyShortcutAsync_AppliesPowerBrightnessAndColor_WhenPowerOnShortcut()
    {
        var color = new RgbColor(10, 20, 30);
        var shortcut = new Shortcut
        {
            Id = 2,
            Name = "Movie Mode",
            Targets = [new ShortcutTarget { DeviceSku = Sku, DeviceId = DeviceId }],
            PowerOn = true,
            Brightness = 40,
            ColorRgbPacked = color.ToPackedInt(),
            CreatedAtUtc = DateTime.UtcNow
        };
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut> { shortcut });
        var deviceControl = new Mock<IDeviceControlService>();
        var service = new ShortcutService(repository.Object, deviceControl.Object, TimeProvider.System);

        await service.ApplyShortcutAsync(2);

        deviceControl.Verify(d => d.TurnOnAsync(Sku, DeviceId, It.IsAny<CancellationToken>()), Times.Once);
        deviceControl.Verify(d => d.SetBrightnessAsync(Sku, DeviceId, 40, It.IsAny<CancellationToken>()), Times.Once);
        deviceControl.Verify(d => d.SetColorAsync(Sku, DeviceId, color, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyShortcutAsync_AppliesToEveryTarget_WhenShortcutHasMultipleDevices()
    {
        var shortcut = new Shortcut
        {
            Id = 3,
            Name = "All Off",
            Targets =
            [
                new ShortcutTarget { DeviceSku = Sku, DeviceId = DeviceId },
                new ShortcutTarget { DeviceSku = Sku2, DeviceId = DeviceId2 }
            ],
            PowerOn = false,
            CreatedAtUtc = DateTime.UtcNow
        };
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut> { shortcut });
        var deviceControl = new Mock<IDeviceControlService>();
        var service = new ShortcutService(repository.Object, deviceControl.Object, TimeProvider.System);

        await service.ApplyShortcutAsync(3);

        deviceControl.Verify(d => d.TurnOffAsync(Sku, DeviceId, It.IsAny<CancellationToken>()), Times.Once);
        deviceControl.Verify(d => d.TurnOffAsync(Sku2, DeviceId2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyShortcutAsync_ContinuesPastAFailingTarget_AndReportsPartialFailure()
    {
        var shortcut = new Shortcut
        {
            Id = 4,
            Name = "All Off",
            Targets =
            [
                new ShortcutTarget { DeviceSku = Sku, DeviceId = DeviceId },
                new ShortcutTarget { DeviceSku = Sku2, DeviceId = DeviceId2 },
                new ShortcutTarget { DeviceSku = Sku3, DeviceId = DeviceId3 }
            ],
            PowerOn = false,
            CreatedAtUtc = DateTime.UtcNow
        };
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut> { shortcut });
        var deviceControl = new Mock<IDeviceControlService>();
        deviceControl.Setup(d => d.TurnOffAsync(Sku2, DeviceId2, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Device is offline."));
        var service = new ShortcutService(repository.Object, deviceControl.Object, TimeProvider.System);

        var ex = await Assert.ThrowsAsync<ShortcutApplyException>(() => service.ApplyShortcutAsync(4));

        // The failing middle target must not prevent the third (or the first) from being attempted.
        deviceControl.Verify(d => d.TurnOffAsync(Sku, DeviceId, It.IsAny<CancellationToken>()), Times.Once);
        deviceControl.Verify(d => d.TurnOffAsync(Sku2, DeviceId2, It.IsAny<CancellationToken>()), Times.Once);
        deviceControl.Verify(d => d.TurnOffAsync(Sku3, DeviceId3, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(2, ex.SucceededCount);
        Assert.Equal(3, ex.TotalCount);
        var failure = Assert.Single(ex.Failures);
        Assert.Equal(DeviceId2, failure.DeviceId);
        Assert.Equal(4, failure.ShortcutId);
        Assert.Equal("All Off", failure.ShortcutName);
    }

    // --- ApplyShortcutAsync: chains ---

    [Fact]
    public async Task ApplyShortcutAsync_RunsEveryStepInChainOrder()
    {
        var a = new Shortcut
        {
            Id = 1, Name = "A", PowerOn = true, CreatedAtUtc = DateTime.UtcNow, NextShortcutId = 2,
            Targets = [new ShortcutTarget { DeviceSku = Sku, DeviceId = DeviceId }]
        };
        var b = new Shortcut
        {
            Id = 2, Name = "B", PowerOn = false, CreatedAtUtc = DateTime.UtcNow,
            Targets = [new ShortcutTarget { DeviceSku = Sku2, DeviceId = DeviceId2 }]
        };
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut> { a, b });
        var deviceControl = new Mock<IDeviceControlService>();
        var callOrder = new List<string>();
        deviceControl.Setup(d => d.TurnOnAsync(Sku, DeviceId, It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("A-on")).Returns(Task.CompletedTask);
        deviceControl.Setup(d => d.TurnOffAsync(Sku2, DeviceId2, It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("B-off")).Returns(Task.CompletedTask);
        var service = new ShortcutService(repository.Object, deviceControl.Object, TimeProvider.System);

        await service.ApplyShortcutAsync(1);

        Assert.Equal(["A-on", "B-off"], callOrder);
    }

    [Fact]
    public async Task ApplyShortcutAsync_WaitsTheConfiguredDelayBeforeTheNextStep()
    {
        var a = new Shortcut
        {
            Id = 1, Name = "A", PowerOn = false, CreatedAtUtc = DateTime.UtcNow,
            NextShortcutId = 2, NextShortcutDelaySeconds = 10,
            Targets = [new ShortcutTarget { DeviceSku = Sku, DeviceId = DeviceId }]
        };
        var b = new Shortcut
        {
            Id = 2, Name = "B", PowerOn = false, CreatedAtUtc = DateTime.UtcNow,
            Targets = [new ShortcutTarget { DeviceSku = Sku2, DeviceId = DeviceId2 }]
        };
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut> { a, b });
        var deviceControl = new Mock<IDeviceControlService>();
        var timeProvider = new FakeTimeProvider();
        var service = new ShortcutService(repository.Object, deviceControl.Object, timeProvider);

        // Every await up to the delay resolves synchronously (Moq's completed-task mocks), so this
        // call suspends exactly at Task.Delay - safe to advance the clock immediately afterward.
        var applyTask = service.ApplyShortcutAsync(1);

        deviceControl.Verify(d => d.TurnOffAsync(Sku2, DeviceId2, It.IsAny<CancellationToken>()), Times.Never);

        timeProvider.Advance(TimeSpan.FromSeconds(10));
        await applyTask;

        deviceControl.Verify(d => d.TurnOffAsync(Sku, DeviceId, It.IsAny<CancellationToken>()), Times.Once);
        deviceControl.Verify(d => d.TurnOffAsync(Sku2, DeviceId2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyShortcutAsync_DoesNotDelayAfterTheLastStep()
    {
        // A configured delay with no NextShortcutId must be ignored - if the code delayed here
        // anyway, this test would hang until WaitAsync's timeout fires, since nothing ever
        // advances the FakeTimeProvider.
        var a = new Shortcut
        {
            Id = 1, Name = "A", PowerOn = false, CreatedAtUtc = DateTime.UtcNow, NextShortcutDelaySeconds = 10,
            Targets = [new ShortcutTarget { DeviceSku = Sku, DeviceId = DeviceId }]
        };
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut> { a });
        var deviceControl = new Mock<IDeviceControlService>();
        var timeProvider = new FakeTimeProvider();
        var service = new ShortcutService(repository.Object, deviceControl.Object, timeProvider);

        await service.ApplyShortcutAsync(1).WaitAsync(TimeSpan.FromSeconds(5));

        deviceControl.Verify(d => d.TurnOffAsync(Sku, DeviceId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyShortcutAsync_ContinuesToLaterSteps_WhenAnEarlierStepFails_AndAttributesFailuresToTheirStep()
    {
        var a = new Shortcut
        {
            Id = 1, Name = "Step A", PowerOn = false, CreatedAtUtc = DateTime.UtcNow, NextShortcutId = 2,
            Targets = [new ShortcutTarget { DeviceSku = Sku, DeviceId = DeviceId }]
        };
        var b = new Shortcut
        {
            Id = 2, Name = "Step B", PowerOn = false, CreatedAtUtc = DateTime.UtcNow,
            Targets = [new ShortcutTarget { DeviceSku = Sku2, DeviceId = DeviceId2 }]
        };
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut> { a, b });
        var deviceControl = new Mock<IDeviceControlService>();
        deviceControl.Setup(d => d.TurnOffAsync(Sku, DeviceId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Device is offline."));
        var service = new ShortcutService(repository.Object, deviceControl.Object, TimeProvider.System);

        var ex = await Assert.ThrowsAsync<ShortcutApplyException>(() => service.ApplyShortcutAsync(1));

        deviceControl.Verify(d => d.TurnOffAsync(Sku2, DeviceId2, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(1, ex.SucceededCount);
        Assert.Equal(2, ex.TotalCount);
        var failure = Assert.Single(ex.Failures);
        Assert.Equal(DeviceId, failure.DeviceId);
        Assert.Equal(1, failure.ShortcutId);
        Assert.Equal("Step A", failure.ShortcutName);
    }

    [Fact]
    public async Task ApplyShortcutAsync_StopsRemainingSteps_WhenCancelledDuringTheDelay()
    {
        var a = new Shortcut
        {
            Id = 1, Name = "A", PowerOn = false, CreatedAtUtc = DateTime.UtcNow,
            NextShortcutId = 2, NextShortcutDelaySeconds = 30,
            Targets = [new ShortcutTarget { DeviceSku = Sku, DeviceId = DeviceId }]
        };
        var b = new Shortcut
        {
            Id = 2, Name = "B", PowerOn = false, CreatedAtUtc = DateTime.UtcNow,
            Targets = [new ShortcutTarget { DeviceSku = Sku2, DeviceId = DeviceId2 }]
        };
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut> { a, b });
        var deviceControl = new Mock<IDeviceControlService>();
        var timeProvider = new FakeTimeProvider();
        var service = new ShortcutService(repository.Object, deviceControl.Object, timeProvider);
        using var cts = new CancellationTokenSource();

        var applyTask = service.ApplyShortcutAsync(1, cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => applyTask);
        deviceControl.Verify(d => d.TurnOffAsync(Sku2, DeviceId2, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApplyShortcutAsync_StartingMidChain_RunsOnlyFromThatPointOnward()
    {
        var a = new Shortcut
        {
            Id = 1, Name = "A", PowerOn = false, CreatedAtUtc = DateTime.UtcNow, NextShortcutId = 2,
            Targets = [new ShortcutTarget { DeviceSku = Sku, DeviceId = DeviceId }]
        };
        var b = new Shortcut
        {
            Id = 2, Name = "B", PowerOn = false, CreatedAtUtc = DateTime.UtcNow, NextShortcutId = 3,
            Targets = [new ShortcutTarget { DeviceSku = Sku2, DeviceId = DeviceId2 }]
        };
        var c = new Shortcut
        {
            Id = 3, Name = "C", PowerOn = false, CreatedAtUtc = DateTime.UtcNow,
            Targets = [new ShortcutTarget { DeviceSku = Sku3, DeviceId = DeviceId3 }]
        };
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut> { a, b, c });
        var deviceControl = new Mock<IDeviceControlService>();
        var service = new ShortcutService(repository.Object, deviceControl.Object, TimeProvider.System);

        await service.ApplyShortcutAsync(2);

        deviceControl.Verify(d => d.TurnOffAsync(Sku, DeviceId, It.IsAny<CancellationToken>()), Times.Never);
        deviceControl.Verify(d => d.TurnOffAsync(Sku2, DeviceId2, It.IsAny<CancellationToken>()), Times.Once);
        deviceControl.Verify(d => d.TurnOffAsync(Sku3, DeviceId3, It.IsAny<CancellationToken>()), Times.Once);
    }
}
