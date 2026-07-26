using GoveeController.Domain.Devices;
using Xunit;

namespace GoveeController.Application.Tests.Devices;

public class DeviceTests
{
    private static Device MakeDevice(string type, params CapabilityKind[] kinds) => new(
        Sku: "H6159",
        Id: "AA:BB:CC:DD:EE:FF:00:11",
        Name: "Test Device",
        Type: type,
        Capabilities: kinds.Select(k => new DeviceCapability(k, $"type.{k}", $"instance.{k}")).ToList());

    [Fact]
    public void IsIndividualLight_IsTrue_ForRealBulbType()
    {
        var device = MakeDevice("devices.types.light");

        Assert.True(device.IsIndividualLight);
    }

    [Theory]
    [InlineData("")]
    [InlineData("devices.types.other")]
    public void IsIndividualLight_IsFalse_ForGroupOrScenicOrUnknownTypes(string type)
    {
        // Govee's "Same-Model Group Control" and DreamView scenic group devices report an empty
        // Type string rather than "devices.types.light" — this is the real-world quirk this
        // property exists to encode, discovered during development against a real account.
        var device = MakeDevice(type);

        Assert.False(device.IsIndividualLight);
    }

    [Fact]
    public void SupportsX_ReflectsPresenceOfMatchingCapability()
    {
        var device = MakeDevice("devices.types.light", CapabilityKind.PowerSwitch, CapabilityKind.Brightness);

        Assert.True(device.SupportsPower);
        Assert.True(device.SupportsBrightness);
        Assert.False(device.SupportsColorRgb);
        Assert.False(device.SupportsColorTemperature);
        Assert.False(device.SupportsScenes);
    }

    [Fact]
    public void SupportsX_AreAllFalse_ForDeviceWithNoCapabilities()
    {
        var device = MakeDevice("devices.types.light");

        Assert.False(device.SupportsPower);
        Assert.False(device.SupportsBrightness);
        Assert.False(device.SupportsColorRgb);
        Assert.False(device.SupportsColorTemperature);
        Assert.False(device.SupportsScenes);
    }
}
