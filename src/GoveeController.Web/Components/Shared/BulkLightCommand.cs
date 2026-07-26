using GoveeController.Domain.Devices;

namespace GoveeController.Web.Components.Shared;

/// <summary>
/// A "set this on every bulb that supports it" instruction broadcast from the Devices page to
/// every <see cref="DeviceCard"/>. Each field is independently optional so one bulk action only
/// touches the one setting it's for (e.g. changing brightness for all bulbs doesn't also reset
/// color). <see cref="Id"/> is a monotonically increasing counter — a <see cref="DeviceCard"/>
/// applies the command when it sees a new <see cref="Id"/>, mirroring how <c>RefreshSignal</c>
/// triggers a forced refresh.
/// </summary>
/// <param name="Id">Unique per broadcast; any change (not the specific value) is what triggers application.</param>
/// <param name="Brightness">Brightness to apply (1-100), or null if this command isn't a brightness change.</param>
/// <param name="Color">RGB color to apply, or null if this command isn't a color change.</param>
/// <param name="ColorTemperatureKelvin">White color temperature to apply, or null if this command isn't a warmth change.</param>
public sealed record BulkLightCommand(int Id, int? Brightness, RgbColor? Color, int? ColorTemperatureKelvin);
