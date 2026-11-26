using ClassicUO.Game.Managers.Trainer;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers;

public class TailoringTrainerTests
{
    [Fact]
    public void NewProfilesChooseTheirOwnRecipe()
    {
        var settings = new TrainerSettings();
        Assert.Equal("", TailoringTrainer.RecipePath(settings));
        settings.TailorStuddedTunic = true;
        Assert.EndsWith("Studded Tunic", TailoringTrainer.RecipePath(settings));
        settings.TailorRecipePath = "Leather > Gloves";
        Assert.Equal(settings.TailorRecipePath, TailoringTrainer.RecipePath(settings));
    }

    [Theory]
    [InlineData(200, 1, "", 200, true)]
    [InlineData(201, 1, "", 200, false)]
    [InlineData(200, 2, "", 200, false)]
    [InlineData(201, 1, "gloves", 0, true)]
    [InlineData(201, 1, "Boots", 0, false)]
    public void OutputFollowsRecipeMenuAndMaterial(int graphic, int hue, string name, int menuGraphic, bool expected) =>
        Assert.Equal(expected, TailoringTrainer.MatchesOutput((ushort)graphic, (ushort)hue, name, (ushort)menuGraphic, 1, "Gloves"));
}
