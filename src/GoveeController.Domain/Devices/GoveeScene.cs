namespace GoveeController.Domain.Devices;

/// <summary>
/// A dynamic light scene (or DIY effect) defined in the user's Govee account for a specific
/// device, as returned by the "Get dynamic scene" endpoint. <see cref="ParamId"/> and
/// <see cref="Id"/> must be echoed back verbatim to activate the scene via the control endpoint.
/// </summary>
/// <param name="Name">The scene's display name, as configured in the Govee Home app (e.g. "Sunset", "Movie Night").</param>
/// <param name="ParamId">Opaque scene parameter identifier required to trigger the scene.</param>
/// <param name="Id">Opaque scene identifier required to trigger the scene.</param>
public sealed record GoveeScene(string Name, int ParamId, int Id);
