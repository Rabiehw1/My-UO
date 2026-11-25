using System.Text.Json;
using ClassicUO.Game;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers;

public class OrganizerListTests
{
    [Fact]
    public void LegacyOrganizerExportCarriesAmountsAndDestinationsIntoSharedItems()
    {
        const string json = """
            {"Name":"Restock","ItemConfigs":[
                {"Graphic":3821,"Amount":50,"DestContSerial":1073741840},
                {"Graphic":3903,"Enabled":false}
            ]}
            """;
        var config = JsonSerializer.Deserialize(json, OrganizerAgentContext.Default.OrganizerConfig);

        var item = Assert.Single(config.BuildMigratedItems(null));

        Assert.Equal(0x0EED, item.Graphic);
        Assert.Equal(ushort.MaxValue, item.Hue);
        Assert.Equal(50, item.MaxAmount);
        Assert.Equal(0x40000010u, item.DestinationContainer);
    }

    [Fact]
    public void LinkedOrganizerMigrationKeepsSharedMembershipAndIsolatesOverrides()
    {
        var gold = new SharedItem(0x0EED, 0, "Gold") { RegexSearch = "reward", MaxAmount = 5 };
        var arrows = new SharedItem(0x0F3F, 0, "Arrows");
        var bandages = new SharedItem(0x0E21, 0, "Bandages");
        var main = new SharedItemList { Items = [gold, arrows, bandages] };
        var config = new OrganizerConfig
        {
            SharedItemListId = main.Id,
            LegacyItemConfigs =
            [
                new() { Graphic = gold.Graphic, Hue = gold.Hue, Amount = 25, DestContSerial = 0x40000010 },
                new() { Graphic = arrows.Graphic, Hue = arrows.Hue, Enabled = false },
                new() { Graphic = 0x0F7A }
            ]
        };

        Assert.Equal(
            new[] { gold with { MaxAmount = 25, DestinationContainer = 0x40000010 }, bandages },
            config.BuildMigratedItems(main));
        Assert.Equal(new[] { gold, arrows, bandages }, main.Items);
    }

    [Fact]
    public void OrganizerSelectsTheFirstMatchingSharedRuleByGraphicHueAndProperties()
    {
        Client.UnitTestingActive = true;
        var world = new World();
        var item = new Item(world) { Graphic = 0x0EED, Hue = 3, OPLName = "Gold", OPLData = "\nQuest reward" };
        var exact = new SharedItem(item.Graphic, item.Hue, "Quest gold") { RegexSearch = "^Quest reward$" };
        var fallback = new SharedItem(item.Graphic, ushort.MaxValue, "All gold");
        var list = new SharedItemList { Items = [exact, fallback] };

        Assert.Same(exact, list.Find(item));
        item.OPLData = "\nTreasure";
        Assert.Same(fallback, list.Find(item));
        item.Graphic = 0x0F3F;
        Assert.Null(list.Find(item));
        world.Clear();
    }
}
