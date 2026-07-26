using GoveeController.Domain.Devices;
using GoveeController.Domain.Shortcuts;

namespace GoveeController.Application.Shortcuts;

/// <summary>
/// Use-case service for managing and applying user-defined <see cref="Shortcut"/> presets.
/// </summary>
public interface IShortcutService
{
    /// <summary>Lists all saved shortcuts.</summary>
    Task<IReadOnlyList<Shortcut>> ListShortcutsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates and persists a new shortcut targeting one or more devices. <paramref name="color"/>
    /// and <paramref name="colorTemperatureKelvin"/> are mutually exclusive — a device is either in
    /// color mode or white/temperature mode. <paramref name="targets"/> must contain at least one device.
    /// </summary>
    Task<Shortcut> CreateShortcutAsync(
        string name,
        IReadOnlyList<(string Sku, string DeviceId)> targets,
        bool powerOn,
        int? brightness,
        RgbColor? color,
        int? colorTemperatureKelvin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Overwrites an existing shortcut's name, targets, and power/brightness/color values. Same
    /// validation rules as <see cref="CreateShortcutAsync"/>. Throws <see cref="KeyNotFoundException"/>
    /// if the shortcut does not exist.
    /// </summary>
    Task UpdateShortcutAsync(
        int id,
        string name,
        IReadOnlyList<(string Sku, string DeviceId)> targets,
        bool powerOn,
        int? brightness,
        RgbColor? color,
        int? colorTemperatureKelvin,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a shortcut by id.</summary>
    Task DeleteShortcutAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a saved shortcut to every one of its target devices: sets power, then brightness and
    /// color/color-temperature on each, if the shortcut specifies them. Devices are updated one at a
    /// time, not in parallel, to stay predictable under Govee's per-account rate limit. Throws
    /// <see cref="KeyNotFoundException"/> if the shortcut does not exist.
    /// </summary>
    Task ApplyShortcutAsync(int id, CancellationToken cancellationToken = default);
}
