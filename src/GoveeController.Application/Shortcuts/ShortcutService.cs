using GoveeController.Application.Devices;
using GoveeController.Domain.Devices;
using GoveeController.Domain.Shortcuts;

namespace GoveeController.Application.Shortcuts;

/// <inheritdoc cref="IShortcutService" />
public sealed class ShortcutService : IShortcutService
{
    private readonly IShortcutRepository _repository;
    private readonly IDeviceControlService _deviceControlService;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the service.</summary>
    public ShortcutService(IShortcutRepository repository, IDeviceControlService deviceControlService, TimeProvider timeProvider)
    {
        _repository = repository;
        _deviceControlService = deviceControlService;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Shortcut>> ListShortcutsAsync(CancellationToken cancellationToken = default) =>
        _repository.GetAllAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<Shortcut> CreateShortcutAsync(
        string name,
        IReadOnlyList<(string Sku, string DeviceId)> targets,
        bool powerOn,
        int? brightness,
        RgbColor? color,
        int? colorTemperatureKelvin,
        int? nextShortcutId,
        int nextShortcutDelaySeconds,
        CancellationToken cancellationToken = default)
    {
        ValidateShortcutInputs(targets, brightness, color, colorTemperatureKelvin);

        var all = await _repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        ValidateChainLink(all, currentId: null, nextShortcutId, nextShortcutDelaySeconds);

        var shortcut = new Shortcut
        {
            Name = name,
            Targets = targets.Select(t => new ShortcutTarget { DeviceSku = t.Sku, DeviceId = t.DeviceId }).ToList(),
            PowerOn = powerOn,
            Brightness = brightness,
            ColorRgbPacked = color?.ToPackedInt(),
            ColorTemperatureKelvin = colorTemperatureKelvin,
            CreatedAtUtc = DateTime.UtcNow,
            NextShortcutId = nextShortcutId,
            // Normalized here (not just trusted from the caller) so a stale delay can never
            // reappear if a shortcut is unlinked and later relinked — see LINKED-SHORTCUTS-PLAN.md §6.
            NextShortcutDelaySeconds = nextShortcutId is null ? 0 : nextShortcutDelaySeconds
        };

        return await _repository.AddAsync(shortcut, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateShortcutAsync(
        int id,
        string name,
        IReadOnlyList<(string Sku, string DeviceId)> targets,
        bool powerOn,
        int? brightness,
        RgbColor? color,
        int? colorTemperatureKelvin,
        int? nextShortcutId,
        int nextShortcutDelaySeconds,
        CancellationToken cancellationToken = default)
    {
        ValidateShortcutInputs(targets, brightness, color, colorTemperatureKelvin);

        var all = await _repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        ValidateChainLink(all, currentId: id, nextShortcutId, nextShortcutDelaySeconds);

        var shortcut = new Shortcut
        {
            Id = id,
            Name = name,
            Targets = targets.Select(t => new ShortcutTarget { DeviceSku = t.Sku, DeviceId = t.DeviceId }).ToList(),
            PowerOn = powerOn,
            Brightness = brightness,
            ColorRgbPacked = color?.ToPackedInt(),
            ColorTemperatureKelvin = colorTemperatureKelvin,
            NextShortcutId = nextShortcutId,
            NextShortcutDelaySeconds = nextShortcutId is null ? 0 : nextShortcutDelaySeconds
        };

        await _repository.UpdateAsync(shortcut, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task DeleteShortcutAsync(int id, CancellationToken cancellationToken = default) =>
        _repository.DeleteAsync(id, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Shortcut>> ListEligibleNextShortcutsAsync(int? forShortcutId, CancellationToken cancellationToken = default)
    {
        var all = await _repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return all.Where(candidate => IsEligibleNext(all, forShortcutId, candidate.Id)).ToList();
    }

    // Govee's brightness range is consistently 1-100 across every known device; the color
    // temperature band is deliberately generous (device-specific ranges are typically narrower,
    // e.g. 2700-6500K) since this is just a sanity check against nonsensical input, not a
    // per-device bound — the UI's own inputs already constrain to each device's real range, but
    // the service is the actual trust boundary and shouldn't rely on that.
    private const int MinBrightness = 1;
    private const int MaxBrightness = 100;
    private const int MinColorTemperatureKelvin = 2000;
    private const int MaxColorTemperatureKelvin = 9000;

    // Chain rules - see LINKED-SHORTCUTS-PLAN.md §6 for the full rationale.
    private const int MaxChainLength = 3;
    // Defensive cap on predecessor/successor walks. Chains never legitimately exceed
    // MaxChainLength (enforced at write time by ValidateChainLink, and structurally by the unique
    // index on NextShortcutId - see AppDbContext), but a corrupted database row must not be able to
    // spin these walks forever.
    private const int MaxChainWalk = 4;
    private const int MinChainDelaySeconds = 0;
    private const int MaxChainDelaySeconds = 60;

    private static void ValidateShortcutInputs(
        IReadOnlyList<(string Sku, string DeviceId)> targets,
        int? brightness,
        RgbColor? color,
        int? colorTemperatureKelvin)
    {
        if (color is not null && colorTemperatureKelvin is not null)
        {
            throw new ArgumentException("A shortcut cannot specify both an RGB color and a color temperature.");
        }

        if (targets.Count == 0)
        {
            throw new ArgumentException("A shortcut must target at least one device.", nameof(targets));
        }

        if (brightness is { } b && (b < MinBrightness || b > MaxBrightness))
        {
            throw new ArgumentException($"Brightness must be between {MinBrightness} and {MaxBrightness}.", nameof(brightness));
        }

        if (colorTemperatureKelvin is { } k && (k < MinColorTemperatureKelvin || k > MaxColorTemperatureKelvin))
        {
            throw new ArgumentException(
                $"Color temperature must be between {MinColorTemperatureKelvin}K and {MaxColorTemperatureKelvin}K.",
                nameof(colorTemperatureKelvin));
        }
    }

    /// <summary>
    /// Validates a proposed chain link (<paramref name="currentId"/> would run
    /// <paramref name="nextShortcutId"/> next). No-op when <paramref name="nextShortcutId"/> is
    /// null - unlinking never needs validation. <paramref name="currentId"/> is null when
    /// validating a brand-new shortcut that doesn't have an id yet, which is why every rule below
    /// treats "no currentId" as "nothing points at this yet, and it can't be anyone's ancestor".
    /// Operates on an already-fetched shortcut list (see LINKED-SHORTCUTS-PLAN.md §3.4) rather than
    /// making its own repository calls, so callers control exactly how many round-trips happen.
    /// </summary>
    private static void ValidateChainLink(
        IReadOnlyList<Shortcut> all,
        int? currentId,
        int? nextShortcutId,
        int nextShortcutDelaySeconds)
    {
        if (nextShortcutId is not { } nextId)
        {
            return;
        }

        if (nextShortcutDelaySeconds < MinChainDelaySeconds || nextShortcutDelaySeconds > MaxChainDelaySeconds)
        {
            throw new ArgumentException(
                $"Delay must be between {MinChainDelaySeconds} and {MaxChainDelaySeconds} seconds.",
                nameof(nextShortcutDelaySeconds));
        }

        var byId = all.ToDictionary(s => s.Id);
        if (!byId.ContainsKey(nextId))
        {
            throw new KeyNotFoundException($"No shortcut with id {nextId} exists.");
        }

        if (currentId == nextId)
        {
            throw new ArgumentException("A shortcut cannot run itself.", nameof(nextShortcutId));
        }

        // predecessorOf[followerId] = the shortcut whose NextShortcutId == followerId, if any. At
        // most one exists per follower because of the unique index on NextShortcutId.
        var predecessorOf = all
            .Where(s => s.NextShortcutId is not null)
            .ToDictionary(s => s.NextShortcutId!.Value, s => s);

        if (predecessorOf.TryGetValue(nextId, out var predecessorOfNext) && predecessorOfNext.Id != currentId)
        {
            throw new ArgumentException("That shortcut already runs after another shortcut.", nameof(nextShortcutId));
        }

        // Cycle check: walking currentId's predecessors must never reach nextId. Only the upstream
        // side needs checking - currentId's existing downstream link is what's being replaced.
        if (currentId is { } id)
        {
            var cursor = predecessorOf.GetValueOrDefault(id);
            for (var hops = 0; cursor is not null && hops < MaxChainWalk; hops++)
            {
                if (cursor.Id == nextId)
                {
                    throw new ArgumentException("That would create a loop.", nameof(nextShortcutId));
                }
                cursor = predecessorOf.GetValueOrDefault(cursor.Id);
            }
        }

        // A brand-new shortcut (currentId is null) has upstreamCount 1 - nothing points at it yet.
        var upstreamCount = currentId is { } uid
            ? WalkChainLength(uid, x => predecessorOf.TryGetValue(x, out var p) ? p.Id : null)
            : 1;
        var downstreamCount = WalkChainLength(nextId, x => byId[x].NextShortcutId);

        if (upstreamCount + downstreamCount > MaxChainLength)
        {
            throw new ArgumentException($"A chain can link at most {MaxChainLength} shortcuts.", nameof(nextShortcutId));
        }
    }

    /// <summary>Counts nodes from <paramref name="startId"/> to the end of the path <paramref name="step"/> walks, inclusive of the start.</summary>
    private static int WalkChainLength(int startId, Func<int, int?> step)
    {
        var count = 1;
        var currentId = startId;
        for (var hops = 0; hops < MaxChainWalk; hops++)
        {
            if (step(currentId) is not { } next)
            {
                break;
            }
            count++;
            currentId = next;
        }
        return count;
    }

    /// <summary>
    /// Non-throwing eligibility check backing <see cref="ListEligibleNextShortcutsAsync"/>. Reuses
    /// <see cref="ValidateChainLink"/> (with a placeholder in-range delay, since delay never affects
    /// eligibility) so there is exactly one source of truth for the chain rules - this is a filter
    /// over that same logic, not a second implementation of it.
    /// </summary>
    private static bool IsEligibleNext(IReadOnlyList<Shortcut> all, int? currentId, int candidateId)
    {
        try
        {
            ValidateChainLink(all, currentId, candidateId, nextShortcutDelaySeconds: 0);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task ApplyShortcutAsync(int id, CancellationToken cancellationToken = default)
    {
        var all = await _repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var byId = all.ToDictionary(s => s.Id);
        if (!byId.TryGetValue(id, out var start))
        {
            throw new KeyNotFoundException($"No shortcut with id {id} exists.");
        }

        // Resolve the chain from the starting shortcut to the end. The hop cap guards against a
        // corrupted database row forming an unexpected cycle - by construction (the unique index on
        // NextShortcutId plus ValidateChainLink's cycle check) chains never legitimately exceed
        // MaxChainLength, but this walk must not spin forever if that invariant is ever violated.
        var chain = new List<Shortcut> { start };
        var cursor = start;
        for (var hops = 0; hops < MaxChainWalk && cursor.NextShortcutId is { } nextId; hops++)
        {
            if (!byId.TryGetValue(nextId, out var next))
            {
                break;
            }
            chain.Add(next);
            cursor = next;
        }

        // Applying is best-effort per target, per step: one offline bulb (a routine occurrence with
        // these devices) must not prevent the shortcut - or the rest of the chain - from reaching
        // everything else. Failures are collected across every step and reported together once the
        // whole chain has run, rather than letting the first exception abort everything after it.
        var failures = new List<ShortcutTargetFailure>();
        var succeededCount = 0;
        var totalCount = 0;

        for (var i = 0; i < chain.Count; i++)
        {
            var step = chain[i];
            totalCount += step.Targets.Count;

            foreach (var target in step.Targets)
            {
                try
                {
                    await ApplyToTargetAsync(step, target, cancellationToken).ConfigureAwait(false);
                    succeededCount++;
                }
                catch (Exception ex)
                {
                    failures.Add(new ShortcutTargetFailure(target.DeviceSku, target.DeviceId, ex.Message, step.Id, step.Name));
                }
            }

            var hasNextStep = i < chain.Count - 1;
            if (hasNextStep && step.NextShortcutDelaySeconds > 0)
            {
                // Honors cancellation deliberately: Shortcuts.razor cancels its CancellationTokenSource
                // on dispose, so navigating away mid-chain must abort the remaining steps rather than
                // keep running in the background against a torn-down component. Do not "fix" this by
                // swallowing OperationCanceledException here.
                await Task.Delay(TimeSpan.FromSeconds(step.NextShortcutDelaySeconds), _timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }

        if (failures.Count > 0)
        {
            throw new ShortcutApplyException(succeededCount, totalCount, failures);
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
