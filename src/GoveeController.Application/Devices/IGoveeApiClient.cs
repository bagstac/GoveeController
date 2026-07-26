using GoveeController.Domain.Devices;

namespace GoveeController.Application.Devices;

/// <summary>
/// Thin abstraction over the Govee Cloud API (https://developer.govee.com), scoped to exactly the
/// operations this application needs. Implemented by the Infrastructure layer's HTTP client;
/// kept as an interface here so the Application layer's use-case services can be unit tested
/// without making real network calls.
/// </summary>
public interface IGoveeApiClient
{
    /// <summary>Fetches every device registered to the account, via "Get devices".</summary>
    Task<IReadOnlyList<Device>> GetDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>Fetches the current power/brightness/color state of one device, via "Get device state".</summary>
    Task<LightState> GetDeviceStateAsync(string sku, string deviceId, CancellationToken cancellationToken = default);

    /// <summary>Turns a device on or off, via the on_off/powerSwitch control capability.</summary>
    Task SetPowerAsync(string sku, string deviceId, bool powerOn, CancellationToken cancellationToken = default);

    /// <summary>Sets a device's brightness (1-100), via the range/brightness control capability.</summary>
    Task SetBrightnessAsync(string sku, string deviceId, int brightness, CancellationToken cancellationToken = default);

    /// <summary>Sets a device's RGB color, via the color_setting/colorRgb control capability.</summary>
    Task SetColorAsync(string sku, string deviceId, RgbColor color, CancellationToken cancellationToken = default);

    /// <summary>Sets a device's white color temperature in Kelvin, via the color_setting/colorTemperatureK control capability.</summary>
    Task SetColorTemperatureAsync(string sku, string deviceId, int kelvin, CancellationToken cancellationToken = default);

    /// <summary>Fetches the dynamic scenes/DIY effects configured for one device, via "Get dynamic scene".</summary>
    Task<IReadOnlyList<GoveeScene>> GetScenesAsync(string sku, string deviceId, CancellationToken cancellationToken = default);

    /// <summary>Activates a previously-fetched scene, via the dynamic_scene/lightScene control capability.</summary>
    Task TriggerSceneAsync(string sku, string deviceId, GoveeScene scene, CancellationToken cancellationToken = default);
}
