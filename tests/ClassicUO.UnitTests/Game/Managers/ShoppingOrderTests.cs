using System.Collections.Generic;
using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers;

public class ShoppingOrderTests
{
    [Fact]
    public void OverlappingRequestsReserveOnlyTheOfferAndKeepOneOrderLinePerSerial()
    {
        const uint serial = 0x40000001;
        var order = new Dictionary<uint, ushort>();

        Assert.Equal(7, BuySellAgent.ReserveStock(order, serial, available: 10, requested: 7));
        Assert.Equal(3, BuySellAgent.ReserveStock(order, serial, available: 10, requested: 6));
        Assert.Equal(0, BuySellAgent.ReserveStock(order, serial, available: 10, requested: 5));

        Assert.Equal(new KeyValuePair<uint, ushort>(serial, 10), Assert.Single(order));
    }
}
