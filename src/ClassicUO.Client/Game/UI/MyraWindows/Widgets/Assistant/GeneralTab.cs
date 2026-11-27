using System;
using ClassicUO.Configuration;
using ClassicUO.Game.UI.MyraWindows.Options.Tabs;
using ClassicUO.Game.UI.MyraWindows.Theme;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Filters;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Launcher;
using Myra.Graphics2D;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant;

public static class GeneralTab
{
    // Launcher comes first in the list, but General opens on Options, which players use most in game.
    private const int OPTIONS_SECTION = 1;

    internal static string Title => TazLang.Get("assistantwindow_tab_general", "General");

    public static Widget Build()
    {
        var tabs = new MyraTabControl();
        tabs.AddTab(TazLang.Get("assistant_general_tab_options", "Options"), GeneralTabContent.Build);
        tabs.AddTab(TazLang.Get("assistant_general_tab_hud", "HUD"), HudTabContent.Build);
        tabs.AddTab(TazLang.Get("assistant_general_tab_titlebar", "Title Bar"), TitleBarTabContent.Build);
        tabs.AddTab(TazLang.Get("assistant_general_tab_spellindicators", "Spell Indicators"), SpellIndicatorTabContent.Build);
        tabs.AddTab(TazLang.Get("assistant_general_tab_friends", "Friends"), FriendsListTabContent.Build, rebuildOnSelect: true);
        tabs.AddTab(TazLang.Get("assistant_general_tab_pathfinding", "Pathfinding"), PathfindingTabContent.Build);
        tabs.AddTab(FiltersTab.Title, FiltersTab.Build, rebuildOnSelect: true);
        tabs.SelectFirst();
        return tabs;
    }

    internal static Widget Build(AgentPageInteraction interaction)
    {
        (string Label, Func<Widget> Build)[] sections =
        [
            (LauncherTabContent.Title, () => LauncherTabContent.Build(interaction)),
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
            (TazLang.Get("mog_misctab_label", "Miscellaneous"), () => MiscTab.GetContent().Render()),
            (FiltersTab.Title, () => FiltersTab.Build(interaction))
        ];

        int sidebarWidth = DesktopAssistantTheme.SidebarWidth - 14;
        var content = new Panel();
        var root = new DesktopAssistantForm.FixedSidebarLayout(content, sidebarWidth) { ColumnSpacing = 12 };
        root.AddColumn();
        root.AddColumn(new Proportion(ProportionType.Fill));
        var navigation = new VerticalStackPanel
        {
            Width = sidebarWidth,
            Background = DesktopAssistantTheme.Sidebar,
            VerticalAlignment = VerticalAlignment.Top,
            Padding = new Thickness(3),
            Spacing = 2
        };
        var buttons = new MyraButton[sections.Length];
        for (int i = 0; i < sections.Length; i++)
        {
            int index = i;
            var button = DesktopAssistantTheme.NavigationButton(sections[i].Label);
            button.OnClick = () => Select(index);
            buttons[i] = button;
            navigation.Widgets.Add(button);
        }

        root.AddWidget(navigation, 0, 0);
        root.AddWidget(content, 0, 1);
        Select(OPTIONS_SECTION);

        void Select(int index)
        {
            for (int i = 0; i < buttons.Length; i++)
                DesktopAssistantTheme.SelectNavigation(buttons[i], i == index);

            root.FitContent = index == OPTIONS_SECTION;
            content.Widgets.Clear();
            content.Widgets.Add(sections[index].Build());
        }
        return root;
    }
}
