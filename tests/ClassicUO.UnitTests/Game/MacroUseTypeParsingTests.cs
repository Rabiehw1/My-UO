using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests.Game;

public class MacroUseTypeParsingTests
{
    [Fact]
    public void Parse_DistinguishesAnExplicitZeroHueFromAnyHue()
    {
        Assert.True(MacroManager.TryParseUseType("  0x0F0E\t  0 ", out ushort graphic, out ushort? hue));
        Assert.Equal((ushort)3854, graphic);
        Assert.Equal((ushort)0, hue);

        Assert.True(MacroManager.TryParseUseType("3854", out graphic, out hue));
        Assert.Equal((ushort)3854, graphic);
        Assert.Null(hue);
    }

    [Theory]
    [InlineData("3854 blue")]
    [InlineData("3854 -1")]
    [InlineData("3854 65536")]
    [InlineData("3854 0 extra")]
    [InlineData("0x10000 0")]
    [InlineData(" ")]
    public void Parse_RejectsInvalidFields(string text)
    {
        Assert.False(MacroManager.TryParseUseType(text, out _, out _));
    }
}
