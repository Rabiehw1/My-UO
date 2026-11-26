using System.Text.Json;
using ClassicUO.Configuration;
using Xunit;

namespace ClassicUO.UnitTests;

public class ReconnectDelayTests
{
    [Theory]
    [InlineData(int.MinValue, 1)]
    [InlineData(0, 1)]
    [InlineData(15, 15)]
    [InlineData(int.MaxValue, Settings.MaxReconnectSeconds)]
    public void ReconnectSecondsStayConvertibleToMilliseconds(int input, int expected)
    {
        var settings = new Settings { ReconnectTime = input };

        Assert.Equal(expected, settings.ReconnectTime);
        Assert.Equal(expected * 1000, settings.ReconnectDelayMilliseconds);
    }

    [Fact]
    public void OldOutOfRangeSettingsAreNormalizedWhenLoaded()
    {
        Settings settings = JsonSerializer.Deserialize("{\"reconnect_time\":2147483647}", SettingsJsonContext.RealDefault.Settings);

        Assert.Equal(Settings.MaxReconnectSeconds, settings.ReconnectTime);
    }
}
