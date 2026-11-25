#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.MyraWindows.Theme;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;
using Myra.Graphics2D.Brushes;
using Myra.Graphics2D.UI;
using Myra.Graphics2D.UI.WrapPanel;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.ItemLists;

internal static class SharedItemListPicker
{
    internal static Widget BuildGathering(AgentPageInteraction interaction, Guid? selectedId, Action<Guid?> select,
        (Guid? Id, Action<Guid?> Select)? ignore = null)
    {
        var library = ItemListLibrary.Instance;
        var layout = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalSpacing = 8, VerticalSpacing = 4 };
        AddSelector("Main list", selectedId, select);
        if (ignore is { } exclusion)
            AddSelector("Ignore list", exclusion.Id, exclusion.Select);

        layout.Widgets.Add(new MyraButton("Manage Lists", interaction.OpenItemLists));
        return ItemListsTabContent.Section(interaction, "Lists", layout);

        void AddSelector(string label, Guid? id, Action<Guid?> change)
        {
            var row = new HorizontalStackPanel { Spacing = 4 };
            row.Widgets.Add(new MyraLabel(label, MyraLabel.TextStyle.P));
            row.Widgets.Add(CreateSelector(library, id, "None", interaction.IsDesktop, change).Picker);
            layout.Widgets.Add(row);
        }
    }

    public static Widget Build(AgentPageInteraction interaction, string agentName, Guid? selectedId, Action<Guid?> select,
        Func<IEnumerable<SharedItem>> localItems, Func<SharedItem, bool> createLocalRule, Action refreshLocal, Widget? defaults = null,
        (Guid? SelectedId, Action<Guid?> Select)? ignoreList = null, string? includedAction = null)
    {
        var root = new VerticalStackPanel { Spacing = 4 };
        var settings = new VerticalStackPanel { Spacing = 4 };
        var row = new HorizontalStackPanel { Spacing = 4 };
        var actions = new HorizontalStackPanel { Spacing = 4 };
        var help = new MyraLabel("", MyraLabel.TextStyle.P);
        var status = new MyraLabel("", MyraLabel.TextStyle.P) { Visible = false };
        var library = ItemListLibrary.Instance;
        var preview = interaction.IsDesktop && includedAction != null ? new VerticalStackPanel { Spacing = 3 } : null;
        Guid? ignoredId = ignoreList?.SelectedId;
        Action? refreshIgnoreChoices = null;

        row.Widgets.Add(new MyraLabel(interaction.IsDesktop && ignoreList.HasValue
            ? TazLang.Get("assistant_desktop_main_list", "Main list") : "Item list", MyraLabel.TextStyle.P));
        var manage = new MyraButton("Manage Item Lists");
        manage.Click += (_, _) => interaction.OpenItemLists();

        var copy = new MyraButton("Copy to local rules", () =>
        {
            if (selectedId is not Guid id || library.Find(id) is not { } list)
                return;

            int count = (list.Items ?? []).Count(item => item != null && createLocalRule(item));
            if (count > 0)
                refreshLocal();
            Report(count > 0 ? $"Added {count} local rules. You can now edit their agent settings." :
                "Local rules already exist for these items.");
        });
        copy.Tooltip = "Creates settings for shared items without changing existing local rules.";
        actions.Widgets.Add(copy);

        var (picker, refreshMainChoices) = CreateSelector(library, selectedId, "Local entries", interaction.IsDesktop, id =>
        {
            selectedId = id;
            select(id);
            status.Visible = false;
            UpdateAppearance();
        });

        actions.Widgets.Add(new MyraButton("Save local items as a list", () =>
        {
            SharedItem[] items = localItems().Where(item => item.Graphic != 0).ToArray();
            if (items.Length == 0)
            {
                Report("There are no local items with a graphic ID to save.");
                return;
            }

            SharedItemList list = library.Create(agentName, items);
            refreshMainChoices();
            refreshIgnoreChoices?.Invoke();
            UpdateAppearance();
            Report($"Saved {list.Items.Count} item types as {list.Name}.");
        }));

        row.Widgets.Add(picker);
        Container selectors = interaction.IsDesktop
            ? new WrapPanel { Orientation = Orientation.Horizontal, HorizontalSpacing = 8, VerticalSpacing = 3, UniformSizing = false }
            : new VerticalStackPanel { Spacing = 4 };
        selectors.Widgets.Add(row);
        if (!interaction.IsDesktop)
            row.Widgets.Add(manage);

        if (ignoreList is { } ignore)
        {
            var (ignorePicker, refreshChoices) = CreateSelector(library, ignore.SelectedId, "None", interaction.IsDesktop, id =>
            {
                ignoredId = id;
                ignore.Select(id);
                UpdateAppearance();
            });
            refreshIgnoreChoices = refreshChoices;
            var ignoreRow = new HorizontalStackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            const string tooltip = "Skip matching items even if they appear on the main list.";
            ignoreRow.Widgets.Add(new MyraLabel("Ignore list", MyraLabel.TextStyle.P) { Tooltip = tooltip });
            ignorePicker.Tooltip = tooltip;
            ignoreRow.Widgets.Add(ignorePicker);
            selectors.Widgets.Add(ignoreRow);
        }
        if (interaction.IsDesktop)
            selectors.Widgets.Add(manage);

        settings.Widgets.Add(selectors);
        settings.Widgets.Add(actions);
        settings.Widgets.Add(help);
        if (defaults != null)
            settings.Widgets.Add(defaults);
        settings.Widgets.Add(status);
        root.Widgets.Add(interaction.IsDesktop ? DesktopAssistantTheme.Group(ItemListsTabContent.Title, settings) : settings);
        if (preview != null)
            root.Widgets.Add(preview);

        UpdateAppearance();
        return root;

        void UpdateAppearance()
        {
            var list = selectedId.HasValue ? library.Find(selectedId.Value) : null;
            copy.Enabled = list?.Items is { Count: > 0 };
            if (defaults != null)
                defaults.Visible = list != null;
            help.Visible = selectedId.HasValue;
            help.Text = list == null
                ? "Select an item list to resume using shared entries."
                : "Local rules with the same graphic ID and hue supply this agent’s item settings.";

            if (preview != null)
            {
                preview.Widgets.Clear();
                if (list?.Items is { Count: > 0 } items)
                    BuildPreview(preview, items, ItemListLibrary.Resolve(ignoredId), includedAction!);
            }
        }

        void Report(string text)
        {
            status.Text = text;
            status.Visible = true;
        }
    }

    private static void BuildPreview(VerticalStackPanel preview, List<SharedItem> items, SharedItemList? ignore, string includedAction)
    {
        var excluded = (ignore?.Items ?? []).Where(item => item != null).Select(item => (item.Graphic, item.Hue)).ToHashSet();
        var excludedGraphics = excluded.Select(item => item.Graphic).ToHashSet();
        var grid = new MyraGrid { Background = DesktopAssistantTheme.Divider, RowSpacing = 1, ColumnSpacing = 0 };
        grid.AddColumn(new Proportion(ProportionType.Part, 2));
        grid.AddColumn(new Proportion(ProportionType.Part, 1), count: 3);

        AddRow(0, DesktopAssistantTheme.Sidebar, false,
            TazLang.Get("assistant_desktop_item", "Item"), TazLang.Get("assistant_desktop_graphic", "Graphic"),
            TazLang.Get("assistant_desktop_hue", "Hue"), TazLang.Get("assistant_desktop_action", "Action"));
        int row = 1;
        foreach (SharedItem item in items)
        {
            if (item == null)
                continue;

            bool skip = excluded.Contains((item.Graphic, item.Hue)) || excluded.Contains((item.Graphic, ushort.MaxValue));
            bool partial = !skip && item.Hue == ushort.MaxValue && excludedGraphics.Contains(item.Graphic);
            string action = skip ? TazLang.Get("assistant_desktop_skip", "Skip") :
                partial ? TazLang.Get("assistant_desktop_skip_hues", "Skip matching hues") : includedAction;
            AddRow(row++, skip ? DesktopAssistantTheme.Sidebar : DesktopAssistantTheme.Canvas, skip, item.Name,
                $"0x{item.Graphic:X4}", item.Hue == ushort.MaxValue ? TazLang.Get("assistant_desktop_any_hue", "Any") : $"0x{item.Hue:X4}", action);
        }

        preview.Widgets.Add(new MyraLabel(TazLang.Get("assistant_desktop_main_items", "Items from main list"), MyraLabel.TextStyle.P)
        { Font = DesktopAssistantTheme.Font(bold: true) });
        preview.Widgets.Add(new ScrollViewer { Content = grid, MaxHeight = 156 });

        void AddRow(int index, SolidBrush background, bool muted, params string[] labels)
        {
            grid.AddWidget(new Panel { Background = background }, index, 0, colspan: labels.Length);
            for (int col = 0; col < labels.Length; col++)
                grid.AddWidget(new MyraLabel(labels[col], MyraLabel.TextStyle.P)
                { TextColor = muted ? DesktopAssistantTheme.SecondaryText : DesktopAssistantTheme.Text }, index, col);
        }
    }

    private static (ComboView Picker, Action Refresh) CreateSelector(ItemListLibrary library, Guid? selectedId,
        string emptyLabel, bool desktop, Action<Guid?> select)
    {
        var picker = new ComboView { MinWidth = desktop ? 115 : 185, VerticalAlignment = VerticalAlignment.Center };
        var choices = new List<SharedItemList?>();
        bool refreshing = false;

        picker.ListView.SelectedIndexChanged += (_, _) =>
        {
            if (refreshing || picker.ListView.SelectedIndex is not int index || index < 0 || index >= choices.Count)
                return;

            // Preserve an unresolved link until the player explicitly picks a different list.
            if (index != 0 && choices[index] == null)
                return;

            selectedId = choices[index]?.Id;
            select(selectedId);
        };

        RefreshChoices();
        return (picker, RefreshChoices);

        void RefreshChoices()
        {
            refreshing = true;
            choices.Clear();
            picker.ListView.Widgets.Clear();
            choices.Add(null);
            picker.ListView.Widgets.Add(new Label { Text = emptyLabel });
            int index = 0;
            foreach (SharedItemList list in library.Lists ?? [])
            {
                if (list == null)
                    continue;

                if (list.Id == selectedId)
                    index = choices.Count;
                choices.Add(list);
                picker.ListView.Widgets.Add(new Label { Text = list.Name });
            }

            if (selectedId.HasValue && index == 0)
            {
                index = choices.Count;
                choices.Add(null);
                picker.ListView.Widgets.Add(new Label { Text = "Missing shared list" });
            }
            picker.ListView.SelectedIndex = index;
            refreshing = false;
        }
    }

    public static SharedItem? FromLocal(int graphic, ushort hue, string name = "") =>
        graphic is > 0 and <= ushort.MaxValue ? new SharedItem((ushort)graphic, hue, name) : null;
}
