namespace GoveeController.Domain.Devices;

/// <summary>
/// A point-in-time snapshot of a device's controllable state, as reported by the
/// "Get device state" endpoint. Fields are null when the device does not support
/// that capability or Govee did not report a value for it.
/// </summary>
/// <param name="PowerOn">Whether the device is currently powered on.</param>
/// <param name="Brightness">Current brightness, typically 1-100.</param>
/// <param name="Color">Current RGB color, when the device is in color mode.</param>
/// <param name="ColorTemperatureKelvin">Current white color temperature in Kelvin, when the device is in white mode.</param>
public sealed record LightState(
    bool PowerOn,
    int? Brightness,
    RgbColor? Color,
    int? ColorTemperatureKelvin);
