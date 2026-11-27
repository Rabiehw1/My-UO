using ClassicUO.Game.Managers.Trainer;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers;

public class TailoringTrainerTests
{
    [Theory]
    [InlineData(1050, 0, 14, "Studded Tunic")]
    [InlineData(1050, 0, 12, "Studded Leggings")]
    [InlineData(1009, 0, 14, "Studded Sleeves")]
    [InlineData(160, 0, 20, null)]
    [InlineData(1079, 0x07E2, 14, "Studded Leggings")]
    [InlineData(1080, 0x07E2, 14, "Studded Tunic")]
    [InlineData(1390, 0x0494, 12, "Studded Leggings")]
    [InlineData(1429, 0x0494, 14, "Studded Leggings")]
    [InlineData(1430, 0x0494, 14, "Studded Tunic")]
    [InlineData(1490, 0x0482, 14, "Studded Leggings")]
    [InlineData(1500, 0x048D, 14, "Studded Leggings")]
    [InlineData(2000, 0xFFFF, 20, null)]
    public void ChoosesAffordableArmorUsingMeasuredHideThresholds(int skill, int hue, int amount, string expected) =>
        Assert.Equal(expected, TailoringTrainer.SelectRecipe(skill, (ushort)hue, amount)?.Name);

    [Theory]
    [InlineData(200, 1, "", 200, true)]
    [InlineData(201, 1, "", 200, false)]
    [InlineData(200, 2, "", 200, false)]
    [InlineData(201, 1, "gloves", 0, true)]
    [InlineData(201, 1, "Boots", 0, false)]
    public void OutputFollowsRecipeMenuAndMaterial(int graphic, int hue, string name, int menuGraphic, bool expected) =>
        Assert.Equal(expected, TailoringTrainer.MatchesOutput((ushort)graphic, (ushort)hue, name, (ushort)menuGraphic, 1, "Gloves"));
}
