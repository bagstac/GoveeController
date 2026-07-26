using GoveeController.Domain.Devices;

namespace GoveeController.Application.Devices;

/// <summary>
/// Use-case service for listing and controlling Govee devices. This is the boundary the Web
/// layer's UI code talks to — it never calls <see cref="IGoveeApiClient"/> directly.
/// </summary>
public interface IDeviceControlService
{
    /// <summary>
    /// Lists all devices on the account. Results are cached briefly to stay under Govee's rate limit.
    /// Pass <paramref name="forceRefresh"/> to bypass the cache (e.g. for a user-triggered refresh).
    /// </summary>
    Task<IReadOnlyList<Device>> ListDevicesAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current state of one device. Results are cached briefly to stay under Govee's rate limit.
    /// Pass <paramref name="forceRefresh"/> to bypass the cache (e.g. for a user-triggered refresh).
    /// </summary>
    Task<LightState> GetStateAsync(string sku, string deviceId, bool forceRefresh = false, CancellationToken cancellationToken = default);

    /// <summary>Turns a device on.</summary>
    Task TurnOnAsync(string sku, string deviceId, CancellationToken cancellationToken = default);

    /// <summary>Turns a device off.</summary>
    Task TurnOffAsync(string sku, string deviceId, CancellationToken cancellationToken = default);

    /// <summary>Sets a device's brightness (1-100).</summary>
    Task SetBrightnessAsync(string sku, string deviceId, int brightness, CancellationToken cancellationToken = default);

    /// <summary>Sets a device's RGB color.</summary>
    Task SetColorAsync(string sku, string deviceId, RgbColor color, CancellationToken cancellationToken = default);

    /// <summary>Sets a device's white color temperature in Kelvin.</summary>
    Task SetColorTemperatureAsync(string sku, string deviceId, int kelvin, CancellationToken cancellationToken = default);

    /// <summary>Lists the dynamic scenes/DIY effects available for one device. Results are cached briefly.</summary>
    Task<IReadOnlyList<GoveeScene>> ListScenesAsync(string sku, string deviceId, CancellationToken cancellationToken = default);

    /// <summary>Activates a scene previously returned by <see cref="ListScenesAsync"/>.</summary>
    Task TriggerSceneAsync(string sku, string deviceId, GoveeScene scene, CancellationToken cancellationToken = default);
}
