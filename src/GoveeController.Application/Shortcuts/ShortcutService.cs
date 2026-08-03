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

    /// <inheritdoc />
    public async Task<Shortcut> CreateCompositeShortcutAsync(
        string name,
        IReadOnlyList<(int ReferencedShortcutId, int DelaySeconds)> referencedShortcuts,
        int? nextShortcutId,
        int nextShortcutDelaySeconds,
        CancellationToken cancellationToken = default)
    {
        var all = await _repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var byId = all.ToDictionary(s => s.Id);
        ValidateCompositeReferences(byId, currentId: null, referencedShortcuts);
        ValidateChainLink(all, currentId: null, nextShortcutId, nextShortcutDelaySeconds);

        var shortcut = new Shortcut
        {
            Name = name,
            // PowerOn is required on the entity but meaningless for a composite — a composite has
            // no targets of its own, so there is no power state to apply. Left at its default.
            PowerOn = false,
            CreatedAtUtc = DateTime.UtcNow,
            NextShortcutId = nextShortcutId,
            // Normalized here (not just trusted from the caller) so a stale delay can never
            // reappear if this composite is unlinked and later relinked — same convention as
            // CreateShortcutAsync.
            NextShortcutDelaySeconds = nextShortcutId is null ? 0 : nextShortcutDelaySeconds,
            ReferencedShortcuts = referencedShortcuts
                .Select((r, index) => new ShortcutReference
                {
                    ReferencedShortcutId = r.ReferencedShortcutId,
                    DelaySeconds = r.DelaySeconds,
                    Order = index
                })
                .ToList()
        };

        return await _repository.AddAsync(shortcut, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateCompositeShortcutAsync(
        int id,
        string name,
        IReadOnlyList<(int ReferencedShortcutId, int DelaySeconds)> referencedShortcuts,
        int? nextShortcutId,
        int nextShortcutDelaySeconds,
        CancellationToken cancellationToken = default)
    {
        var all = await _repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var byId = all.ToDictionary(s => s.Id);
        ValidateCompositeReferences(byId, currentId: id, referencedShortcuts);
        ValidateChainLink(all, currentId: id, nextShortcutId, nextShortcutDelaySeconds);

        var shortcut = new Shortcut
        {
            Id = id,
            Name = name,
            // PowerOn is required on the entity but meaningless for a composite — a composite has
            // no targets of its own, so there is no power state to apply. Left at its default.
            PowerOn = false,
            NextShortcutId = nextShortcutId,
            NextShortcutDelaySeconds = nextShortcutId is null ? 0 : nextShortcutDelaySeconds,
            ReferencedShortcuts = referencedShortcuts
                .Select((r, index) => new ShortcutReference
                {
                    ReferencedShortcutId = r.ReferencedShortcutId,
                    DelaySeconds = r.DelaySeconds,
                    Order = index
                })
                .ToList()
        };

        await _repository.UpdateAsync(shortcut, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Shortcut>> ListEligibleReferencedShortcutsAsync(int? forShortcutId, CancellationToken cancellationToken = default)
    {
        var all = await _repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var byId = all.ToDictionary(s => s.Id);
        return all.Where(candidate => IsEligibleReference(byId, forShortcutId, candidate.Id)).ToList();
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

        // The linear-chain walk above can't see cycles that run through referenced shortcuts: if
        // nextId's full downstream (its chain AND anything it references, recursively) reaches
        // currentId, then currentId -> nextId would close a loop through the composite graph —
        // e.g. a composite A that references B, then someone links B to run A next.
        if (currentId is { } cid && DownstreamContains(byId, nextId, cid))
        {
            throw new ArgumentException("That would create a loop.", nameof(nextShortcutId));
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

    /// <summary>
    /// Validates a proposed composite reference set for a shortcut identified by
    /// <paramref name="currentId"/> (null when creating a brand-new composite). Operates on an
    /// already-fetched shortcut list resolved to <paramref name="byId"/> so callers control how
    /// many repository round-trips happen, same convention as <see cref="ValidateChainLink"/>.
    /// </summary>
    private static void ValidateCompositeReferences(
        Dictionary<int, Shortcut> byId,
        int? currentId,
        IReadOnlyList<(int ReferencedShortcutId, int DelaySeconds)> referencedShortcuts)
    {
        if (referencedShortcuts.Count == 0)
        {
            throw new ArgumentException("A composite shortcut must reference at least one shortcut.", nameof(referencedShortcuts));
        }

        foreach (var (referencedShortcutId, delaySeconds) in referencedShortcuts)
        {
            if (delaySeconds < MinChainDelaySeconds || delaySeconds > MaxChainDelaySeconds)
            {
                throw new ArgumentException(
                    $"Delay must be between {MinChainDelaySeconds} and {MaxChainDelaySeconds} seconds.",
                    nameof(referencedShortcuts));
            }

            if (!byId.ContainsKey(referencedShortcutId))
            {
                throw new KeyNotFoundException($"No shortcut with id {referencedShortcutId} exists.");
            }

            if (currentId == referencedShortcutId)
            {
                throw new ArgumentException("A composite shortcut cannot reference itself.", nameof(referencedShortcuts));
            }

            // A reference from currentId to referencedShortcutId is a cycle iff walking the
            // referenced shortcut's full downstream (its chain AND anything it references,
            // recursively) reaches currentId.
            if (currentId is { } id && DownstreamContains(byId, referencedShortcutId, id))
            {
                throw new ArgumentException("That would create a loop.", nameof(referencedShortcuts));
            }
        }
    }

    /// <summary>
    /// True if walking the full downstream of <paramref name="startId"/> — its NextShortcutId chain
    /// AND the chains of any shortcuts it references, recursively — ever reaches
    /// <paramref name="targetId"/>. Used to prevent composite references and chain links from
    /// forming cycles through the composite graph. In an acyclic graph (which the validators
    /// guarantee by construction), reachability from a node is path-independent, so a shared
    /// visited set (purely defensive against a corrupted row forming a cycle) cannot cause a false
    /// negative.
    /// </summary>
    private static bool DownstreamContains(Dictionary<int, Shortcut> byId, int startId, int targetId, int maxDepth = 10)
    {
        var visited = new HashSet<int>();
        return DownstreamContainsCore(byId, startId, targetId, visited, maxDepth);
    }

    private static bool DownstreamContainsCore(
        Dictionary<int, Shortcut> byId,
        int currentId,
        int targetId,
        HashSet<int> visited,
        int maxDepth)
    {
        if (currentId == targetId)
        {
            return true;
        }
        if (maxDepth <= 0 || !visited.Add(currentId))
        {
            return false;
        }

        if (!byId.TryGetValue(currentId, out var shortcut))
        {
            return false;
        }

        if (shortcut.NextShortcutId is { } nextId &&
            DownstreamContainsCore(byId, nextId, targetId, visited, maxDepth - 1))
        {
            return true;
        }

        foreach (var reference in shortcut.ReferencedShortcuts)
        {
            if (reference.ReferencedShortcutId is { } referencedId &&
                DownstreamContainsCore(byId, referencedId, targetId, visited, maxDepth - 1))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Non-throwing eligibility check backing <see cref="ListEligibleReferencedShortcutsAsync"/>:
    /// a candidate is referenceable unless it's the composite itself or its full downstream would
    /// reach the composite (a cycle).
    /// </summary>
    private static bool IsEligibleReference(Dictionary<int, Shortcut> byId, int? forShortcutId, int candidateId)
    {
        if (forShortcutId == candidateId)
        {
            return false;
        }

        return forShortcutId is not { } id || !DownstreamContains(byId, candidateId, id);
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

        // Applying is best-effort per target, per step: one offline bulb (a routine occurrence with
        // these devices) must not prevent the shortcut - or the rest of the chain - from reaching
        // everything else. Failures are collected across every step and reported together once the
        // whole chain has run, rather than letting the first exception abort everything after it.
        var failures = new List<ShortcutTargetFailure>();
        var counters = new ApplyCounters();
        var recursionStack = new HashSet<int>();

        await ApplyOneAsync(start, byId, failures, counters, recursionStack, cancellationToken).ConfigureAwait(false);

        if (failures.Count > 0)
        {
            throw new ShortcutApplyException(counters.Succeeded, counters.Total, failures);
        }
    }

    /// <summary>
    /// Mutable counters shared across the recursive composite apply, so the async
    /// <see cref="ApplyOneAsync"/> can track progress without ref parameters (which async methods
    /// cannot have). <see cref="Total"/> counts every device target across the entire tree.
    /// </summary>
    private sealed class ApplyCounters
    {
        public int Succeeded;
        public int Total;
    }

    /// <summary>
    /// Applies one shortcut's worth of work: for a device-targeted shortcut, every target; for a
    /// composite, every referenced shortcut in order (recursively). Either way it then continues
    /// down the shortcut's own <see cref="Shortcut.NextShortcutId"/> chain, so a referenced
    /// shortcut runs in full. Failures never abort the rest of the work — they're collected into
    /// <paramref name="failures"/> and reported once the whole chain has run.
    /// <paramref name="counters"/> accumulates device-target totals across the entire tree (all
    /// chain steps and all nested references), so "applied to N of M devices" stays meaningful for
    /// composites that ultimately control real bulbs.
    /// </summary>
    private async Task ApplyOneAsync(
        Shortcut shortcut,
        Dictionary<int, Shortcut> byId,
        List<ShortcutTargetFailure> failures,
        ApplyCounters counters,
        HashSet<int> recursionStack,
        CancellationToken cancellationToken)
    {
        // Defensive recursion cap: a corrupted database row must not spin forever through composite
        // references or chain links. By construction (ValidateCompositeReferences +
        // ValidateChainLink) the combined graph is acyclic, so a shortcut should only ever appear
        // once on the current path; if it does again, the data is corrupt and the rest of this path
        // is skipped. Added to / removed from the stack per path (not globally) so a shortcut
        // legitimately reached twice (e.g. referenced by two different composites) runs twice.
        if (!recursionStack.Add(shortcut.Id))
        {
            return;
        }

        try
        {
            if (shortcut.ReferencedShortcuts.Count > 0)
            {
                // Composite: run each referenced shortcut in order, waiting its configured delay
                // before the next one starts.
                foreach (var reference in shortcut.ReferencedShortcuts.OrderBy(r => r.Order))
                {
                    if (reference.ReferencedShortcutId is { } referencedId && byId.TryGetValue(referencedId, out var referenced))
                    {
                        await ApplyOneAsync(referenced, byId, failures, counters, recursionStack, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        // Referenced shortcut was deleted (SetNull leaves a null FK behind) —
                        // report it so the user isn't left wondering why one step never ran.
                        failures.Add(new ShortcutTargetFailure(string.Empty, string.Empty, "Referenced shortcut no longer exists.", shortcut.Id, shortcut.Name));
                    }

                    if (reference.DelaySeconds > 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(reference.DelaySeconds), _timeProvider, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            else
            {
                // Device-targeted: apply every target, one at a time, best-effort per target.
                foreach (var target in shortcut.Targets)
                {
                    counters.Total++;
                    try
                    {
                        await ApplyToTargetAsync(shortcut, target, cancellationToken).ConfigureAwait(false);
                        counters.Succeeded++;
                    }
                    catch (Exception ex)
                    {
                        failures.Add(new ShortcutTargetFailure(target.DeviceSku, target.DeviceId, ex.Message, shortcut.Id, shortcut.Name));
                    }
                }
            }

            // Then follow this shortcut's own chain link, if it has one. Waiting the configured
            // delay before the next step is what lets large chains space their bursts of Govee
            // calls across rate-limit windows.
            if (shortcut.NextShortcutId is { } nextId && byId.TryGetValue(nextId, out var next))
            {
                if (shortcut.NextShortcutDelaySeconds > 0)
                {
                    // Honors cancellation deliberately: Shortcuts.razor cancels its CancellationTokenSource
                    // on dispose, so navigating away mid-chain must abort the remaining steps rather than
                    // keep running in the background against a torn-down component. Do not "fix" this by
                    // swallowing OperationCanceledException here.
                    await Task.Delay(TimeSpan.FromSeconds(shortcut.NextShortcutDelaySeconds), _timeProvider, cancellationToken).ConfigureAwait(false);
                }
                await ApplyOneAsync(next, byId, failures, counters, recursionStack, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            recursionStack.Remove(shortcut.Id);
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
