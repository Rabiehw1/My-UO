#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ClassicUO.Configuration;
using ClassicUO.Game.UI.MyraWindows.Theme;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.ItemLists;
using Microsoft.Xna.Framework;
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
        Func<T, bool>? isRunning = null, Widget? desktopOptions = null, string? runningText = null) where T : class
    {
        bool fullHeight = interaction.IsDesktop && desktopOptions != null;
        T? selected = null;
        bool refreshing = false;
        var picker = new ListView
        {
            Height = fullHeight ? null : 290,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        var details = new VerticalStackPanel { Spacing = 6 };
        var edit = new MyraButton(TazLang.Get("assistant_dialog_rename"));
        var duplicate = new MyraButton(TazLang.Get("agent_preset_copy"));
        var delete = new MyraButton(TazLang.Get("remove"));
        var add = new MyraButton(TazLang.Get("agent_preset_new"));
        add.OnClick = () => Prompt(add, "New preset", "", TazLang.Get("add"), value =>
        {
            if (create(value) is { } preset)
                Refresh(preset);
        });
        if (interaction.IsDesktop)
            DesktopAssistantTheme.ApplyActionButtonStyle(add, primary: true);

        edit.OnClick = () =>
        {
            if (selected is not { } preset)
                return;

            Prompt(edit, "Rename preset", name(preset), TazLang.Get("assistant_dialog_rename"), value =>
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

            interaction.Confirm(delete, "Remove preset", $"Remove “{name(preset)}” from this agent?", () =>
            {
                if (remove(preset))
                    Refresh(null);
            }, confirmText: TazLang.Get("remove"), destructive: true);
        };
        MyraStyle.ApplyButtonDangerStyle(delete);

        int sidebarWidth = fullHeight ? 238 : interaction.IsDesktop ? 210 : 175;
        MyraGrid layout = interaction.IsDesktop ? new DesktopAssistantForm.FixedSidebarLayout(details, sidebarWidth) : new MyraGrid();
        layout.ColumnSpacing = 10;
        layout.AddColumn(new Proportion(ProportionType.Pixels, sidebarWidth));
        layout.AddColumn(new Proportion(ProportionType.Fill));
        if (fullHeight)
        {
            var buttons = new MyraGrid { ColumnSpacing = 5, RowSpacing = 4 };
            buttons.AddColumn(new Proportion(ProportionType.Part, 1), count: 2);
            MyraButton[] actions = [add, edit, duplicate, delete];
            for (int index = 0; index < actions.Length; index++)
            {
                actions[index].HorizontalAlignment = HorizontalAlignment.Stretch;
                buttons.AddWidget(actions[index], index / 2, index % 2);
            }

            var sidebar = new MyraGrid
            {
                Background = DesktopAssistantTheme.Sidebar, Border = DesktopAssistantTheme.Divider,
                BorderThickness = new Thickness(1), Padding = new Thickness(7), RowSpacing = 5
            };
            sidebar.AddRow();
            sidebar.AddRow(new Proportion(ProportionType.Fill));
            sidebar.AddRow();
            sidebar.AddWidget(new MyraLabel(title, MyraLabel.TextStyle.H3), 0, 0);
            sidebar.AddWidget(picker, 1, 0);
            sidebar.AddWidget(buttons, 2, 0);

            var settings = new VerticalStackPanel { Spacing = 9, Padding = new Thickness(0, 0, 5, 0) };
            settings.Widgets.Add(details);
            settings.Widgets.Add(desktopOptions!);
            layout.AddRow(new Proportion(ProportionType.Fill));
            layout.AddWidget(sidebar, 0, 0);
            layout.AddWidget(new ScrollViewer { Content = settings, ShowHorizontalScrollBar = false }, 0, 1);
        }
        else
        {
            var toolbar = Actions();
            toolbar.Widgets.Add(add);
            toolbar.Widgets.Add(edit);
            toolbar.Widgets.Add(duplicate);
            toolbar.Widgets.Add(delete);
            layout.AddWidget(ItemListsTabContent.Section(interaction, title, picker, toolbar), 0, 0);
            layout.AddWidget(details, 0, 1);
        }

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
            {
                bool running = isRunning?.Invoke(preset) == true;
                if (fullHeight)
                {
                    var row = new MyraGrid { MinHeight = 24, ColumnSpacing = 4 };
                    row.AddColumn(new Proportion(ProportionType.Fill));
                    row.AddColumn();
                    var label = MyraGrid.TableText(name(preset));
                    label.VerticalAlignment = VerticalAlignment.Center;
                    row.AddWidget(label, 0, 0);
                    if (running)
                        row.AddWidget(new MyraLabel(runningText ?? TazLang.Get("agent_preset_running"), MyraLabel.TextStyle.P)
                        {
                            TextColor = new Color(43, 108, 73), VerticalAlignment = VerticalAlignment.Center
                        }, 0, 1);
                    picker.Widgets.Add(row);
                }
                else
                {
                    string caption = !running ? name(preset)
                        : runningText != null ? $"{name(preset)} ({runningText})"
                        : TazLang.Get("agent_preset_running_name", [name(preset)]);
                    picker.Widgets.Add(new Label { Text = caption });
                }
            }

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

        void Prompt(Widget anchor, string caption, string value, string action, Action<string> apply)
        {
            var input = new MyraInputBox { Text = value, HintText = "Preset name", MinWidth = 220 };
            var body = new VerticalStackPanel { Spacing = 4 };
            var error = new MyraLabel(TazLang.Get("assistant_dialog_preset_name_required"), MyraLabel.TextStyle.P) { Visible = false };
            input.TextChangedByUser += (_, _) => error.Visible = false;
            body.Widgets.Add(input);
            body.Widgets.Add(error);
            interaction.ShowDialog(anchor, caption, body, confirmed =>
            {
                if (confirmed && input.Text is { } entered)
                {
                    interaction.CancelTarget();
                    apply(entered.Trim());
                }
            }, () =>
            {
                error.Visible = string.IsNullOrWhiteSpace(input.Text);
                return !error.Visible;
            }, confirmText: action);
        }
    }

    internal static WrapPanel Actions() =>
        new() { Orientation = Orientation.Horizontal, HorizontalSpacing = 5, VerticalSpacing = 4 };
}
