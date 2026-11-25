using System;
using ClassicUO.Configuration;
using ClassicUO.Game.UI.MyraWindows.Options.Tabs;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant;

public static class GeneralTab
{
    internal static string Title => TazLang.Get("assistantwindow_tab_general", "General");

    public static Widget Build()
    {
        var tabs = new MyraTabControl();
        tabs.AddTab(TazLang.Get("assistant_general_tab_options", "Options"), GeneralTabContent.Build);
        tabs.AddTab(TazLang.Get("assistant_general_tab_hud", "HUD"), HudTabContent.Build);
        tabs.AddTab(TazLang.Get("assistant_general_tab_titlebar", "Title Bar"), TitleBarTabContent.Build);
        tabs.AddTab(TazLang.Get("assistant_general_tab_spellindicators", "Spell Indicators"), SpellIndicatorTabContent.Build);
        tabs.AddTab(TazLang.Get("assistant_general_tab_friends", "Friends"), FriendsListTabContent.Build);
        tabs.AddTab(TazLang.Get("assistant_general_tab_pathfinding", "Pathfinding"), PathfindingTabContent.Build);
        tabs.SelectFirst();
        return tabs;
    }

    internal static Widget Build(AgentPageInteraction interaction)
    {
        (string Label, Func<Widget> Build)[] sections =
        [
            (TazLang.Get("assistant_general_tab_options", "Options"), () => GeneralTabContent.Build(interaction)),
            (TazLang.Get("mog_gameplaytab_gameplaylabel", "Gameplay"), () => GameplayTab.GetContent().Render()),
            (TazLang.Get("mog_kw_interface", "Interface"), () => InterfaceTab.GetContent().Render()),
            (TazLang.Get("mog_videotab_label", "Display"), () => VideoTab.GetContent().Render()),
            (TazLang.Get("mog_soundtab_label", "Sound"), () => SoundsTab.GetContent(interaction.RunInGame).Render()),
            (TazLang.Get("mog_labelchatandtext", "Chat & Text"), () => ChatTab.GetContent().Render()),
            (TazLang.Get("assistant_general_tab_hud", "HUD"), HudTabContent.Build),
            (TazLang.Get("assistant_general_tab_titlebar", "Title Bar"), TitleBarTabContent.Build),
            (TazLang.Get("assistant_general_tab_spellindicators", "Spell Indicators"), SpellIndicatorTabContent.Build),
            (TazLang.Get("assistant_general_tab_friends", "Friends"), () => FriendsListTabContent.Build(interaction)),
            (TazLang.Get("assistant_general_tab_pathfinding", "Pathfinding"), PathfindingTabContent.Build),
            (TazLang.Get("mog_misctab_label", "Miscellaneous"), () => MiscTab.GetContent().Render())
        ];

        var root = new VerticalStackPanel { Spacing = 8 };
        var picker = new ComboView { MinWidth = 200 };
        foreach (var section in sections)
            picker.Widgets.Add(new Label { Text = section.Label });

        var content = new Panel();
        picker.SelectedIndexChanged += (_, _) =>
        {
            if (picker.SelectedIndex is int index)
            {
                content.Widgets.Clear();
                content.Widgets.Add(sections[index].Build());
            }
        };
        root.Widgets.Add(new MyraLabel(TazLang.Get("assistant_desktop_section", "Section"), MyraLabel.TextStyle.P).PlaceBefore(picker));
        root.Widgets.Add(content);
        picker.SelectedIndex = 0;
        return root;
    }
}
