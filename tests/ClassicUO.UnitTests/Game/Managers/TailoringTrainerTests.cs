using ClassicUO.Game.Managers.Trainer;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers;

public class TailoringTrainerTests
{
    [Theory]
    [InlineData(true, "Studded Leather Armors", true)]
    [InlineData(false, "Studded Leggings", true)]
    [InlineData(false, "Studded Gorget", false)]
    [InlineData(true, "Studded Leggings", false)]
    public void SelectsOnlyTheExpectedMenuEntry(bool category, string entry, bool expected)
    {
        Assert.Equal(expected, TailoringTrainer.MatchesMenu(category, "What would you like to make?", entry, false));
        Assert.False(TailoringTrainer.MatchesMenu(category, "Another menu", entry, false));
    }

    [Fact]
    public void MatchesSelectedRecipeAndLeatherHue()
    {
        Assert.True(TailoringTrainer.MatchesMenu(false, "What would you like to make?", "Studded Tunic", true));
        Assert.True(TailoringTrainer.IsOutput(0x13DB, 0, true, 0));
        Assert.False(TailoringTrainer.IsOutput(0x13DA, 0, true, 0));
        Assert.True(TailoringTrainer.IsOutput(0x13DA, 0x0486, false, 0x0486));
        Assert.False(TailoringTrainer.IsOutput(0x13DA, 0, false, 0x0486));
        Assert.False(TailoringTrainer.IsOutput(0x13D6, 0x0486, false, 0x0486));
    }
}
