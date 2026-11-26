#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ClassicUO.Game.Managers.ShardSpells;
using ClassicUO.Game.Managers.Support;
using FluentAssertions;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers.Support;

public sealed class SupportAgentSaveTests
{
    public static IEnumerable<object[]> ClassesWithAbilities => Enum.GetValues<SupportClass>()
        .Where(supportClass => supportClass != SupportClass.Ranger)
        .Select(supportClass => new object[] { supportClass });

    [Theory]
    [MemberData(nameof(ClassesWithAbilities))]
    public void Presets_SupportAbilitiesHaveUniqueIds(SupportClass supportClass)
    {
        List<SupportAbility> abilities = SupportClassPresets.Create(supportClass);

        abilities.Should().NotBeEmpty();
        abilities.Select(a => a.Id).Should().OnlyHaveUniqueItems();
        abilities.Should().OnlyContain(a => !string.IsNullOrWhiteSpace(a.Name));
    }

    [Fact]
    public void RangerPreset_RemovesSavedSpellsAndKeepsCombatSettings()
    {
        var save = new SupportAgentSave { Class = SupportClass.Ranger, PresetVersion = SupportClassPresets.PRESET_VERSION };
        SupportClassSettings ranger = save.GetClassSettings(SupportClass.Ranger);
        ranger.Abilities.Should().BeEmpty();
        save.ActiveCombat.AttackRange = 7;
        ranger.Abilities.Add(new SupportAbility { Id = "ranger.cure", Action = SupportActionType.CastSpell, SpellId = 11 });

        save.AddNewPresetAbilities();

        ranger.Abilities.Should().BeEmpty();
        save.ActiveCombat.AttackRange.Should().Be(7);
    }

    [Fact]
    public void Presets_Bard_UsesTheSharedSongbookForAreaHeal()
    {
        SupportAbility heal = SupportClassPresets.Create(SupportClass.Bard).Single();

        heal.Role.Should().Be(SupportRole.Heal);
        heal.Action.Should().Be(SupportActionType.CastShardSpell);
        heal.Target.Should().Be(SupportTargetMode.None);
        heal.ShardBook.Should().Be(ShardSpellBook.Songs);
        ShardSpellCatalog.GetSpell(heal.ShardBook, heal.SpellId)?.DefaultButton.Should().Be(7);

        // No item until the player picks their own songbook.
        heal.ItemSerial.Should().Be(0u);
    }

    [Fact]
    public void BardSongMigration_KeepsTimingAndExposesOldBookOnce()
    {
        var save = new SupportAgentSave();
        SupportAbility song = save.GetClassSettings(SupportClass.Bard).Abilities.Single();
        song.Action = SupportActionType.UseItemAndGumpButton;
        song.ItemSerial = 0x40001234;
        song.GumpButton = 9;
        song.RecoveryMs = 6500;
        song.Enabled = false;

        SupportClassPresets.ConvertLegacyBardSong(save, out int button).Should().Be(0x40001234u);

        button.Should().Be(9);
        song.Action.Should().Be(SupportActionType.CastShardSpell);
        song.ItemSerial.Should().Be(0u);
        song.RecoveryMs.Should().Be(6500);
        song.Enabled.Should().BeFalse();
        SupportClassPresets.ConvertLegacyBardSong(save, out _).Should().Be(0u);
    }

    [Fact]
    public void RangerOrder_DeduplicatesOnlyAfterSendingTheTarget()
    {
        var order = new RangerPetOrder();
        order.TryBegin(1, 2, 100).Should().BeTrue();
        order.IsWaitingForCursor(101).Should().BeTrue();
        order.TryBegin(1, 3, 200).Should().BeFalse();
        order.Complete();
        order.IsWaitingForCursor(350).Should().BeFalse();
        order.TryBegin(1, 2, 400).Should().BeFalse();
        order.TryBegin(1, 3, 400).Should().BeTrue();
        order.Reset();
        order.IsWaitingForCursor(450).Should().BeFalse();
        order.TryBegin(1, 2, 450).Should().BeTrue();
    }

    [Fact]
    public void RangerOrder_RetriesOnceAfterTimeoutWithBackoff()
    {
        var order = new RangerPetOrder();
        order.TryBegin(1, 2, 100).Should().BeTrue();
        order.IsWaitingForCursor(1850).Should().BeFalse();
        order.TryBegin(1, 2, 1850).Should().BeFalse();
        order.TryBegin(1, 2, 2600).Should().BeTrue();
        order.TryBegin(1, 2, 5100).Should().BeFalse();
        order.TryBegin(1, 3, 5100).Should().BeTrue();
    }

    [Fact]
    public void BardPolicy_AllowsHealingAsItsOnlyAbilityRole()
    {
        SupportClassPresets.CanAttack(SupportClass.Bard).Should().BeFalse();
        Enum.GetValues<SupportRole>()
            .Where(role => SupportClassPresets.CanUseRole(SupportClass.Bard, role))
            .Should().Equal(SupportRole.Heal);
    }

    [Fact]
    public void Presets_ReturnIndependentCopies()
    {
        List<SupportAbility> first = SupportClassPresets.Create(SupportClass.Mage);
        first[0].Enabled = !first[0].Enabled;

        SupportClassPresets.Create(SupportClass.Mage)[0].Enabled.Should().Be(!first[0].Enabled);
    }

    [Fact]
    public void GetClassSettings_KeepsEachClassSeparate()
    {
        var save = new SupportAgentSave();

        SupportClassSettings bard = save.GetClassSettings(SupportClass.Bard);
        bard.Abilities[0].Enabled = false;

        save.GetClassSettings(SupportClass.Warrior).Abilities.Should().OnlyContain(a => a.Enabled);
        save.GetClassSettings(SupportClass.Bard).Should().BeSameAs(bard);
        save.Classes.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(SupportClass.Ranger, true, 4)]
    [InlineData(SupportClass.MysticArcher, true, 5)]
    [InlineData(SupportClass.BladeSinger, false, 2)]
    [InlineData(SupportClass.Paladin, false, 2)]
    public void NewClassPositioning_RoundTripsWithoutOverwritingOriginalSettings(
        SupportClass supportClass, bool keepDistance, int followDistance)
    {
        var save = new SupportAgentSave { Class = supportClass, PresetVersion = SupportClassPresets.PRESET_VERSION };
        save.Follow.FollowDistance = 3;
        save.Combat.AttackRange = 7;
        save.ActiveFollow.FollowDistance.Should().Be(followDistance);
        save.ActiveCombat.KeepDistance.Should().Be(keepDistance);
        save.ActiveCombat.AttackRange.Should().Be(keepDistance ? 8 : 1);
        save.ActiveFollow.FollowDistance++;
        save.ActiveCombat.AttackRange++;

        string json = JsonSerializer.Serialize(save, SupportAgentJsonContext.Default.SupportAgentSave);
        SupportAgentSave loaded = JsonSerializer.Deserialize(json, SupportAgentJsonContext.Default.SupportAgentSave)!;
        loaded.AddNewPresetAbilities();

        loaded.Class.Should().Be(supportClass);
        loaded.ActiveFollow.FollowDistance.Should().Be(followDistance + 1);
        loaded.ActiveCombat.AttackRange.Should().Be(keepDistance ? 9 : 2);
        loaded.Class = SupportClass.Mage;
        loaded.ActiveFollow.FollowDistance.Should().Be(3);
        loaded.ActiveCombat.AttackRange.Should().Be(7);
    }

    [Theory]
    [InlineData(SupportClass.MysticArcher, ShardSpellBook.Earth, "Nature's Touch", SupportTargetMode.Patient)]
    [InlineData(SupportClass.BladeSinger, ShardSpellBook.Songs, "Song of Life", SupportTargetMode.None)]
    [InlineData(SupportClass.Paladin, ShardSpellBook.Holy, "Angelic Aura", SupportTargetMode.Patient)]
    public void BookPreset_MatchesCatalogAndTargetMode(
        SupportClass supportClass, ShardSpellBook book, string spellName, SupportTargetMode target)
    {
        SupportAbility ability = SupportClassPresets.Create(supportClass).First(a => a.Role == SupportRole.Heal);

        ability.Action.Should().Be(SupportActionType.CastShardSpell);
        ability.ShardBook.Should().Be(book);
        ability.Target.Should().Be(target);
        ShardSpellCatalog.GetSpell(book, ability.SpellId)?.Name.Should().Be(spellName);
    }

    [Fact]
    public void Json_RoundTrip_KeepsSettingsAndWritesEnumsAsText()
    {
        var save = new SupportAgentSave { Enabled = true, Class = SupportClass.Mage, LeaderSerial = 0x1234, HealThreshold = 65 };
        save.Combat.KeepDistance = false;
        save.GetClassSettings(SupportClass.Mage).Abilities[0].CastTimeMs = 1500;

        string json = JsonSerializer.Serialize(save, SupportAgentJsonContext.Default.SupportAgentSave);
        SupportAgentSave? loaded = JsonSerializer.Deserialize(json, SupportAgentJsonContext.Default.SupportAgentSave);

        json.Should().Contain("\"Mage\"");
        loaded.Should().NotBeNull();
        loaded!.Enabled.Should().BeTrue();
        loaded.Class.Should().Be(SupportClass.Mage);
        loaded.LeaderSerial.Should().Be(0x1234u);
        loaded.HealThreshold.Should().Be(65);
        loaded.Combat.KeepDistance.Should().BeFalse();
        loaded.GetClassSettings(SupportClass.Mage).Abilities[0].CastTimeMs.Should().Be(1500);
    }

    [Fact]
    public void AddNewPresetAbilities_AddsMissingMageAbilitiesOnce()
    {
        var save = new SupportAgentSave();
        save.GetClassSettings(SupportClass.Mage).Abilities.RemoveAll(a => a.Role == SupportRole.Bless);

        save.AddNewPresetAbilities();
        save.AddNewPresetAbilities();

        save.GetClassSettings(SupportClass.Mage).Abilities.Should().ContainSingle(a => a.Role == SupportRole.Bless);
    }
}
