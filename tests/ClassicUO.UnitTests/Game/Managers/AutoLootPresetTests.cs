using System;
using System.Text.Json;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers;

public class AutoLootPresetTests
{
    [Fact]
    public void MigratingTheProfileKeepsItsListsOnTheActivePresetAndDoesNotRepeat()
    {
        var profile = new Profile
        {
            AutoLootListsMigrated = true,
            AutoLootSharedItemListId = Guid.NewGuid(),
            AutoLootIgnoreItemListId = Guid.NewGuid(),
            GrabBagSerial = 0x40000001,
            AutoLootHumanCorpses = true
        };
        var gold = new AutoLootManager.AutoLootList { Name = "Gold" };
        var supplies = new AutoLootManager.AutoLootList { Name = "Supplies" };
        var saved = new AutoLootManager.AutoLootData { Lists = [gold, supplies], SelectedUid = supplies.Uid };

        Assert.True(saved.MigratePresets(profile, supplies));
        Assert.Equal(profile.AutoLootSharedItemListId, supplies.MainItemListId);
        Assert.Equal(profile.AutoLootIgnoreItemListId, supplies.IgnoreItemListId);
        Assert.True(supplies.LootHumanCorpses);
        Assert.Null(supplies.GrabBagSerial);
        Assert.Null(gold.MainItemListId);
        Assert.Null(gold.IgnoreItemListId);

        supplies.MainItemListId = null;
        supplies.GrabBagSerial = 0;
        gold.MainItemListId = Guid.NewGuid();
        gold.GrabBagSerial = 0x40000002;
        var context = AutoLootJsonContext.Default.AutoLootData;
        var reloaded = JsonSerializer.Deserialize(JsonSerializer.Serialize(saved, context), context);

        Assert.Equal(supplies.Uid, reloaded.SelectedUid);
        Assert.False(reloaded.MigratePresets(profile, reloaded.Lists[1]));
        Assert.Null(reloaded.Lists[1].MainItemListId);
        Assert.Equal(0u, reloaded.Lists[1].GrabBagSerial);
        Assert.Equal(gold.MainItemListId, reloaded.Lists[0].MainItemListId);
        Assert.Equal(0x40000002u, reloaded.Lists[0].GrabBagSerial);
    }
}
