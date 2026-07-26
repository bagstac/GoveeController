namespace GoveeController.Domain.Devices;

/// <summary>
/// Describes one capability a physical Govee device supports, normalized from Govee's raw
/// "type"/"instance"/"parameters" capability shape into the subset this app can act on.
/// </summary>
/// <param name="Kind">The normalized capability kind, or <see cref="CapabilityKind.Unsupported"/> if this app has no control for it.</param>
/// <param name="GoveeType">The raw Govee capability "type" string, kept for round-tripping control requests.</param>
/// <param name="GoveeInstance">The raw Govee capability "instance" string, kept for round-tripping control requests.</param>
/// <param name="Min">Inclusive lower bound for range-typed capabilities (e.g. brightness 1, color temp 2000K). Null when not applicable.</param>
/// <param name="Max">Inclusive upper bound for range-typed capabilities (e.g. brightness 100, color temp 9000K). Null when not applicable.</param>
public sealed record DeviceCapability(
    CapabilityKind Kind,
    string GoveeType,
    string GoveeInstance,
    int? Min = null,
    int? Max = null);
