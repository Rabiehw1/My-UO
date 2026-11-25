using System.Linq;
using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers;

public class WeaponSwitchCatalogTests
{
    [Fact]
    public void SeparatesMonstersWithTheSameGraphicByHue()
    {
        var shadow = WeaponSwitchCatalog.Find(0x000C, 0x4631);
        var silver = WeaponSwitchCatalog.Find(0x000C, 0x08FD);

        Assert.Equal(WeaponLoadout.EarthElemental, shadow!.Primary);
        Assert.Equal(WeaponLoadout.DragonSlayer, silver!.Primary);
        Assert.Null(WeaponSwitchCatalog.Find(0x000C, 0));
    }

    [Fact]
    public void MatchesWildcardHuesAndKeepsDistinctMonsterKeys()
    {
        Assert.Equal("Horned Rat", WeaponSwitchCatalog.Find(0x0172, 0x0620)!.Name);
        Assert.Equal("Liche Monsters", WeaponSwitchCatalog.Find(0x0018, 0x0111)!.Name);
        Assert.Equal(WeaponSwitchCatalog.Monsters.Count,
            WeaponSwitchCatalog.Monsters.Select(monster => monster.Key).Distinct().Count());
    }

    [Fact]
    public void UsesScriptAssignmentsUntilACharacterOverridesThem()
    {
        var monster = WeaponSwitchCatalog.Find(0x0310, 0)!;
        var settings = new WeaponSwitchSettings();

        Assert.Equal(new MonsterLoadouts(WeaponLoadout.Staff, WeaponLoadout.Holy), settings.GetAssignment(monster));
        settings.Assignments[monster.Key] = new(WeaponLoadout.Sword, null);
        Assert.Equal(new MonsterLoadouts(WeaponLoadout.Sword, null), settings.GetAssignment(monster));
        Assert.Equal(new WeaponSet(), settings.GetSet(WeaponLoadout.Sword));
    }
}
