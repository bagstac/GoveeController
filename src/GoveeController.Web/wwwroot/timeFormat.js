// Formats a UTC ISO timestamp in the viewer's own local timezone. Blazor Server runs entirely
// server-side, so C#'s DateTime.ToLocalTime() would use the *server's* timezone (e.g. the
// container's, which may not match whoever is viewing the page) — this needs to happen in the
// browser instead.
window.goveeController = window.goveeController || {};
window.goveeController.formatLocalTime = function (isoUtc) {
    return new Date(isoUtc).toLocaleTimeString([], { hour: "numeric", minute: "2-digit", second: "2-digit" });
};
