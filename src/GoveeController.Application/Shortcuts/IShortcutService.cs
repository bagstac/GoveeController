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
    /// color mode or white/temperature mode. <paramref name="targets"/> must contain at least one
    /// device. <paramref name="nextShortcutId"/> optionally names the shortcut to run after this one
    /// (see <see cref="ApplyShortcutAsync"/>); chains are capped at 3 shortcuts and a shortcut may
    /// follow at most one other — see LINKED-SHORTCUTS-PLAN.md §6 for the full validation rules.
    /// <paramref name="nextShortcutDelaySeconds"/> (0-60) is the pause before running that next
    /// shortcut and is ignored when <paramref name="nextShortcutId"/> is null.
    /// </summary>
    Task<Shortcut> CreateShortcutAsync(
        string name,
        IReadOnlyList<(string Sku, string DeviceId)> targets,
        bool powerOn,
        int? brightness,
        RgbColor? color,
        int? colorTemperatureKelvin,
        int? nextShortcutId,
        int nextShortcutDelaySeconds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Overwrites an existing shortcut's name, targets, power/brightness/color values, and chain
    /// link. Same validation rules as <see cref="CreateShortcutAsync"/>. Throws
    /// <see cref="KeyNotFoundException"/> if the shortcut does not exist.
    /// </summary>
    Task UpdateShortcutAsync(
        int id,
        string name,
        IReadOnlyList<(string Sku, string DeviceId)> targets,
        bool powerOn,
        int? brightness,
        RgbColor? color,
        int? colorTemperatureKelvin,
        int? nextShortcutId,
        int nextShortcutDelaySeconds,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a shortcut by id.</summary>
    Task DeleteShortcutAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists shortcuts that may legally be set as the next step for <paramref name="forShortcutId"/>
    /// (pass null when creating a brand-new shortcut). Applies the same chain rules as
    /// <see cref="CreateShortcutAsync"/>/<see cref="UpdateShortcutAsync"/> so the UI can offer only
    /// valid choices, but those methods still validate on write — this is a convenience for
    /// populating a dropdown, not the enforcement point.
    /// </summary>
    Task<IReadOnlyList<Shortcut>> ListEligibleNextShortcutsAsync(int? forShortcutId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a saved shortcut to every one of its target devices — sets power, then brightness and
    /// color/color-temperature on each, if the shortcut specifies them — and then continues down its
    /// chain: if it names a next shortcut, that one is applied too (after waiting its configured
    /// delay), and so on to the end of the chain. Devices within a step are updated one at a time,
    /// not in parallel, to stay predictable under Govee's per-account rate limit. A target failing
    /// does not stop the rest of that step or later steps; failures from every step are collected and
    /// reported together via <see cref="ShortcutApplyException"/> once the whole chain has run.
    /// Throws <see cref="KeyNotFoundException"/> if the starting shortcut does not exist.
    /// </summary>
    Task ApplyShortcutAsync(int id, CancellationToken cancellationToken = default);
}
