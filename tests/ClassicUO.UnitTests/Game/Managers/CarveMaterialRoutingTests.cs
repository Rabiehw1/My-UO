using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers;

public class CarveMaterialRoutingTests
{
    [Theory]
    [InlineData(0x1079, true)] // Hides
    [InlineData(0x1081, true)] // Cut leather
    [InlineData(0x09F1, true)] // Raw ribs
    [InlineData(0x1BD1, true)] // Feathers
    [InlineData(0x101F, true)] // Tainted wool
    [InlineData(0x26B4, true)] // Scales
    [InlineData(0, false)]
    [InlineData(0x0E20, false)] // Bloody bandages
    [InlineData(0x0E21, true)] // Bandages
    [InlineData(0x0E22, true)] // Alternate bandage art
    [InlineData(0x0EED, true)] // Gold
    [InlineData(0x0F0C, true)] // Potion
    public void RoutesNewItemsExceptBloodyBandages(int graphic, bool expected)
    {
        Assert.Equal(expected, AutoSkinningManager.CanRouteCarvedItem((ushort)graphic));
    }

    [Fact]
    public void CountsOnlyCarvedGrowthAndReducesTheQuantityWhenAStackShrinks()
    {
        int carved = AutoSkinningManager.UpdateCarvedAmount(0, 110 - 100, canAdd: true);
        Assert.Equal(10, carved);

        carved = AutoSkinningManager.UpdateCarvedAmount(carved, 114 - 110, canAdd: true);
        Assert.Equal(14, carved);

        carved = AutoSkinningManager.UpdateCarvedAmount(carved, 134 - 114, canAdd: false);
        Assert.Equal(14, carved);

        carved = AutoSkinningManager.UpdateCarvedAmount(carved, 129 - 134, canAdd: false);
        Assert.Equal(9, carved);

        carved = AutoSkinningManager.UpdateCarvedAmount(carved, 100 - 129, canAdd: true);
        Assert.Equal(0, carved);
    }
}
