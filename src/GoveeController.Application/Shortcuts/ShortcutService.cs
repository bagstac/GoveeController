using GoveeController.Application.Devices;
using GoveeController.Domain.Devices;
using GoveeController.Domain.Shortcuts;

namespace GoveeController.Application.Shortcuts;

/// <inheritdoc cref="IShortcutService" />
public sealed class ShortcutService : IShortcutService
{
    private readonly IShortcutRepository _repository;
    private readonly IDeviceControlService _deviceControlService;

    /// <summary>Creates the service.</summary>
    public ShortcutService(IShortcutRepository repository, IDeviceControlService deviceControlService)
    {
        _repository = repository;
        _deviceControlService = deviceControlService;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Shortcut>> ListShortcutsAsync(CancellationToken cancellationToken = default) =>
        _repository.GetAllAsync(cancellationToken);

    /// <inheritdoc />
    public Task<Shortcut> CreateShortcutAsync(
        string name,
        IReadOnlyList<(string Sku, string DeviceId)> targets,
        bool powerOn,
        int? brightness,
        RgbColor? color,
        int? colorTemperatureKelvin,
        CancellationToken cancellationToken = default)
    {
        ValidateShortcutInputs(targets, color, colorTemperatureKelvin);

        var shortcut = new Shortcut
        {
            Name = name,
            Targets = targets.Select(t => new ShortcutTarget { DeviceSku = t.Sku, DeviceId = t.DeviceId }).ToList(),
            PowerOn = powerOn,
            Brightness = brightness,
            ColorRgbPacked = color?.ToPackedInt(),
            ColorTemperatureKelvin = colorTemperatureKelvin,
            CreatedAtUtc = DateTime.UtcNow
        };

        return _repository.AddAsync(shortcut, cancellationToken);
    }

    /// <inheritdoc />
    public Task UpdateShortcutAsync(
        int id,
        string name,
        IReadOnlyList<(string Sku, string DeviceId)> targets,
        bool powerOn,
        int? brightness,
        RgbColor? color,
        int? colorTemperatureKelvin,
        CancellationToken cancellationToken = default)
    {
        ValidateShortcutInputs(targets, color, colorTemperatureKelvin);

        var shortcut = new Shortcut
        {
            Id = id,
            Name = name,
            Targets = targets.Select(t => new ShortcutTarget { DeviceSku = t.Sku, DeviceId = t.DeviceId }).ToList(),
            PowerOn = powerOn,
            Brightness = brightness,
            ColorRgbPacked = color?.ToPackedInt(),
            ColorTemperatureKelvin = colorTemperatureKelvin
        };

        return _repository.UpdateAsync(shortcut, cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteShortcutAsync(int id, CancellationToken cancellationToken = default) =>
        _repository.DeleteAsync(id, cancellationToken);

    private static void ValidateShortcutInputs(IReadOnlyList<(string Sku, string DeviceId)> targets, RgbColor? color, int? colorTemperatureKelvin)
    {
        if (color is not null && colorTemperatureKelvin is not null)
        {
            throw new ArgumentException("A shortcut cannot specify both an RGB color and a color temperature.");
        }

        if (targets.Count == 0)
        {
            throw new ArgumentException("A shortcut must target at least one device.", nameof(targets));
        }
    }

    /// <inheritdoc />
    public async Task ApplyShortcutAsync(int id, CancellationToken cancellationToken = default)
    {
        var shortcut = await _repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"No shortcut with id {id} exists.");

        foreach (var target in shortcut.Targets)
        {
            await ApplyToTargetAsync(shortcut, target, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ApplyToTargetAsync(Shortcut shortcut, ShortcutTarget target, CancellationToken cancellationToken)
    {
        if (shortcut.PowerOn)
        {
            await _deviceControlService.TurnOnAsync(target.DeviceSku, target.DeviceId, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _deviceControlService.TurnOffAsync(target.DeviceSku, target.DeviceId, cancellationToken).ConfigureAwait(false);
            // Nothing further to apply to this device once it's off.
            return;
        }

        if (shortcut.Brightness is { } brightness)
        {
            await _deviceControlService.SetBrightnessAsync(target.DeviceSku, target.DeviceId, brightness, cancellationToken).ConfigureAwait(false);
        }

        if (shortcut.ColorRgbPacked is { } packedColor)
        {
            await _deviceControlService.SetColorAsync(target.DeviceSku, target.DeviceId, RgbColor.FromPackedInt(packedColor), cancellationToken).ConfigureAwait(false);
        }
        else if (shortcut.ColorTemperatureKelvin is { } kelvin)
        {
            await _deviceControlService.SetColorTemperatureAsync(target.DeviceSku, target.DeviceId, kelvin, cancellationToken).ConfigureAwait(false);
        }
    }
}
