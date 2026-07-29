namespace GoveeController.Web.Components.Shared;

/// <summary>
/// Builds the list of Kelvin values offered by a color-temperature dropdown. Govee reports
/// colorTemperatureK as a continuous integer range (confirmed against this account's real
/// devices: 2700-6500K, precision 1), not a fixed preset list, so every individual Kelvin value
/// would mean thousands of options - this buckets a device's actual min/max into 100K steps
/// instead, which stays genuinely derived from the API response while keeping the list usable.
/// </summary>
internal static class WarmthOptions
{
    private const int Step = 100;

    /// <summary>
    /// Generates the option list for the range [<paramref name="min"/>, <paramref name="max"/>],
    /// always including <paramref name="currentKelvin"/> even if it falls off the 100K grid (e.g.
    /// a Govee scene set an odd value), so a dropdown built from this never shows a mismatched
    /// selection.
    /// </summary>
    public static IReadOnlyList<int> Generate(int min, int max, int currentKelvin)
    {
        var values = new List<int>();
        for (var kelvin = min; kelvin < max; kelvin += Step)
        {
            values.Add(kelvin);
        }
        values.Add(max);

        if (!values.Contains(currentKelvin))
        {
            values.Add(currentKelvin);
            values.Sort();
        }

        return values;
    }
}
