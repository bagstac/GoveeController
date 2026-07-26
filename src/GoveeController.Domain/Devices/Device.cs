namespace GoveeController.Domain.Devices;

/// <summary>
/// A Govee smart light device belonging to the authenticated account, as reported by the
/// "Get devices" endpoint, together with the capabilities this app can control on it.
/// </summary>
/// <param name="Sku">The Govee product model number (e.g. "H6159"). Required alongside <see cref="Id"/> for every control call.</param>
/// <param name="Id">The device's unique identifier (typically a MAC-address-shaped string).</param>
/// <param name="Name">The user-assigned device name from the Govee Home app.</param>
/// <param name="Type">
/// The Govee device type string (e.g. "devices.types.light"). Govee leaves this blank for
/// multi-device control surfaces (same-model groups, DreamView scenic groups) rather than a
/// single physical bulb — see <see cref="IsIndividualLight"/>.
/// </param>
/// <param name="Capabilities">The normalized set of controllable capabilities this app recognizes for the device.</param>
public sealed record Device(
    string Sku,
    string Id,
    string Name,
    string Type,
    IReadOnlyList<DeviceCapability> Capabilities)
{
    /// <summary>Govee's device type string for a single physical light bulb.</summary>
    private const string LightTypeName = "devices.types.light";

    /// <summary>
    /// True if this represents one physical light bulb (<see cref="Type"/> is "devices.types.light").
    /// False for Govee's group/scenic control surfaces (e.g. "Same-Model Group Control", DreamView
    /// scenic groups), which report an empty <see cref="Type"/> instead.
    /// </summary>
    public bool IsIndividualLight => Type == LightTypeName;

    /// <summary>True if the device exposes an on/off switch.</summary>
    public bool SupportsPower => Capabilities.Any(c => c.Kind == CapabilityKind.PowerSwitch);

    /// <summary>True if the device exposes a dimmable brightness range.</summary>
    public bool SupportsBrightness => Capabilities.Any(c => c.Kind == CapabilityKind.Brightness);

    /// <summary>True if the device exposes RGB color control.</summary>
    public bool SupportsColorRgb => Capabilities.Any(c => c.Kind == CapabilityKind.ColorRgb);

    /// <summary>True if the device exposes white color-temperature control.</summary>
    public bool SupportsColorTemperature => Capabilities.Any(c => c.Kind == CapabilityKind.ColorTemperature);

    /// <summary>True if the device exposes Govee's built-in dynamic light scenes.</summary>
    public bool SupportsScenes => Capabilities.Any(c => c.Kind == CapabilityKind.DynamicScene);
}
