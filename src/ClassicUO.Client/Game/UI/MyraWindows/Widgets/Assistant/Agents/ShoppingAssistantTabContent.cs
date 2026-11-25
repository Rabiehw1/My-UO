#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.MyraWindows.Theme;
using ClassicUO.Game.UI.MyraWindows.Widgets.ArtTexture;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.ItemLists;
using ClassicUO.Utility;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;

internal static class ShoppingAssistantTabContent
{
    internal static string Title => TazLang.Get("assistant_agent_tab_shopping");

    internal static Widget Build(AgentPageInteraction interaction)
    {
        if (ProfileManager.CurrentProfile is not { } profile)
            return new MyraLabel(TazLang.Get("shopping_profilenotloaded"), MyraLabel.TextStyle.P);

        var tabs = new MyraTabControl();
        if (interaction.IsDesktop)
        {
            tabs.HorizontalAlignment = HorizontalAlignment.Stretch;
            tabs.VerticalAlignment = VerticalAlignment.Stretch;
        }

        tabs.AddTab(TazLang.Get("shopping_tab_buy"),
            () => ShopListsTabContent.Build(interaction, BuildSettings(profile, interaction, buying: true), buying: true), rebuildOnSelect: true);
        tabs.AddTab(TazLang.Get("shopping_tab_sell"),
            () => ShopListsTabContent.Build(interaction, BuildSettings(profile, interaction, buying: false), buying: false), rebuildOnSelect: true);
        tabs.SelectFirst();
        return tabs;
    }

    private static Widget BuildSettings(Profile profile, AgentPageInteraction interaction, bool buying)
    {
        var enabled = MyraCheckButton.CreateWithCallback(buying ? profile.BuyAgentEnabled : profile.SellAgentEnabled, value =>
        {
            if (buying)
                profile.BuyAgentEnabled = value;
            else
                profile.SellAgentEnabled = value;
        }, TazLang.Get(buying ? "autobuy_enable" : "autosell_enable"));
        var help = new MyraLabel(TazLang.Get(buying ? "shopping_buy_help" : "shopping_sell_help"), MyraLabel.TextStyle.P)
        {
            Wrap = true
        };
        var total = LabeledHorizontalSlider.CreateSliderWithCallback(0, 1000, buying ? profile.BuyAgentMaxItems : profile.SellAgentMaxItems, value =>
        {
            if (buying)
                profile.BuyAgentMaxItems = (int)value;
            else
                profile.SellAgentMaxItems = (int)value;
        });
        var uniques = LabeledHorizontalSlider.CreateSliderWithCallback(0, 100, buying ? profile.BuyAgentMaxUniques : profile.SellAgentMaxUniques, value =>
        {
            if (buying)
                profile.BuyAgentMaxUniques = (int)value;
            else
                profile.SellAgentMaxUniques = (int)value;
        });
        total.Tooltip = uniques.Tooltip = TazLang.Get("shopping_limit_tooltip");
        var subcontainers = MyraCheckButton.CreateWithCallback(profile.BuyAgentSubContainers, value => profile.BuyAgentSubContainers = value,
            TazLang.Get("shopping_subcontainers"), TazLang.Get("shopping_subcontainers_tooltip"));
        return ItemListsTabContent.Section(interaction, TazLang.Get("shopping_settings"), enabled, help,
            DesktopAssistantForm.Fields(
                new(TazLang.Get("shopping_max_items"), total), new(TazLang.Get("shopping_max_types"), uniques)),
            subcontainers);
    }
}
