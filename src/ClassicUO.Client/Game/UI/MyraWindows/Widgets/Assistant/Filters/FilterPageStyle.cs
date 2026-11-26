#nullable enable
using System;
using ClassicUO.Game.UI.MyraWindows.Theme;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Filters;

internal static class FilterPageStyle
{
    internal static MyraButton Button(
        AgentPageInteraction interaction, string text, Action? click, bool primary, bool destructive, string? tooltip)
    {
        MyraButton button;
        if (interaction.IsDesktop)
            button = DesktopAssistantForm.Button(text, click, primary, destructive);
        else
        {
            button = new MyraButton(text, click);
            if (destructive)
                MyraStyle.ApplyButtonDangerStyle(button);
        }

        button.Tooltip = tooltip;
        return button;
    }

    internal static Widget Group(AgentPageInteraction interaction, string title, params Widget[] children)
    {
        if (interaction.IsDesktop)
            return DesktopAssistantTheme.Group(title, children);

        var panel = new VerticalStackPanel { Spacing = 4 };
        panel.Widgets.Add(new MyraLabel(title, MyraLabel.TextStyle.H3));
        foreach (Widget child in children)
            panel.Widgets.Add(child);
        return panel;
    }
}
