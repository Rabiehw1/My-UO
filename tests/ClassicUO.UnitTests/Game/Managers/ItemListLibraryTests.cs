using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers;

public class ItemListLibraryTests
{
    [Fact]
    public void AddingStacksDeduplicatesGraphicAndHue()
    {
        var list = new SharedItemList();

        Assert.Equal(2, list.AddItems([
            new SharedItem(0x0EED, 0, "Gold"),
            new SharedItem(0x0EED, 0, "Gold pile"),
            new SharedItem(0x0EED, 32, "Colored gold")
        ]));
        Assert.Equal("Gold", list.Find(0x0EED, 0)?.Name);
        Assert.Null(list.Find(0x0EED, 40));
    }

    [Fact]
    public void AnyHueCoversTheGraphicWithOneRule()
    {
        var list = new SharedItemList();
        list.AddItems([new SharedItem(0x0EED, 0, "Gold"), new SharedItem(0x0EED, 32, "Colored gold")]);

        var anyHue = new SharedItem(0x0EED, ushort.MaxValue, "Any gold");
        Assert.Equal(1, list.AddItems([anyHue]));
        Assert.Equal(0, list.AddItems([new SharedItem(0x0EED, 50, "More gold")]));
        Assert.Equal(anyHue, Assert.Single(list.Items));
        Assert.Equal(anyHue, list.Find(0x0EED, 50));
    }

    [Fact]
    public void SharedMembershipAndLocalVendorOverridesAreIndependent()
    {
        var list = new SharedItemList();
        var localRule = new BuySellItemConfig { Graphic = 0x0EED, Hue = 0, MaxAmount = 12, MaxPrice = 3, Enabled = false };
        var unrelatedRule = new BuySellItemConfig { Graphic = 0x0E21, Hue = 0, MaxAmount = 10 };
        var gold = new SharedItem(0x0EED, 0, "Gold");
        var otherItem = new SharedItem(0x0F7A, ushort.MaxValue, "Black pearl");
        list.AddItems([gold, otherItem]);

        var rules = list.ResolveRules([localRule, unrelatedRule], static rule => (rule.Graphic, rule.Hue),
            static item => new BuySellItemConfig { Graphic = item.Graphic, Hue = item.Hue, MaxAmount = 1 });

        BuySellItemConfig[] first = rules.ToArray();
        Assert.Equal(2, first.Length);
        Assert.Same(localRule, first[0]);
        Assert.False(first[0].Enabled);
        Assert.Equal((ushort)1, first[1].MaxAmount);

        list.Items.Remove(gold);
        Assert.Equal(otherItem.Graphic, Assert.Single(rules).Graphic);
    }

    [Fact]
    public void LibraryRoundTripPreservesListIdentityAndItems()
    {
        var list = new SharedItemList { Name = "Reagents" };
        list.AddItems([new SharedItem(0x0F7A, ushort.MaxValue, "Black pearl")]);
        var library = new ItemListLibrary { Lists = [list] };

        var context = ItemListJsonContext.Default.ItemListLibrary;
        string json = JsonSerializer.Serialize(library, context);
        var loaded = JsonSerializer.Deserialize(json, context);

        SharedItemList reloaded = Assert.Single(loaded.Lists);
        Assert.Equal(list.Id, reloaded.Id);
        Assert.Equal(list.Name, reloaded.Name);
        Assert.Equal(Assert.Single(list.Items), Assert.Single(reloaded.Items));
    }

    [Fact]
    public void GatheringRulesPreserveOrderedRegexVariantsAndSettings()
    {
        var list = new SharedItemList();
        var broad = new SharedItem(0x0EED, ushort.MaxValue, "Gold");
        var specific = GatheringListRules.From(new AutoLootManager.AutoLootConfigEntry
        {
            Graphic = 0x0EED, Hue = 0, Name = "Gold coins", RegexSearch = "reward",
            Priority = AutoLootManager.AutoLootPriority.High, MaxAmount = 300,
            DestinationContainer = 0x40001234
        });

        Assert.Equal(2, list.AddItems([specific, broad], preserveRules: true));
        Assert.Equal(0, list.AddItems([specific], preserveRules: true));

        var type = ItemListJsonContext.Default.ListSharedItem;
        var imported = JsonSerializer.Deserialize(JsonSerializer.Serialize(list.Items, type), type);
        Assert.Equal(new[] { specific, broad }, imported);
    }

    [Fact]
    public void AgentSettingsKeepMainAndIgnoreListLinksSeparate()
    {
        var main = new SharedItemList { Name = "Supplies" };
        var ignore = new SharedItemList { Name = "Arrows" };
        var profile = new Profile { AutoLootSharedItemListId = main.Id, AutoLootIgnoreItemListId = ignore.Id };
        var organizer = new OrganizerConfig { SharedItemListId = main.Id, IgnoreItemListId = ignore.Id };

        var profileType = ProfileJsonContext.DefaultToUse.Profile;
        var organizerType = OrganizerAgentContext.Default.OrganizerConfig;
        JsonObject profileJson = JsonSerializer.SerializeToNode(profile, profileType).AsObject();
        profileJson.Remove("options_font"); // Loading the font initializes the game UI.
        var reloadedProfile = profileJson.Deserialize(profileType);
        var reloadedOrganizer = JsonSerializer.Deserialize(JsonSerializer.Serialize(organizer, organizerType), organizerType);

        Assert.Equal(main.Id, reloadedProfile.AutoLootSharedItemListId);
        Assert.Equal(ignore.Id, reloadedProfile.AutoLootIgnoreItemListId);
        Assert.Equal(main.Id, reloadedOrganizer.SharedItemListId);
        Assert.Equal(ignore.Id, reloadedOrganizer.IgnoreItemListId);
        Assert.Null(JsonSerializer.Deserialize("{}", profileType).AutoLootIgnoreItemListId);
        Assert.Null(JsonSerializer.Deserialize("{}", organizerType).IgnoreItemListId);
    }
}
