using GoveeController.Domain.Devices;

namespace GoveeController.Web.Components.Shared;

/// <summary>
/// Computes a capability bound (e.g. color-temperature min/max) as the intersection across a set
/// of devices - the most restrictive bound of all of them, so any value derived from it is
/// guaranteed valid for every device it might get applied to. Shared by the Devices page's bulk
/// controls and the Shortcuts page's form, which both need this for the same reason.
/// </summary>
internal static class CapabilityRange
{
    public static int Bound(
        IEnumerable<Device> devices,
        CapabilityKind kind,
        Func<DeviceCapability, int> selectBound,
        Func<IEnumerable<int>, int> combine,
        int fallback)
    {
        var bounds = devices
            .SelectMany(d => d.Capabilities.Where(c => c.Kind == kind))
            .Select(selectBound)
            .ToList();
        return bounds.Count == 0 ? fallback : combine(bounds);
    }
}
