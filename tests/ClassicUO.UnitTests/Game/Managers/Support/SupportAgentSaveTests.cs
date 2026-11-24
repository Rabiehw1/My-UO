#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ClassicUO.Game.Managers.Support;
using FluentAssertions;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers.Support;

public sealed class SupportAgentSaveTests
{
    public static IEnumerable<object[]> AllClasses => Enum.GetValues<SupportClass>().Select(c => new object[] { c });

    [Theory]
    [MemberData(nameof(AllClasses))]
    public void Presets_EveryClass_HasAbilitiesWithUniqueIds(SupportClass supportClass)
    {
        List<SupportAbility> abilities = SupportClassPresets.Create(supportClass);

        abilities.Should().NotBeEmpty();
        abilities.Select(a => a.Id).Should().OnlyHaveUniqueItems();
        abilities.Should().OnlyContain(a => !string.IsNullOrWhiteSpace(a.Name));
    }

    [Fact]
    public void Presets_Bard_UsesPickedSongbookButtonSevenForAreaHeal()
    {
        SupportAbility heal = SupportClassPresets.Create(SupportClass.Bard).Single(a => a.Role == SupportRole.Heal);

        heal.Action.Should().Be(SupportActionType.UseItemAndGumpButton);
        heal.Target.Should().Be(SupportTargetMode.None);
        heal.GumpButton.Should().Be(7);

        // No item until the player picks their own songbook.
        heal.ItemSerial.Should().Be(0u);
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
}
