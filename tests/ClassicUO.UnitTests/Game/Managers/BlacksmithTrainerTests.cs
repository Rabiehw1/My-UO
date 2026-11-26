using ClassicUO.Game.Managers.Trainer;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers;

public class BlacksmithTrainerTests
{
    [Theory]
    [InlineData(109, null, 0)]
    [InlineData(110, "Female Breastplate", 25)]
    [InlineData(659, "Female Breastplate", 25)]
    [InlineData(660, "Platemail Gorget", 10)]
    [InlineData(690, "Platemail Gloves", 12)]
    [InlineData(720, "Plate Helm", 15)]
    [InlineData(760, "Platemail Arms", 18)]
    [InlineData(790, "Platemail Legs", 20)]
    [InlineData(850, "Breastplate", 25)]
    [InlineData(950, "Breastplate", 25)]
    [InlineData(1500, "Breastplate", 25)]
    public void ChoosesHighestUnlockedPlate(int skill, string name, int ingots)
    {
        var recipe = BlacksmithTrainer.SelectRecipe(skill);
        Assert.Equal(name, recipe?.Name);
        Assert.Equal(ingots, recipe?.Ingots ?? 0);
    }

    [Theory]
    [InlineData(949, 0, true)]
    [InlineData(949, 1, false)]
    [InlineData(950, 0, false)]
    [InlineData(950, 10, true)]
    [InlineData(999, 15, false)]
    [InlineData(1000, 15, true)]
    public void UsesOnlyAppropriateMetal(int skill, int difficulty, bool expected) =>
        Assert.Equal(expected, BlacksmithTrainer.Eligible(skill, difficulty));

    [Fact]
    public void RegularBreastplateNeverMatchesFemaleBreastplate() =>
        Assert.False(BlacksmithTrainer.IsRecipeLabel("Breastplate", "female breastplate"));
}
