using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GoveeController.Application.Devices;
using GoveeController.Domain.Devices;
using Microsoft.Extensions.Options;
using Polly.RateLimiting;

namespace GoveeController.Infrastructure.Govee;

/// <summary>
/// <see cref="IGoveeApiClient"/> implementation backed by the real Govee Cloud API over HTTP.
/// Registered as a typed client (see Web/Program.cs), which is where request-level retry/backoff
/// for HTTP 429 and 5xx responses is configured via Microsoft.Extensions.Http.Resilience —
/// this class only concerns itself with request/response shape, not resiliency policy.
/// </summary>
public sealed class GoveeApiClient : IGoveeApiClient
{
    private const string DevicesPath = "/router/api/v1/user/devices";
    private const string StatePath = "/router/api/v1/device/state";
    private const string ControlPath = "/router/api/v1/device/control";
    private const string ScenesPath = "/router/api/v1/device/scenes";

    private const string OnOffType = "devices.capabilities.on_off";
    private const string OnOffInstance = "powerSwitch";
    private const string BrightnessType = "devices.capabilities.range";
    private const string BrightnessInstance = "brightness";
    private const string ColorSettingType = "devices.capabilities.color_setting";
    private const string ColorRgbInstance = "colorRgb";
    private const string ColorTemperatureInstance = "colorTemperatureK";
    private const string DynamicSceneType = "devices.capabilities.dynamic_scene";
    private const string LightSceneInstance = "lightScene";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    /// <summary>Creates the client. Called by the DI container via the typed-HttpClient registration in ServiceCollectionExtensions.</summary>
    public GoveeApiClient(HttpClient httpClient, IOptions<GoveeApiOptions> options)
    {
        var config = options.Value;
        httpClient.BaseAddress = new Uri(config.BaseUrl, UriKind.Absolute);
        // Never log this header's value or config.ApiKey directly — default HttpClient logging
        // doesn't include headers, but raising log verbosity for debugging must not be able to leak it.
        httpClient.DefaultRequestHeaders.Add("Govee-API-Key", config.ApiKey);
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Device>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var httpResponse = await _httpClient.GetAsync(DevicesPath, cancellationToken).ConfigureAwait(false);
            httpResponse.EnsureSuccessStatusCode();

            var response = await httpResponse.Content.ReadFromJsonAsync<GetDevicesResponseDto>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            ThrowIfGoveeError(response?.Code, response?.Message);

            return response?.Data.Select(MapDevice).ToList() ?? [];
        }
        catch (RateLimiterRejectedException ex)
        {
            throw TranslateRateLimitRejection(ex);
        }
    }

    /// <inheritdoc />
    public async Task<LightState> GetDeviceStateAsync(string sku, string deviceId, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new DeviceRefRequestDto { Payload = new DeviceRefPayloadDto { Sku = sku, Device = deviceId } };
            using var httpResponse = await _httpClient.PostAsJsonAsync(StatePath, request, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            httpResponse.EnsureSuccessStatusCode();

            var response = await httpResponse.Content.ReadFromJsonAsync<GetDeviceStateResponseDto>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            ThrowIfGoveeError(response?.Code, response?.Msg);
            var capabilities = response?.Payload?.Capabilities ?? [];

            return MapState(capabilities);
        }
        catch (RateLimiterRejectedException ex)
        {
            throw TranslateRateLimitRejection(ex);
        }
    }

    /// <inheritdoc />
    public Task SetPowerAsync(string sku, string deviceId, bool powerOn, CancellationToken cancellationToken = default) =>
        SendControlAsync(sku, deviceId, OnOffType, OnOffInstance, powerOn ? 1 : 0, cancellationToken);

    /// <inheritdoc />
    public Task SetBrightnessAsync(string sku, string deviceId, int brightness, CancellationToken cancellationToken = default) =>
        SendControlAsync(sku, deviceId, BrightnessType, BrightnessInstance, brightness, cancellationToken);

    /// <inheritdoc />
    public Task SetColorAsync(string sku, string deviceId, RgbColor color, CancellationToken cancellationToken = default) =>
        SendControlAsync(sku, deviceId, ColorSettingType, ColorRgbInstance, color.ToPackedInt(), cancellationToken);

    /// <inheritdoc />
    public Task SetColorTemperatureAsync(string sku, string deviceId, int kelvin, CancellationToken cancellationToken = default) =>
        SendControlAsync(sku, deviceId, ColorSettingType, ColorTemperatureInstance, kelvin, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<GoveeScene>> GetScenesAsync(string sku, string deviceId, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new DeviceRefRequestDto { Payload = new DeviceRefPayloadDto { Sku = sku, Device = deviceId } };
            using var httpResponse = await _httpClient.PostAsJsonAsync(ScenesPath, request, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            httpResponse.EnsureSuccessStatusCode();

            var response = await httpResponse.Content.ReadFromJsonAsync<GetScenesResponseDto>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            ThrowIfGoveeError(response?.Code, response?.Msg);

            var sceneCapability = response?.Payload?.Capabilities
                .FirstOrDefault(c => c.Type == DynamicSceneType && c.Instance == LightSceneInstance);

            return sceneCapability?.Parameters?.Options
                .Select(o => new GoveeScene(o.Name, o.Value.ParamId, o.Value.Id))
                .ToList() ?? [];
        }
        catch (RateLimiterRejectedException ex)
        {
            throw TranslateRateLimitRejection(ex);
        }
    }

    /// <inheritdoc />
    public Task TriggerSceneAsync(string sku, string deviceId, GoveeScene scene, CancellationToken cancellationToken = default) =>
        SendControlAsync(
            sku,
            deviceId,
            DynamicSceneType,
            LightSceneInstance,
            new SceneValueDto { ParamId = scene.ParamId, Id = scene.Id },
            cancellationToken);

    private async Task SendControlAsync(string sku, string deviceId, string type, string instance, object value, CancellationToken cancellationToken)
    {
        try
        {
            var request = new ControlRequestDto
            {
                Payload = new ControlPayloadDto
                {
                    Sku = sku,
                    Device = deviceId,
                    Capability = new ControlCapabilityDto { Type = type, Instance = instance, Value = value }
                }
            };

            using var httpResponse = await _httpClient.PostAsJsonAsync(ControlPath, request, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            httpResponse.EnsureSuccessStatusCode();

            // Govee reports device-specific failures (e.g. "Device is offline") as HTTP 200 with a
            // non-200 `code` in the body, not as an HTTP error status — so EnsureSuccessStatusCode()
            // alone would silently swallow a failed control command.
            var response = await httpResponse.Content.ReadFromJsonAsync<ControlResponseDto>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            ThrowIfGoveeError(response?.Code, response?.Msg);
        }
        catch (RateLimiterRejectedException ex)
        {
            throw TranslateRateLimitRejection(ex);
        }
    }

    /// <summary>
    /// Throws <see cref="GoveeApiException"/> if Govee's response body reports a failure code.
    /// A missing/null code is treated as success only when there is genuinely no body to check.
    /// </summary>
    private static void ThrowIfGoveeError(int? code, string? message)
    {
        if (code is { } c && c != 200)
        {
            throw new GoveeApiException(c, message ?? $"Govee API returned error code {c}.");
        }
    }

    /// <summary>
    /// Translates a local rate-limit rejection (this app's own client-side throttle — see
    /// ServiceCollectionExtensions.AddGoveeInfrastructure — refusing to even send the request) into
    /// the same exception type callers already handle for Govee-reported failures, so the UI shows
    /// a clear "try again shortly" message instead of an unfamiliar Polly exception type.
    /// </summary>
    private static GoveeApiException TranslateRateLimitRejection(RateLimiterRejectedException ex) =>
        new(429, "Govee rate limit reached; please wait a moment and try again.");

    private static Device MapDevice(DeviceDto dto)
    {
        var capabilities = dto.Capabilities
            .Select(MapCapability)
            .Where(c => c.Kind != CapabilityKind.Unsupported)
            .ToList();

        return new Device(
            Sku: dto.Sku,
            Id: dto.Device,
            Name: dto.DeviceName ?? dto.Device,
            Type: dto.Type ?? string.Empty,
            Capabilities: capabilities);
    }

    private static DeviceCapability MapCapability(CapabilityDto dto)
    {
        var kind = (dto.Type, dto.Instance) switch
        {
            (OnOffType, OnOffInstance) => CapabilityKind.PowerSwitch,
            (BrightnessType, BrightnessInstance) => CapabilityKind.Brightness,
            (ColorSettingType, ColorRgbInstance) => CapabilityKind.ColorRgb,
            (ColorSettingType, ColorTemperatureInstance) => CapabilityKind.ColorTemperature,
            (DynamicSceneType, LightSceneInstance) => CapabilityKind.DynamicScene,
            _ => CapabilityKind.Unsupported
        };

        return new DeviceCapability(
            Kind: kind,
            GoveeType: dto.Type,
            GoveeInstance: dto.Instance,
            Min: dto.Parameters?.Range?.Min,
            Max: dto.Parameters?.Range?.Max);
    }

    private static LightState MapState(IReadOnlyCollection<StateCapabilityDto> capabilities)
    {
        bool powerOn = false;
        int? brightness = null;
        RgbColor? color = null;
        int? colorTemperature = null;

        foreach (var capability in capabilities)
        {
            switch (capability.Type, capability.Instance)
            {
                case (OnOffType, OnOffInstance):
                    powerOn = TryReadInt(capability.State) == 1;
                    break;
                case (BrightnessType, BrightnessInstance):
                    brightness = TryReadInt(capability.State);
                    break;
                case (ColorSettingType, ColorRgbInstance):
                    if (TryReadInt(capability.State) is { } packed)
                    {
                        color = RgbColor.FromPackedInt(packed);
                    }
                    break;
                case (ColorSettingType, ColorTemperatureInstance):
                    colorTemperature = TryReadInt(capability.State);
                    break;
            }
        }

        return new LightState(powerOn, brightness, color, colorTemperature);
    }

    /// <summary>
    /// Reads a capability's state value as an int, or null if it's missing or isn't shaped like one.
    /// Govee's capability value shapes vary by type (plain numbers, nested objects for scenes), and
    /// this app has already been bitten once by an unexpected response shape (see
    /// <see cref="ThrowIfGoveeError"/>) — a single malformed field here should degrade to "unknown"
    /// rather than take down the whole device card with an unhandled exception.
    /// </summary>
    private static int? TryReadInt(StateValueDto? state) =>
        state is { Value.ValueKind: JsonValueKind.Number } && state.Value.TryGetInt32(out var value)
            ? value
            : null;
}
