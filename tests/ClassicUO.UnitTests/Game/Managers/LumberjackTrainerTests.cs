using ClassicUO.Game.Managers.Trainer;
using Microsoft.Xna.Framework;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers;

public class LumberjackTrainerTests
{
    [Theory]
    [InlineData(10, 10, true)]
    [InlineData(9, 11, true)]
    [InlineData(11, 9, true)]
    [InlineData(12, 10, false)]
    [InlineData(10, 8, false)]
    public void AxeReachIncludesDiagonalsButStopsAtOneTile(int x, int y, bool expected) =>
        Assert.Equal(expected, LumberjackTrainer.WithinReach(x, y, new Point(10, 10)));

    [Fact]
    public void CrafterUsesTheCharactersPickedAxe()
    {
        var skill = TrainerCatalog.Find("lumberjacking");
        Assert.Equal(TrainerClass.Crafter, skill.Class);
        Assert.Equal(TrainerTargetKind.Item, skill.Target);
        Assert.Equal(TrainerUse.Lumberjack, skill.Use);
    }
}
