using GoveeController.Application.Devices;
using GoveeController.Application.Shortcuts;
using GoveeController.Domain.Devices;
using GoveeController.Domain.Shortcuts;
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
        var service = new ShortcutService(repository.Object, deviceControl.Object);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateShortcutAsync(
            "Movie Mode", OneTarget, powerOn: true, brightness: 50,
            color: new RgbColor(255, 0, 0), colorTemperatureKelvin: 4000));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task CreateShortcutAsync_Throws_WhenBrightnessOutOfRange(int brightness)
    {
        var repository = new Mock<IShortcutRepository>();
        var deviceControl = new Mock<IDeviceControlService>();
        var service = new ShortcutService(repository.Object, deviceControl.Object);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateShortcutAsync(
            "Bad Brightness", OneTarget, powerOn: true, brightness: brightness, color: null, colorTemperatureKelvin: null));
    }

    [Theory]
    [InlineData(1999)]
    [InlineData(9001)]
    public async Task CreateShortcutAsync_Throws_WhenColorTemperatureOutOfRange(int kelvin)
    {
        var repository = new Mock<IShortcutRepository>();
        var deviceControl = new Mock<IDeviceControlService>();
        var service = new ShortcutService(repository.Object, deviceControl.Object);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateShortcutAsync(
            "Bad Temp", OneTarget, powerOn: true, brightness: null, color: null, colorTemperatureKelvin: kelvin));
    }

    [Fact]
    public async Task CreateShortcutAsync_Throws_WhenNoTargetsSpecified()
    {
        var repository = new Mock<IShortcutRepository>();
        var deviceControl = new Mock<IDeviceControlService>();
        var service = new ShortcutService(repository.Object, deviceControl.Object);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateShortcutAsync(
            "Empty", targets: [], powerOn: true, brightness: null, color: null, colorTemperatureKelvin: null));
    }

    [Fact]
    public async Task CreateShortcutAsync_PacksColorAndPersistsAllTargets()
    {
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.AddAsync(It.IsAny<Shortcut>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Shortcut s, CancellationToken _) => s);
        var deviceControl = new Mock<IDeviceControlService>();
        var service = new ShortcutService(repository.Object, deviceControl.Object);

        var color = new RgbColor(255, 128, 0);
        var result = await service.CreateShortcutAsync("Sunset", TwoTargets, true, 60, color, null);

        Assert.Equal(color.ToPackedInt(), result.ColorRgbPacked);
        Assert.Null(result.ColorTemperatureKelvin);
        Assert.Equal(2, result.Targets.Count);
        Assert.Contains(result.Targets, t => t.DeviceId == DeviceId);
        Assert.Contains(result.Targets, t => t.DeviceId == DeviceId2);
        repository.Verify(r => r.AddAsync(It.IsAny<Shortcut>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateShortcutAsync_Throws_WhenColorAndTemperatureBothSpecified()
    {
        var repository = new Mock<IShortcutRepository>();
        var deviceControl = new Mock<IDeviceControlService>();
        var service = new ShortcutService(repository.Object, deviceControl.Object);

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateShortcutAsync(
            1, "Movie Mode", OneTarget, powerOn: true, brightness: 50,
            color: new RgbColor(255, 0, 0), colorTemperatureKelvin: 4000));
    }

    [Fact]
    public async Task UpdateShortcutAsync_Throws_WhenNoTargetsSpecified()
    {
        var repository = new Mock<IShortcutRepository>();
        var deviceControl = new Mock<IDeviceControlService>();
        var service = new ShortcutService(repository.Object, deviceControl.Object);

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateShortcutAsync(
            1, "Empty", targets: [], powerOn: true, brightness: null, color: null, colorTemperatureKelvin: null));
    }

    [Fact]
    public async Task UpdateShortcutAsync_PassesIdAndPackedColorToRepository()
    {
        Shortcut? saved = null;
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.UpdateAsync(It.IsAny<Shortcut>(), It.IsAny<CancellationToken>()))
            .Callback<Shortcut, CancellationToken>((s, _) => saved = s)
            .Returns(Task.CompletedTask);
        var deviceControl = new Mock<IDeviceControlService>();
        var service = new ShortcutService(repository.Object, deviceControl.Object);

        var color = new RgbColor(1, 2, 3);
        await service.UpdateShortcutAsync(7, "Renamed", TwoTargets, false, null, color, null);

        repository.Verify(r => r.UpdateAsync(It.IsAny<Shortcut>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(saved);
        Assert.Equal(7, saved!.Id);
        Assert.Equal("Renamed", saved.Name);
        Assert.False(saved.PowerOn);
        Assert.Equal(color.ToPackedInt(), saved.ColorRgbPacked);
        Assert.Equal(2, saved.Targets.Count);
    }

    [Fact]
    public async Task ApplyShortcutAsync_Throws_WhenShortcutMissing()
    {
        var repository = new Mock<IShortcutRepository>();
        repository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync((Shortcut?)null);
        var deviceControl = new Mock<IDeviceControlService>();
        var service = new ShortcutService(repository.Object, deviceControl.Object);

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
        repository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(shortcut);
        var deviceControl = new Mock<IDeviceControlService>();
        var service = new ShortcutService(repository.Object, deviceControl.Object);

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
        repository.Setup(r => r.GetByIdAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(shortcut);
        var deviceControl = new Mock<IDeviceControlService>();
        var service = new ShortcutService(repository.Object, deviceControl.Object);

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
        repository.Setup(r => r.GetByIdAsync(3, It.IsAny<CancellationToken>())).ReturnsAsync(shortcut);
        var deviceControl = new Mock<IDeviceControlService>();
        var service = new ShortcutService(repository.Object, deviceControl.Object);

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
        repository.Setup(r => r.GetByIdAsync(4, It.IsAny<CancellationToken>())).ReturnsAsync(shortcut);
        var deviceControl = new Mock<IDeviceControlService>();
        deviceControl.Setup(d => d.TurnOffAsync(Sku2, DeviceId2, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Device is offline."));
        var service = new ShortcutService(repository.Object, deviceControl.Object);

        var ex = await Assert.ThrowsAsync<ShortcutApplyException>(() => service.ApplyShortcutAsync(4));

        // The failing middle target must not prevent the third (or the first) from being attempted.
        deviceControl.Verify(d => d.TurnOffAsync(Sku, DeviceId, It.IsAny<CancellationToken>()), Times.Once);
        deviceControl.Verify(d => d.TurnOffAsync(Sku2, DeviceId2, It.IsAny<CancellationToken>()), Times.Once);
        deviceControl.Verify(d => d.TurnOffAsync(Sku3, DeviceId3, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(2, ex.SucceededCount);
        Assert.Equal(3, ex.TotalCount);
        Assert.Equal(DeviceId2, Assert.Single(ex.Failures).DeviceId);
    }
}
