using ClassicUO.Game.Managers.Trainer;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers;

public class TailoringTrainerTests
{
    [Theory]
    [InlineData(1050, 0, 14, "Studded Tunic")]
    [InlineData(1050, 0, 12, "Studded Leggings")]
    [InlineData(1050, 10, 14, "Studded Gloves")]
    [InlineData(2160, 115, 12, "Studded Leggings")]
    [InlineData(1009, 0, 14, "Studded Sleeves")]
    [InlineData(160, 0, 20, null)]
    [InlineData(2200, -1, 20, null)]
    public void ChoosesAffordableArmor(int skill, int difficulty, int amount, string expected) =>
        Assert.Equal(expected, TailoringTrainer.SelectRecipe(skill, difficulty, amount)?.Name);

    [Fact]
    public void DistinguishesDragonTiers() =>
        Assert.Equal(130, TailoringTrainer.HideDifficulty("Golden Dragon hides", 123));

    [Theory]
    [InlineData(200, 1, "", 200, true)]
    [InlineData(201, 1, "", 200, false)]
    [InlineData(200, 2, "", 200, false)]
    [InlineData(201, 1, "gloves", 0, true)]
    [InlineData(201, 1, "Boots", 0, false)]
    public void OutputFollowsRecipeMenuAndMaterial(int graphic, int hue, string name, int menuGraphic, bool expected) =>
        Assert.Equal(expected, TailoringTrainer.MatchesOutput((ushort)graphic, (ushort)hue, name, (ushort)menuGraphic, 1, "Gloves"));
}
