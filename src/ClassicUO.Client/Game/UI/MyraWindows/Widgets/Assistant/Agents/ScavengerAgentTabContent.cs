#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.ItemLists;
using ClassicUO.Game.UI.MyraWindows.Widgets.ArtTexture;
using ClassicUO.Utility;
using Myra.Graphics2D;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;

public static class ScavengerAgentTabContent
{
    public static Widget Build() => Build(AgentPageInteraction.InGame);

    internal static Widget Build(AgentPageInteraction interaction)
    {
        Profile? profile = ProfileManager.CurrentProfile;

        var root = new VerticalStackPanel { Spacing = 6 };

        // Enable Scavenger + Set Grab Bag
        var topRow = new HorizontalStackPanel { Spacing = 8 };
        topRow.Widgets.Add(MyraCheckButton.CreateWithCallback(
            profile.EnableScavenger,
            b => profile.EnableScavenger = b,
            TazLang.Get("scavenger_enable", "Enable Scavenger"),
            TazLang.Get("scavenger_enable_tooltip", "Scavenger automatically picks up items on the ground based on configured criteria.")));
        topRow.Widgets.Add(new MyraButton(TazLang.Get("scavenger_setgrabbag", "Set Grab Bag"), () =>
        {
            GameActions.Print(Client.Game.UO.World, TazLang.Get("scavenger_grabbag_prompt", "Target container to grab items into"));
            interaction.SelectGrabBag();
        }) { Tooltip = TazLang.Get("scavenger_setgrabbag_tooltip", "Choose a container to grab items into") });
        root.Widgets.Add(topRow);

        root.Widgets.Add(MyraCheckButton.CreateWithCallback(
            profile.ScavengerSkipLockedDown,
            b => profile.ScavengerSkipLockedDown = b,
            TazLang.Get("scavenger_skiplockeddown", "Skip locked-down items"),
            TazLang.Get("scavenger_skiplockeddown_tooltip",
                "Ignore items whose tooltip says they are locked down or secured, such as house decorations.")));

        root.Widgets.Add(SharedItemListPicker.BuildGathering(interaction, profile.ScavengerSharedItemListId,
            id => profile.ScavengerSharedItemListId = id));
        return root;
    }

}
