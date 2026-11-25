using ClassicUO.Configuration;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Macros;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant;

internal static class HotkeysMacrosTabContent
{
    internal static string Title => TazLang.Get("assistantwindow_tab_hotkeys_macros", "Hotkeys & Macros");

    internal static Widget Build(AgentPageInteraction interaction)
    {
        var tabs = new MyraTabControl();
        tabs.AddTab(MacrosTabContent.Title, () => MacrosTabContent.Build(interaction), rebuildOnSelect: true);
        tabs.AddTab(HotkeysTabContent.Title, () => HotkeysTabContent.Build(interaction), rebuildOnSelect: true);
        tabs.SelectFirst();
        tabs.SelectedIndexChanged += (_, _) =>
        {
            interaction.CancelDialog();
            interaction.CancelKeyCapture();
            if (!interaction.IsDesktop)
                HotkeysTabContent.Cleanup();
        };
        return tabs;
    }
}
