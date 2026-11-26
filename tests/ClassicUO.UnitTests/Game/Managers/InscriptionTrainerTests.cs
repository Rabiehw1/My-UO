using ClassicUO.Game.Managers.Trainer;
using ClassicUO.Game.Managers.ShardSpells;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers;

public class InscriptionTrainerTests
{
    [Theory]
    [InlineData(false, 19, 100, false, true)]
    [InlineData(false, 20, 100, false, false)]
    [InlineData(false, 20, 101, false, true)]
    [InlineData(true, 90, 100, false, true)]
    [InlineData(true, 99, 100, false, true)]
    [InlineData(true, 100, 100, true, true)]
    [InlineData(true, 100, 100, false, false)]
    public void RecoveryWaitsForFullManaAndMeditationToEnd(bool recovering, int mana, int maximum,
        bool meditating, bool expected) =>
        Assert.Equal(expected, InscriptionTrainer.NeedsRecovery(recovering, mana, maximum, meditating));

    [Theory]
    [InlineData(null, 30, 50)]
    [InlineData(null, 31, 60)]
    [InlineData(null, 63, 100)]
    [InlineData(ShardSpellBook.Necro, 8, 150)]
    [InlineData(ShardSpellBook.Earth, 11, 140)]
    [InlineData(ShardSpellBook.Holy, 8, -1)]
    [InlineData(ShardSpellBook.Holy, 15, 160)]
    [InlineData(ShardSpellBook.Songs, 0, -1)]
    public void UsesFromBookDifficultyForMappedSpells(ShardSpellBook? book, int index, int expected) =>
        Assert.Equal(expected, InscriptionTrainer.Difficulty(book, index));
}
