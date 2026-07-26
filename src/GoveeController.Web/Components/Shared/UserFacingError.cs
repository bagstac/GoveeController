using GoveeController.Application.Shortcuts;
using GoveeController.Infrastructure.Govee;

namespace GoveeController.Web.Components.Shared;

/// <summary>
/// Converts an exception caught in a component into a message safe to display in the UI.
/// </summary>
internal static class UserFacingError
{
    /// <summary>
    /// <see cref="GoveeApiException"/>'s message comes from Govee itself (e.g. "Device is
    /// offline"), <see cref="ShortcutApplyException"/>'s is built entirely from that same kind of
    /// per-device detail, and <see cref="ArgumentException"/> is this app's own deliberate input
    /// validation feedback (e.g. "Brightness must be between 1 and 100") — all three are safe and
    /// useful to show directly. Any other exception type is unexpected and could carry internal
    /// detail (file paths, connection strings) that shouldn't reach the browser, so it's logged in
    /// full server-side and replaced with a message that says only that something went wrong.
    /// </summary>
    /// <param name="ex">The caught exception.</param>
    /// <param name="logger">Logger to record the full exception against, when it isn't shown as-is.</param>
    /// <param name="context">Short description of what was being attempted, e.g. "Applying shortcut".</param>
    public static string From(Exception ex, ILogger logger, string context)
    {
        if (ex is GoveeApiException or ShortcutApplyException or ArgumentException)
        {
            return $"{context} failed: {ex.Message}";
        }

        logger.LogError(ex, "{Context} failed unexpectedly", context);
        return $"{context} failed unexpectedly. Check the server logs for details.";
    }
}
