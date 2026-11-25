#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ClassicUO.Game.UI.MyraWindows.Theme;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.ItemLists;
using Myra.Graphics2D;
using Myra.Graphics2D.UI;
using Myra.Graphics2D.UI.WrapPanel;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;

internal static class AgentPresetPanel
{
    internal static Widget Build<T>(
        AgentPageInteraction interaction, string title, IReadOnlyList<T> presets, T? initial,
        Func<T, string> name, Func<string, T?> create, Action<T, string> rename, Func<T, T?> copy,
        Func<T, bool> remove, Func<T?, Action<T?>, Widget> buildDetails, int minimumCount = 0,
        Func<T, string>? display = null) where T : class
    {
        T? selected = null;
        bool refreshing = false;
        var picker = new ListView { Height = 290, HorizontalAlignment = HorizontalAlignment.Stretch };
        var details = new VerticalStackPanel { Spacing = 6 };
        var toolbar = Actions();
        var edit = new MyraButton("Rename");
        var duplicate = new MyraButton("Copy");
        var delete = new MyraButton("Remove");
        var add = new MyraButton("New", () => Prompt(toolbar, "New preset", "", value =>
        {
            if (create(value) is { } preset)
                Refresh(preset);
        }));
        if (interaction.IsDesktop)
            DesktopAssistantTheme.ApplyActionButtonStyle(add, primary: true);

        edit.OnClick = () =>
        {
            if (selected is not { } preset)
                return;

            Prompt(toolbar, "Rename preset", name(preset), value =>
            {
                rename(preset, value);
                Refresh(preset);
            });
        };
        duplicate.OnClick = () =>
        {
            if (selected is { } preset && copy(preset) is { } copied)
                Refresh(copied);
        };
        delete.OnClick = () =>
        {
            if (selected is not { } preset)
                return;

            interaction.Confirm(toolbar, "Remove preset", $"Remove “{name(preset)}” from this agent?", () =>
            {
                if (remove(preset))
                    Refresh(null);
            });
        };
        MyraStyle.ApplyButtonDangerStyle(delete);
        toolbar.Widgets.Add(add);
        toolbar.Widgets.Add(edit);
        toolbar.Widgets.Add(duplicate);
        toolbar.Widgets.Add(delete);

        var layout = new MyraGrid { ColumnSpacing = 10, HorizontalAlignment = HorizontalAlignment.Stretch };
        layout.AddColumn(new Proportion(ProportionType.Pixels, interaction.IsDesktop ? 210 : 175));
        layout.AddColumn(new Proportion(ProportionType.Fill));
        layout.AddWidget(ItemListsTabContent.Section(interaction, title, picker, toolbar), 0, 0);
        layout.AddWidget(details, 0, 1);

        picker.SelectedIndexChanged += (_, _) =>
        {
            if (!refreshing && picker.SelectedIndex is int index && index < presets.Count)
            {
                interaction.CancelTarget();
                Select(presets[index]);
            }
        };
        Refresh(initial);
        return layout;

        void Refresh(T? preferred)
        {
            refreshing = true;
            picker.Widgets.Clear();
            foreach (T preset in presets)
                picker.Widgets.Add(new Label { Text = (display ?? name)(preset) });

            int index = preferred == null ? -1 : presets.ToList().IndexOf(preferred);
            if (index < 0 && presets.Count > 0)
                index = 0;

            picker.SelectedIndex = index >= 0 ? index : null;
            refreshing = false;
            Select(index >= 0 ? presets[index] : null);
        }

        void Select(T? preset)
        {
            selected = preset;
            edit.Enabled = duplicate.Enabled = preset != null;
            delete.Enabled = preset != null && presets.Count > minimumCount;
            details.Widgets.Clear();
            details.Widgets.Add(buildDetails(preset, Refresh));
        }

        void Prompt(Widget anchor, string caption, string value, Action<string> apply)
        {
            var input = new MyraInputBox { Text = value, HintText = "Preset name", Width = 240 };
            interaction.ShowDialog(anchor, caption, input, confirmed =>
            {
                if (confirmed && input.Text is { } entered)
                {
                    interaction.CancelTarget();
                    apply(entered.Trim());
                }
            }, () => !string.IsNullOrWhiteSpace(input.Text));
        }
    }

    internal static WrapPanel Actions() =>
        new() { Orientation = Orientation.Horizontal, HorizontalSpacing = 5, VerticalSpacing = 4 };
}
