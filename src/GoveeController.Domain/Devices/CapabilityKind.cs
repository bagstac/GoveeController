namespace GoveeController.Domain.Devices;

/// <summary>
/// The set of Govee device capabilities this application understands and can render controls for.
/// Govee's API exposes many capability types (segmented color, music mode, work modes, etc.); this
/// application intentionally only models the ones needed for basic light control plus scenes.
/// </summary>
public enum CapabilityKind
{
    /// <summary>Capability type/instance combination this app does not have explicit support for.</summary>
    Unsupported = 0,

    /// <summary>Maps to Govee capability type "devices.capabilities.on_off", instance "powerSwitch".</summary>
    PowerSwitch,

    /// <summary>Maps to Govee capability type "devices.capabilities.range", instance "brightness".</summary>
    Brightness,

    /// <summary>Maps to Govee capability type "devices.capabilities.color_setting", instance "colorRgb".</summary>
    ColorRgb,

    /// <summary>Maps to Govee capability type "devices.capabilities.color_setting", instance "colorTemperatureK".</summary>
    ColorTemperature,

    /// <summary>Maps to Govee capability type "devices.capabilities.dynamic_scene", instance "lightScene".</summary>
    DynamicScene
}
