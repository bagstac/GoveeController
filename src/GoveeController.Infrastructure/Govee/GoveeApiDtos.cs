using System.Text.Json.Serialization;

namespace GoveeController.Infrastructure.Govee;

// These types mirror the Govee Open API's JSON shapes exactly (see
// https://developer.govee.com/reference/get-you-devices, .../control-you-devices,
// .../get-devices-status and .../get-light-scene). They are internal — nothing outside
// GoveeApiClient should depend on Govee's wire format; everything else uses the Domain types.

internal sealed class GetDevicesResponseDto
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("data")]
    public List<DeviceDto> Data { get; set; } = [];
}

internal sealed class DeviceDto
{
    [JsonPropertyName("sku")]
    public string Sku { get; set; } = string.Empty;

    [JsonPropertyName("device")]
    public string Device { get; set; } = string.Empty;

    [JsonPropertyName("deviceName")]
    public string? DeviceName { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("capabilities")]
    public List<CapabilityDto> Capabilities { get; set; } = [];
}

internal sealed class CapabilityDto
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("instance")]
    public string Instance { get; set; } = string.Empty;

    [JsonPropertyName("parameters")]
    public CapabilityParametersDto? Parameters { get; set; }
}

internal sealed class CapabilityParametersDto
{
    [JsonPropertyName("dataType")]
    public string? DataType { get; set; }

    [JsonPropertyName("range")]
    public CapabilityRangeDto? Range { get; set; }
}

internal sealed class CapabilityRangeDto
{
    [JsonPropertyName("min")]
    public int Min { get; set; }

    [JsonPropertyName("max")]
    public int Max { get; set; }
}

internal sealed class DeviceRefRequestDto
{
    [JsonPropertyName("requestId")]
    public string RequestId { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("payload")]
    public required DeviceRefPayloadDto Payload { get; set; }
}

internal sealed class DeviceRefPayloadDto
{
    [JsonPropertyName("sku")]
    public required string Sku { get; set; }

    [JsonPropertyName("device")]
    public required string Device { get; set; }
}

internal sealed class GetDeviceStateResponseDto
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("msg")]
    public string? Msg { get; set; }

    [JsonPropertyName("payload")]
    public StatePayloadDto? Payload { get; set; }
}

internal sealed class StatePayloadDto
{
    [JsonPropertyName("capabilities")]
    public List<StateCapabilityDto> Capabilities { get; set; } = [];
}

internal sealed class StateCapabilityDto
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("instance")]
    public string Instance { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public StateValueDto? State { get; set; }
}

internal sealed class StateValueDto
{
    // Value shape varies by capability (bool-as-int, integer, or nested object), so it is
    // deserialized as a raw JsonElement and interpreted by GoveeApiClient based on capability type.
    [JsonPropertyName("value")]
    public System.Text.Json.JsonElement Value { get; set; }
}

internal sealed class ControlRequestDto
{
    [JsonPropertyName("requestId")]
    public string RequestId { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("payload")]
    public required ControlPayloadDto Payload { get; set; }
}

internal sealed class ControlPayloadDto
{
    [JsonPropertyName("sku")]
    public required string Sku { get; set; }

    [JsonPropertyName("device")]
    public required string Device { get; set; }

    [JsonPropertyName("capability")]
    public required ControlCapabilityDto Capability { get; set; }
}

internal sealed class ControlCapabilityDto
{
    [JsonPropertyName("type")]
    public required string Type { get; set; }

    [JsonPropertyName("instance")]
    public required string Instance { get; set; }

    /// <summary>
    /// The value to set. Shape depends on capability: an int (0/1) for on/off, an int for
    /// brightness/color/color-temperature, or a <see cref="SceneValueDto"/> object for scenes.
    /// </summary>
    [JsonPropertyName("value")]
    public required object Value { get; set; }
}

internal sealed class ControlResponseDto
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("msg")]
    public string? Msg { get; set; }
}

internal sealed class SceneValueDto
{
    [JsonPropertyName("paramId")]
    public int ParamId { get; set; }

    [JsonPropertyName("id")]
    public int Id { get; set; }
}

internal sealed class GetScenesResponseDto
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("msg")]
    public string? Msg { get; set; }

    [JsonPropertyName("payload")]
    public ScenesPayloadDto? Payload { get; set; }
}

internal sealed class ScenesPayloadDto
{
    [JsonPropertyName("capabilities")]
    public List<SceneCapabilityDto> Capabilities { get; set; } = [];
}

internal sealed class SceneCapabilityDto
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("instance")]
    public string Instance { get; set; } = string.Empty;

    [JsonPropertyName("parameters")]
    public SceneParametersDto? Parameters { get; set; }
}

internal sealed class SceneParametersDto
{
    [JsonPropertyName("options")]
    public List<SceneOptionDto> Options { get; set; } = [];
}

internal sealed class SceneOptionDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public SceneValueDto Value { get; set; } = new();
}
