using GoveeController.Domain.Devices;
using Xunit;

namespace GoveeController.Application.Tests.Devices;

public class RgbColorTests
{
    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(255, 255, 255, 16777215)]
    [InlineData(255, 0, 0, 0xFF0000)]
    [InlineData(0, 255, 0, 0x00FF00)]
    [InlineData(0, 0, 255, 0x0000FF)]
    public void ToPackedInt_And_FromPackedInt_RoundTrip(byte r, byte g, byte b, int expectedPacked)
    {
        var color = new RgbColor(r, g, b);

        Assert.Equal(expectedPacked, color.ToPackedInt());
        Assert.Equal(color, RgbColor.FromPackedInt(expectedPacked));
    }

    [Theory]
    [InlineData("#FF8000", "#FF8000")]
    [InlineData("ff8000", "#FF8000")]
    [InlineData("#000000", "#000000")]
    [InlineData("#ffffff", "#FFFFFF")]
    public void FromHex_And_ToHex_RoundTrip(string input, string expectedHex)
    {
        var color = RgbColor.FromHex(input);

        Assert.Equal(expectedHex, color.ToHex());
    }

    [Theory]
    [InlineData("")]
    [InlineData("#ABC")]
    [InlineData("#GGGGGG")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    public void FromHex_Throws_OnMalformedInput(string input)
    {
        Assert.ThrowsAny<FormatException>(() => RgbColor.FromHex(input));
    }
}
