using GoveeController.Application.Devices;
using GoveeController.Domain.Devices;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using Xunit;

namespace GoveeController.Application.Tests.Devices;

public class DeviceControlServiceTests
{
    private static readonly Device SampleDevice = new(
        Sku: "H6159",
        Id: "AA:BB:CC:DD:EE:FF:00:11",
        Name: "Desk Light",
        Type: "devices.types.light",
        Capabilities:
        [
            new DeviceCapability(CapabilityKind.PowerSwitch, "devices.capabilities.on_off", "powerSwitch"),
            new DeviceCapability(CapabilityKind.Brightness, "devices.capabilities.range", "brightness", 1, 100)
        ]);

    private static DeviceControlService CreateService(Mock<IGoveeApiClient> client) =>
        new(client.Object, new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task ListDevicesAsync_CachesResultAcrossCalls()
    {
        var client = new Mock<IGoveeApiClient>();
        client.Setup(c => c.GetDevicesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([SampleDevice]);
        var service = CreateService(client);

        await service.ListDevicesAsync();
        await service.ListDevicesAsync();

        client.Verify(c => c.GetDevicesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetStateAsync_CachesResultAcrossCalls()
    {
        var client = new Mock<IGoveeApiClient>();
        client.Setup(c => c.GetDeviceStateAsync(SampleDevice.Sku, SampleDevice.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LightState(true, 80, null, null));
        var service = CreateService(client);

        await service.GetStateAsync(SampleDevice.Sku, SampleDevice.Id);
        await service.GetStateAsync(SampleDevice.Sku, SampleDevice.Id);

        client.Verify(c => c.GetDeviceStateAsync(SampleDevice.Sku, SampleDevice.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TurnOnAsync_InvalidatesCachedState()
    {
        var client = new Mock<IGoveeApiClient>();
        client.SetupSequence(c => c.GetDeviceStateAsync(SampleDevice.Sku, SampleDevice.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LightState(false, null, null, null))
            .ReturnsAsync(new LightState(true, null, null, null));
        var service = CreateService(client);

        var before = await service.GetStateAsync(SampleDevice.Sku, SampleDevice.Id);
        await service.TurnOnAsync(SampleDevice.Sku, SampleDevice.Id);
        var after = await service.GetStateAsync(SampleDevice.Sku, SampleDevice.Id);

        Assert.False(before.PowerOn);
        Assert.True(after.PowerOn);
        client.Verify(c => c.SetPowerAsync(SampleDevice.Sku, SampleDevice.Id, true, It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(c => c.GetDeviceStateAsync(SampleDevice.Sku, SampleDevice.Id, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ListDevicesAsync_ForceRefresh_BypassesCache()
    {
        var client = new Mock<IGoveeApiClient>();
        client.Setup(c => c.GetDevicesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([SampleDevice]);
        var service = CreateService(client);

        await service.ListDevicesAsync();
        await service.ListDevicesAsync(forceRefresh: true);

        client.Verify(c => c.GetDevicesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetStateAsync_ForceRefresh_BypassesCache()
    {
        var client = new Mock<IGoveeApiClient>();
        client.Setup(c => c.GetDeviceStateAsync(SampleDevice.Sku, SampleDevice.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LightState(true, 80, null, null));
        var service = CreateService(client);

        await service.GetStateAsync(SampleDevice.Sku, SampleDevice.Id);
        await service.GetStateAsync(SampleDevice.Sku, SampleDevice.Id, forceRefresh: true);

        client.Verify(c => c.GetDeviceStateAsync(SampleDevice.Sku, SampleDevice.Id, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task TriggerSceneAsync_DelegatesToClient()
    {
        var client = new Mock<IGoveeApiClient>();
        var scene = new GoveeScene("Sunset", ParamId: 42, Id: 7);
        var service = CreateService(client);

        await service.TriggerSceneAsync(SampleDevice.Sku, SampleDevice.Id, scene);

        client.Verify(c => c.TriggerSceneAsync(SampleDevice.Sku, SampleDevice.Id, scene, It.IsAny<CancellationToken>()), Times.Once);
    }
}
