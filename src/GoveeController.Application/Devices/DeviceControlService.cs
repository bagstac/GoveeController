using GoveeController.Domain.Devices;
using Microsoft.Extensions.Caching.Memory;

namespace GoveeController.Application.Devices;

/// <inheritdoc cref="IDeviceControlService" />
public sealed class DeviceControlService : IDeviceControlService
{
    // Govee's Cloud API allows 30 requests/minute per account and per device. A short cache on
    // reads (device list, state, scenes) lets the UI poll/re-render freely without tripping that
    // limit, while staying short enough that the displayed state never feels stale.
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(12);

    private const string DevicesCacheKey = "govee:devices";

    private readonly IGoveeApiClient _client;
    private readonly IMemoryCache _cache;

    /// <summary>Creates the service.</summary>
    public DeviceControlService(IGoveeApiClient client, IMemoryCache cache)
    {
        _client = client;
        _cache = cache;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Device>> ListDevicesAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && _cache.TryGetValue(DevicesCacheKey, out IReadOnlyList<Device>? cached) && cached is not null)
        {
            return cached;
        }

        var devices = await _client.GetDevicesAsync(cancellationToken).ConfigureAwait(false);
        _cache.Set(DevicesCacheKey, devices, CacheDuration);
        return devices;
    }

    /// <inheritdoc />
    public async Task<LightState> GetStateAsync(string sku, string deviceId, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        var cacheKey = StateCacheKey(sku, deviceId);
        if (!forceRefresh && _cache.TryGetValue(cacheKey, out LightState? cached) && cached is not null)
        {
            return cached;
        }

        var state = await _client.GetDeviceStateAsync(sku, deviceId, cancellationToken).ConfigureAwait(false);
        _cache.Set(cacheKey, state, CacheDuration);
        return state;
    }

    /// <inheritdoc />
    public Task TurnOnAsync(string sku, string deviceId, CancellationToken cancellationToken = default) =>
        ControlAsync(sku, deviceId, () => _client.SetPowerAsync(sku, deviceId, powerOn: true, cancellationToken));

    /// <inheritdoc />
    public Task TurnOffAsync(string sku, string deviceId, CancellationToken cancellationToken = default) =>
        ControlAsync(sku, deviceId, () => _client.SetPowerAsync(sku, deviceId, powerOn: false, cancellationToken));

    /// <inheritdoc />
    public Task SetBrightnessAsync(string sku, string deviceId, int brightness, CancellationToken cancellationToken = default) =>
        ControlAsync(sku, deviceId, () => _client.SetBrightnessAsync(sku, deviceId, brightness, cancellationToken));

    /// <inheritdoc />
    public Task SetColorAsync(string sku, string deviceId, RgbColor color, CancellationToken cancellationToken = default) =>
        ControlAsync(sku, deviceId, () => _client.SetColorAsync(sku, deviceId, color, cancellationToken));

    /// <inheritdoc />
    public Task SetColorTemperatureAsync(string sku, string deviceId, int kelvin, CancellationToken cancellationToken = default) =>
        ControlAsync(sku, deviceId, () => _client.SetColorTemperatureAsync(sku, deviceId, kelvin, cancellationToken));

    /// <inheritdoc />
    public async Task<IReadOnlyList<GoveeScene>> ListScenesAsync(string sku, string deviceId, CancellationToken cancellationToken = default)
    {
        var cacheKey = ScenesCacheKey(sku, deviceId);
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<GoveeScene>? cached) && cached is not null)
        {
            return cached;
        }

        var scenes = await _client.GetScenesAsync(sku, deviceId, cancellationToken).ConfigureAwait(false);
        _cache.Set(cacheKey, scenes, CacheDuration);
        return scenes;
    }

    /// <inheritdoc />
    public Task TriggerSceneAsync(string sku, string deviceId, GoveeScene scene, CancellationToken cancellationToken = default) =>
        ControlAsync(sku, deviceId, () => _client.TriggerSceneAsync(sku, deviceId, scene, cancellationToken));

    /// <summary>
    /// Runs a control command and evicts the cached state for that device, since the command just
    /// invalidated it — the next <see cref="GetStateAsync"/> call should hit the API, not a stale cache entry.
    /// </summary>
    private async Task ControlAsync(string sku, string deviceId, Func<Task> command)
    {
        await command().ConfigureAwait(false);
        _cache.Remove(StateCacheKey(sku, deviceId));
    }

    private static string StateCacheKey(string sku, string deviceId) => $"govee:state:{sku}:{deviceId}";

    private static string ScenesCacheKey(string sku, string deviceId) => $"govee:scenes:{sku}:{deviceId}";
}
