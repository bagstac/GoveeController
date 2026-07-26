namespace GoveeController.Application.Shortcuts;

/// <summary>
/// One target device that failed while applying a shortcut, e.g. because it's offline.
/// </summary>
/// <param name="DeviceSku">The Govee product model of the target device.</param>
/// <param name="DeviceId">The target device's unique identifier.</param>
/// <param name="ErrorMessage">The underlying failure's message (typically from <c>GoveeApiException</c>).</param>
public sealed record ShortcutTargetFailure(string DeviceSku, string DeviceId, string ErrorMessage);

/// <summary>
/// Thrown when applying a shortcut fails on one or more (but not necessarily all) of its target
/// devices. <see cref="SucceededCount"/> is always &gt; 0 when this is thrown — a shortcut whose
/// every target failed would report the same way, but callers can tell the two apart by comparing
/// <see cref="SucceededCount"/> to <see cref="TotalCount"/>.
/// </summary>
public sealed class ShortcutApplyException : Exception
{
    /// <summary>Number of targets the shortcut was successfully applied to.</summary>
    public int SucceededCount { get; }

    /// <summary>Total number of targets the shortcut has.</summary>
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
        var failureDetails = string.Join("; ", failures.Select(f => $"{f.DeviceSku} ({f.DeviceId}): {f.ErrorMessage}"));
        return $"Applied to {succeededCount} of {totalCount} device(s). Failed: {failureDetails}";
    }
}
