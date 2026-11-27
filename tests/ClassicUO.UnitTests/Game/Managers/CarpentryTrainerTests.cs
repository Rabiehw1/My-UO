using ClassicUO.Game.Managers.Trainer;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers;

public class CarpentryTrainerTests
{
    [Theory]
    [InlineData(939, 0x0000, 10, -1)]
    [InlineData(940, 0x0000, 10, 94)]
    [InlineData(969, 0x046C, 10, -1)]
    [InlineData(970, 0x046C, 10, 97)]
    [InlineData(1030, 0x0415, 9, -1)]
    [InlineData(1030, 0x0415, 10, 103)]
    [InlineData(1300, 0x048D, 10, 130)]
    [InlineData(1500, 0xFFFF, 10, -1)]
    public void RanksMeasuredWoodOnlyWhenSkillAndStockQualify(int skill, int hue, int amount, int rank) =>
        Assert.Equal(rank, PracticeTrainer.BlackStaffRank(skill, (ushort)hue, amount));
}
