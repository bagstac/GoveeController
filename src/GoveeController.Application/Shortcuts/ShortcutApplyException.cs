namespace GoveeController.Application.Shortcuts;

/// <summary>
/// One target device that failed while applying a shortcut, e.g. because it's offline.
/// </summary>
/// <param name="DeviceSku">The Govee product model of the target device.</param>
/// <param name="DeviceId">The target device's unique identifier.</param>
/// <param name="ErrorMessage">The underlying failure's message (typically from <c>GoveeApiException</c>).</param>
/// <param name="ShortcutId">Id of the chain step (shortcut) this target belonged to when it failed.</param>
/// <param name="ShortcutName">
/// Name of the chain step this target belonged to when it failed. A chain can run more than one
/// shortcut, so the device id alone is no longer enough to tell the user which step went wrong.
/// </param>
public sealed record ShortcutTargetFailure(
    string DeviceSku,
    string DeviceId,
    string ErrorMessage,
    int ShortcutId,
    string ShortcutName);

/// <summary>
/// Thrown when applying a shortcut (or the chain it starts) fails on one or more (but not
/// necessarily all) of the targets across every step. <see cref="SucceededCount"/> is always
/// &gt; 0 when this is thrown — a run whose every target failed would report the same way, but
/// callers can tell the two apart by comparing <see cref="SucceededCount"/> to <see cref="TotalCount"/>.
/// </summary>
public sealed class ShortcutApplyException : Exception
{
    /// <summary>Number of targets successfully applied to, summed across every step of the chain.</summary>
    public int SucceededCount { get; }

    /// <summary>Total number of targets attempted, summed across every step of the chain.</summary>
    public int TotalCount { get; }

    /// <summary>The targets that failed, and why.</summary>
    public IReadOnlyList<ShortcutTargetFailure> Failures { get; }

    /// <summary>Creates the exception.</summary>
    public ShortcutApplyException(int succeededCount, int totalCount, IReadOnlyList<ShortcutTargetFailure> failures)
        : base(BuildMessage(succeededCount, totalCount, failures))
    {
        SucceededCount = succeededCount;
        TotalCount = totalCount;
        Failures = failures;
    }

    private static string BuildMessage(int succeededCount, int totalCount, IReadOnlyList<ShortcutTargetFailure> failures)
    {
        var failureDetails = string.Join("; ", failures.Select(f => $"{f.ShortcutName} - {f.DeviceSku} ({f.DeviceId}): {f.ErrorMessage}"));
        return $"Applied to {succeededCount} of {totalCount} device(s). Failed: {failureDetails}";
    }
}
