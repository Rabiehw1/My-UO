using System;
using ClassicUO.Configuration;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Filters;

public static class FiltersTab
{
    internal static string Title => TazLang.Get("assistantwindow_tab_filters", "Filters");

    public static Widget Build()
    {
        var tabs = new MyraTabControl();
        tabs.AddTab(TazLang.Get("assistant_filter_tab_graphics", "Graphics"), GraphicReplacementTabContent.Build, rebuildOnSelect: true);
        tabs.AddTab(TazLang.Get("assistant_filter_tab_journal", "Journal Filter"), JournalFilterTabContent.Build, rebuildOnSelect: true);
        tabs.AddTab(TazLang.Get("assistant_filter_tab_sound", "Sound Filter"), SoundFilterTabContent.Build, rebuildOnSelect: true);
        tabs.AddTab(TazLang.Get("assistant_filter_tab_music", "Music Filter"), MusicFilterTabContent.Build, rebuildOnSelect: true);
        tabs.AddTab(TazLang.Get("assistant_filter_tab_season", "Season Filter"), SeasonFilterTabContent.Build, rebuildOnSelect: true);
        tabs.SelectFirst();
        return tabs;
    }

    internal static Widget Build(AgentPageInteraction interaction)
    {
        var tabs = new MyraTabControl();
        tabs.AddTab(TazLang.Get("assistant_desktop_filter_journal", "Journal"),
            () => JournalFilterTabContent.Build(interaction), rebuildOnSelect: true);
        tabs.AddTab(TazLang.Get("assistant_desktop_filter_audio", "Sound & Music"), () => BuildPair(
            TazLang.Get("assistant_desktop_filter_sound", "Sounds"), () => SoundFilterTabContent.Build(interaction, isMusic: false),
            TazLang.Get("assistant_desktop_filter_music", "Music"), () => SoundFilterTabContent.Build(interaction, isMusic: true)));
        tabs.AddTab(TazLang.Get("assistant_filter_tab_graphics", "Graphics"), () => BuildPair(
            TazLang.Get("assistant_desktop_filter_replacements", "Replacements"), () => GraphicReplacementTabContent.Build(interaction),
            TazLang.Get("assistant_desktop_filter_seasons", "Seasons"), () => SeasonFilterTabContent.Build(interaction)));
        tabs.SelectFirst();
        return tabs;
    }

    private static MyraTabControl BuildPair(string firstLabel, Func<Widget> first, string secondLabel, Func<Widget> second)
    {
        var tabs = new MyraTabControl();
        tabs.AddTab(firstLabel, first, rebuildOnSelect: true);
        tabs.AddTab(secondLabel, second, rebuildOnSelect: true);
        tabs.SelectFirst();
        return tabs;
    }
}
