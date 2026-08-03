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

    // --- Composite shortcuts (COMPOSITE-SHORTCUTS-PLAN.md) ---

    // Builds one reference entry as the service stores it (owning shortcut id, referenced id,
    // delay, and order). ShortcutId here is the composite's own id.
    private static ShortcutReference Ref(int owningShortcutId, int referencedId, int delaySeconds = 0, int order = 0) =>
        new() { ShortcutId = owningShortcutId, ReferencedShortcutId = referencedId, DelaySeconds = delaySeconds, Order = order };

    private static Shortcut DeviceShortcut(int id, string name) => new()
    {
        Id = id,
        Name = name,
        PowerOn = true,
        CreatedAtUtc = DateTime.UtcNow,
        Targets = [new ShortcutTarget { DeviceSku = Sku, DeviceId = DeviceId }]
    };

    // --- CreateCompositeShortcutAsync ---

    [Fact]
    public async Task CreateCompositeShortcutAsync_StoresReferencedShortcutsInOrder()
    {
        var a = DeviceShortcut(1, "A");
        var b = DeviceShortcut(2, "B");
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut> { a, b });
        repository.Setup(r => r.AddAsync(It.IsAny<Shortcut>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Shortcut s, CancellationToken _) => s);
        var service = new ShortcutService(repository.Object, new Mock<IDeviceControlService>().Object, TimeProvider.System);

        var result = await service.CreateCompositeShortcutAsync(
            "Movie Night", [(2, 5), (1, 0)], nextShortcutId: null, nextShortcutDelaySeconds: 0);

        Assert.Empty(result.Targets);
        Assert.Equal(2, result.ReferencedShortcuts.Count);
        Assert.Equal(2, result.ReferencedShortcuts[0].ReferencedShortcutId);
        Assert.Equal(5, result.ReferencedShortcuts[0].DelaySeconds);
        Assert.Equal(0, result.ReferencedShortcuts[0].Order);
        Assert.Equal(1, result.ReferencedShortcuts[1].ReferencedShortcutId);
        Assert.Equal(1, result.ReferencedShortcuts[1].Order);
        repository.Verify(r => r.AddAsync(It.IsAny<Shortcut>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateCompositeShortcutAsync_Throws_WhenNoReferences()
    {
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut>());
        var service = new ShortcutService(repository.Object, new Mock<IDeviceControlService>().Object, TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateCompositeShortcutAsync(
            "Empty", [], nextShortcutId: null, nextShortcutDelaySeconds: 0));
    }

    [Fact]
    public async Task CreateCompositeShortcutAsync_Throws_WhenReferenceDoesNotExist()
    {
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut>());
        var service = new ShortcutService(repository.Object, new Mock<IDeviceControlService>().Object, TimeProvider.System);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CreateCompositeShortcutAsync(
            "Bad Ref", [(999, 0)], nextShortcutId: null, nextShortcutDelaySeconds: 0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(61)]
    public async Task CreateCompositeShortcutAsync_Throws_WhenDelayOutOfRange(int delaySeconds)
    {
        var a = DeviceShortcut(1, "A");
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut> { a });
        var service = new ShortcutService(repository.Object, new Mock<IDeviceControlService>().Object, TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateCompositeShortcutAsync(
            "Bad Delay", [(1, delaySeconds)], nextShortcutId: null, nextShortcutDelaySeconds: 0));
    }

    [Fact]
    public async Task UpdateCompositeShortcutAsync_Throws_WhenReferencingItself()
    {
        var composite = new Shortcut { Id = 1, Name = "Composite", PowerOn = false, CreatedAtUtc = DateTime.UtcNow };
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut> { composite });
        var service = new ShortcutService(repository.Object, new Mock<IDeviceControlService>().Object, TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateCompositeShortcutAsync(
            1, "Composite", [(1, 0)], nextShortcutId: null, nextShortcutDelaySeconds: 0));
    }

    [Fact]
    public async Task UpdateCompositeShortcutAsync_Throws_WhenReferenceWouldCreateCycle()
    {
        // A references B; making B also reference A would form a 2-cycle.
        var a = new Shortcut
        {
            Id = 1, Name = "A", PowerOn = false, CreatedAtUtc = DateTime.UtcNow,
            ReferencedShortcuts = [Ref(owningShortcutId: 1, referencedId: 2)]
        };
        var b = DeviceShortcut(2, "B");
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut> { a, b });
        var service = new ShortcutService(repository.Object, new Mock<IDeviceControlService>().Object, TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateCompositeShortcutAsync(
            2, "B", [(1, 0)], nextShortcutId: null, nextShortcutDelaySeconds: 0));
    }

    [Fact]
    public async Task UpdateCompositeShortcutAsync_Throws_WhenReferenceWouldCreateIndirectCycle()
    {
        // A (1) references B (2); B's chain runs C (3); C references A. Adding A -> B would close
        // a loop: A -> B -> C -> A.
        var a = new Shortcut { Id = 1, Name = "A", PowerOn = false, CreatedAtUtc = DateTime.UtcNow };
        var b = new Shortcut { Id = 2, Name = "B", PowerOn = true, CreatedAtUtc = DateTime.UtcNow, NextShortcutId = 3 };
        var c = new Shortcut
        {
            Id = 3, Name = "C", PowerOn = false, CreatedAtUtc = DateTime.UtcNow,
            ReferencedShortcuts = [Ref(owningShortcutId: 3, referencedId: 1)]
        };
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut> { a, b, c });
        var service = new ShortcutService(repository.Object, new Mock<IDeviceControlService>().Object, TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateCompositeShortcutAsync(
            1, "A", [(2, 0)], nextShortcutId: null, nextShortcutDelaySeconds: 0));
    }

    [Fact]
    public async Task UpdateShortcutAsync_Throws_WhenChainLinkWouldCycleThroughCompositeReference()
    {
        // A (1) references B (2); linking B to run A next would close a loop through the composite
        // graph (A -> B -> A) that the linear-chain cycle check alone cannot see.
        var a = new Shortcut
        {
            Id = 1, Name = "A", PowerOn = false, CreatedAtUtc = DateTime.UtcNow,
            ReferencedShortcuts = [Ref(owningShortcutId: 1, referencedId: 2)]
        };
        var b = DeviceShortcut(2, "B");
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut> { a, b });
        var service = new ShortcutService(repository.Object, new Mock<IDeviceControlService>().Object, TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateShortcutAsync(
            2, "B", OneTarget, powerOn: true, brightness: null, color: null, colorTemperatureKelvin: null,
            nextShortcutId: 1, nextShortcutDelaySeconds: 0));
    }

    [Fact]
    public async Task CreateCompositeShortcutAsync_CanHaveItsOwnChainLink()
    {
        var a = DeviceShortcut(1, "A");
        var b = DeviceShortcut(2, "B");
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut> { a, b });
        repository.Setup(r => r.AddAsync(It.IsAny<Shortcut>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Shortcut s, CancellationToken _) => s);
        var service = new ShortcutService(repository.Object, new Mock<IDeviceControlService>().Object, TimeProvider.System);

        var result = await service.CreateCompositeShortcutAsync(
            "Composite", [(1, 0)], nextShortcutId: 2, nextShortcutDelaySeconds: 10);

        Assert.Equal(2, result.NextShortcutId);
        Assert.Equal(10, result.NextShortcutDelaySeconds);
        Assert.Single(result.ReferencedShortcuts);
    }

    [Fact]
    public async Task UpdateCompositeShortcutAsync_ReplacesReferencedShortcuts()
    {
        Shortcut? saved = null;
        var a = DeviceShortcut(1, "A");
        var b = DeviceShortcut(2, "B");
        var c = DeviceShortcut(3, "C");
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut> { a, b, c });
        repository.Setup(r => r.UpdateAsync(It.IsAny<Shortcut>(), It.IsAny<CancellationToken>()))
            .Callback<Shortcut, CancellationToken>((s, _) => saved = s)
            .Returns(Task.CompletedTask);
        var service = new ShortcutService(repository.Object, new Mock<IDeviceControlService>().Object, TimeProvider.System);

        await service.UpdateCompositeShortcutAsync(
            7, "Renamed Composite", [(3, 2), (1, 0)], nextShortcutId: null, nextShortcutDelaySeconds: 0);

        Assert.NotNull(saved);
        Assert.Equal(7, saved!.Id);
        Assert.Equal("Renamed Composite", saved.Name);
        Assert.Equal([3, 1], saved.ReferencedShortcuts.Select(r => r.ReferencedShortcutId));
        Assert.Equal([0, 1], saved.ReferencedShortcuts.Select(r => r.Order));
        Assert.Equal([2, 0], saved.ReferencedShortcuts.Select(r => r.DelaySeconds));
    }

    // --- ListEligibleReferencedShortcutsAsync ---

    [Fact]
    public async Task ListEligibleReferencedShortcutsAsync_ExcludesSelf()
    {
        var a = DeviceShortcut(1, "A");
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut> { a });
        var service = new ShortcutService(repository.Object, new Mock<IDeviceControlService>().Object, TimeProvider.System);

        var eligible = await service.ListEligibleReferencedShortcutsAsync(forShortcutId: 1);

        Assert.Empty(eligible);
    }

    [Fact]
    public async Task ListEligibleReferencedShortcutsAsync_ExcludesShortcutWhoseDownstreamWouldCycle()
    {
        // C (3) references A (1). While editing A, C must not be offered — adding A -> C would be
        // a cycle (A -> C -> A). An unrelated shortcut B stays eligible.
        var a = new Shortcut { Id = 1, Name = "A", PowerOn = false, CreatedAtUtc = DateTime.UtcNow };
        var b = DeviceShortcut(2, "B");
        var c = new Shortcut
        {
            Id = 3, Name = "C", PowerOn = false, CreatedAtUtc = DateTime.UtcNow,
            ReferencedShortcuts = [Ref(owningShortcutId: 3, referencedId: 1)]
        };
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut> { a, b, c });
        var service = new ShortcutService(repository.Object, new Mock<IDeviceControlService>().Object, TimeProvider.System);

        var eligible = await service.ListEligibleReferencedShortcutsAsync(forShortcutId: 1);

        Assert.DoesNotContain(eligible, s => s.Id == 1); // self
        Assert.DoesNotContain(eligible, s => s.Id == 3); // C's downstream reaches A -> would cycle
        Assert.Contains(eligible, s => s.Id == 2); // B is unrelated and safe to reference
    }

    // --- ApplyShortcutAsync: composites ---

    [Fact]
    public async Task ApplyShortcutAsync_RunsReferencedShortcutsInOrder()
    {
        var a = new Shortcut
        {
            Id = 1, Name = "Composite", PowerOn = false, CreatedAtUtc = DateTime.UtcNow,
            ReferencedShortcuts = [Ref(owningShortcutId: 1, referencedId: 2, order: 0), Ref(owningShortcutId: 1, referencedId: 3, order: 1)]
        };
        var b = DeviceShortcut(2, "B");
        var c = new Shortcut
        {
            Id = 3, Name = "C", PowerOn = false, CreatedAtUtc = DateTime.UtcNow,
            Targets = [new ShortcutTarget { DeviceSku = Sku2, DeviceId = DeviceId2 }]
        };
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut> { a, b, c });
        var deviceControl = new Mock<IDeviceControlService>();
        var callOrder = new List<string>();
        deviceControl.Setup(d => d.TurnOnAsync(Sku, DeviceId, It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("B-on")).Returns(Task.CompletedTask);
        deviceControl.Setup(d => d.TurnOffAsync(Sku2, DeviceId2, It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("C-off")).Returns(Task.CompletedTask);
        var service = new ShortcutService(repository.Object, deviceControl.Object, TimeProvider.System);

        await service.ApplyShortcutAsync(1);

        Assert.Equal(["B-on", "C-off"], callOrder);
    }

    [Fact]
    public async Task ApplyShortcutAsync_RunsReferencedShortcutChains()
    {
        // Composite (1) references B (2); B's chain runs C (3). Applying the composite must run B
        // and then C.
        var composite = new Shortcut
        {
            Id = 1, Name = "Composite", PowerOn = false, CreatedAtUtc = DateTime.UtcNow,
            ReferencedShortcuts = [Ref(owningShortcutId: 1, referencedId: 2)]
        };
        var b = new Shortcut
        {
            Id = 2, Name = "B", PowerOn = false, CreatedAtUtc = DateTime.UtcNow, NextShortcutId = 3,
            Targets = [new ShortcutTarget { DeviceSku = Sku, DeviceId = DeviceId }]
        };
        var c = new Shortcut
        {
            Id = 3, Name = "C", PowerOn = false, CreatedAtUtc = DateTime.UtcNow,
            Targets = [new ShortcutTarget { DeviceSku = Sku2, DeviceId = DeviceId2 }]
        };
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut> { composite, b, c });
        var deviceControl = new Mock<IDeviceControlService>();
        var service = new ShortcutService(repository.Object, deviceControl.Object, TimeProvider.System);

        await service.ApplyShortcutAsync(1);

        deviceControl.Verify(d => d.TurnOffAsync(Sku, DeviceId, It.IsAny<CancellationToken>()), Times.Once);
        deviceControl.Verify(d => d.TurnOffAsync(Sku2, DeviceId2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyShortcutAsync_ContinuesToNextReferencedShortcut_WhenAnEarlierOneFails()
    {
        var composite = new Shortcut
        {
            Id = 1, Name = "Composite", PowerOn = false, CreatedAtUtc = DateTime.UtcNow,
            ReferencedShortcuts = [Ref(owningShortcutId: 1, referencedId: 2, order: 0), Ref(owningShortcutId: 1, referencedId: 3, order: 1)]
        };
        var b = new Shortcut
        {
            Id = 2, Name = "B", PowerOn = false, CreatedAtUtc = DateTime.UtcNow,
            Targets = [new ShortcutTarget { DeviceSku = Sku, DeviceId = DeviceId }]
        };
        var c = new Shortcut
        {
            Id = 3, Name = "C", PowerOn = false, CreatedAtUtc = DateTime.UtcNow,
            Targets = [new ShortcutTarget { DeviceSku = Sku2, DeviceId = DeviceId2 }]
        };
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut> { composite, b, c });
        var deviceControl = new Mock<IDeviceControlService>();
        deviceControl.Setup(d => d.TurnOffAsync(Sku, DeviceId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Device is offline."));
        var service = new ShortcutService(repository.Object, deviceControl.Object, TimeProvider.System);

        var ex = await Assert.ThrowsAsync<ShortcutApplyException>(() => service.ApplyShortcutAsync(1));

        // B's failing target must not prevent C from running, and totals count device targets
        // across the whole composite tree (B: 1 + C: 1 = 2).
        deviceControl.Verify(d => d.TurnOffAsync(Sku2, DeviceId2, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(1, ex.SucceededCount);
        Assert.Equal(2, ex.TotalCount);
        var failure = Assert.Single(ex.Failures);
        Assert.Equal(DeviceId, failure.DeviceId);
        Assert.Equal(2, failure.ShortcutId);
        Assert.Equal("B", failure.ShortcutName);
    }

    [Fact]
    public async Task ApplyShortcutAsync_WaitsReferenceDelayBetweenReferencedShortcuts()
    {
        var composite = new Shortcut
        {
            Id = 1, Name = "Composite", PowerOn = false, CreatedAtUtc = DateTime.UtcNow,
            ReferencedShortcuts = [Ref(owningShortcutId: 1, referencedId: 2, delaySeconds: 10, order: 0), Ref(owningShortcutId: 1, referencedId: 3, order: 1)]
        };
        var b = new Shortcut
        {
            Id = 2, Name = "B", PowerOn = false, CreatedAtUtc = DateTime.UtcNow,
            Targets = [new ShortcutTarget { DeviceSku = Sku, DeviceId = DeviceId }]
        };
        var c = new Shortcut
        {
            Id = 3, Name = "C", PowerOn = false, CreatedAtUtc = DateTime.UtcNow,
            Targets = [new ShortcutTarget { DeviceSku = Sku2, DeviceId = DeviceId2 }]
        };
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut> { composite, b, c });
        var deviceControl = new Mock<IDeviceControlService>();
        var timeProvider = new FakeTimeProvider();
        var service = new ShortcutService(repository.Object, deviceControl.Object, timeProvider);

        // The apply suspends exactly at the first reference's Task.Delay (all Moq calls resolve
        // synchronously), so the second referenced shortcut must not run until the clock advances.
        var applyTask = service.ApplyShortcutAsync(1);

        deviceControl.Verify(d => d.TurnOffAsync(Sku2, DeviceId2, It.IsAny<CancellationToken>()), Times.Never);

        timeProvider.Advance(TimeSpan.FromSeconds(10));
        await applyTask;

        deviceControl.Verify(d => d.TurnOffAsync(Sku, DeviceId, It.IsAny<CancellationToken>()), Times.Once);
        deviceControl.Verify(d => d.TurnOffAsync(Sku2, DeviceId2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyShortcutAsync_SkipsMissingReferencedShortcut_AndReportsIt()
    {
        // The composite references shortcut 2, which no longer exists (its reference row was left
        // behind with a null FK). Applying must not throw mid-way; it reports the missing step.
        var composite = new Shortcut
        {
            Id = 1, Name = "Composite", PowerOn = false, CreatedAtUtc = DateTime.UtcNow,
            ReferencedShortcuts = [new ShortcutReference { ShortcutId = 1, ReferencedShortcutId = 2, DelaySeconds = 0, Order = 0 }]
        };
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Shortcut> { composite });
        var deviceControl = new Mock<IDeviceControlService>();
        var service = new ShortcutService(repository.Object, deviceControl.Object, TimeProvider.System);

        var ex = await Assert.ThrowsAsync<ShortcutApplyException>(() => service.ApplyShortcutAsync(1));

        Assert.Empty(deviceControl.Invocations);
        var failure = Assert.Single(ex.Failures);
        Assert.Equal("Referenced shortcut no longer exists.", failure.ErrorMessage);
        Assert.Equal(1, failure.ShortcutId);
    }
}
