using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers;

public class WorldMapEntityManagerTests
{
    [Fact]
    public void AnUnconfirmedShardDoesNotReceiveQueriesByDefault()
    {
        var settings = new ServerSettingsSave();

        Assert.False(WorldMapEntityManager.CanQueryServer(false,
            settings.UseLegacyWorldMapTracking, usesNewMovement: false, encrypted: false));
    }

    [Theory]
    [InlineData(false, true, false, false, true)]
    [InlineData(false, true, true, false, false)]
    [InlineData(false, true, false, true, false)]
    [InlineData(true, false, false, false, true)]
    [InlineData(true, false, true, false, true)]
    [InlineData(true, true, false, true, false)]
    public void QueriesRequireConfirmedSupportOrCompatibleLegacyOptIn(
        bool acknowledged, bool legacyOptIn, bool usesNewMovement, bool encrypted, bool expected)
    {
        Assert.Equal(expected, WorldMapEntityManager.CanQueryServer(acknowledged, legacyOptIn, usesNewMovement, encrypted));
    }
}
