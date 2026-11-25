#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.MyraWindows.Theme;
using ClassicUO.Game.UI.MyraWindows.Widgets.ArtTexture;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;
using ClassicUO.Utility;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.ItemLists;

internal static class ItemListsTabContent
{
    public static string Title => TazLang.Get("assistantwindow_tab_lists", "Lists");

    public static Widget Build(AgentPageInteraction interaction)
    {
        var tabs = new MyraTabControl();
        tabs.AddTab("Items", () => BuildItems(interaction), rebuildOnSelect: true);
        tabs.AddTab(TazLang.Get("assistant_general_tab_friends", "Friends"), () => FriendsListTabContent.Build(interaction), rebuildOnSelect: true);
        tabs.AddTab("Weapon Switch", () => WeaponSwitchListContent.Build(interaction), rebuildOnSelect: true);
        tabs.SelectFirst();
        UpdateTabColors();
        tabs.SelectedIndexChanged += (_, _) =>
        {
            interaction.CancelDialog();
            interaction.CancelTarget();
            UpdateTabColors();
        };
        return interaction.IsDesktop ? tabs
            : new ScrollViewer { Content = tabs, MaxHeight = Math.Max(180, ScaleHelper.LogicalWindowHeight - 140) };

        void UpdateTabColors()
        {
            if (!interaction.IsDesktop)
                return;

            foreach (var item in tabs.Items)
            {
                bool selected = ReferenceEquals(item, tabs.SelectedItem);
                if (tabs.FindChild<Widget>(widget => ReferenceEquals(widget.Tag, item))?.FindChild<Label>() is { } caption)
                {
                    caption.TextColor = selected ? DesktopAssistantTheme.Accent : DesktopAssistantTheme.Text;
                    caption.Font = DesktopAssistantTheme.Font(bold: selected);
                }
            }
        }
    }

    private static Widget BuildItems(AgentPageInteraction interaction)
    {
        var library = ItemListLibrary.Instance;
        SharedItemList? selected = library.Lists?.FirstOrDefault();
        SharedItemList[] choices = [];
        bool refreshing = false;

        var root = new VerticalStackPanel { Spacing = 8 };
        var listPicker = new ListView { Height = 300 };
        var name = new MyraInputBox { Width = 180, HintText = "List name" };
        var status = new MyraLabel("", MyraLabel.TextStyle.P) { Visible = false };
        var details = new VerticalStackPanel { Spacing = 6 };
        var entries = new ListView { Height = 300 };
        SharedItem[] visibleItems = [];
        SharedItem? editing = null;
        var filter = new MyraInputBox { Width = 180, HintText = "Filter this list" };
        var names = new VerticalStackPanel { Spacing = 5 };
        var grid = new MyraGrid { ColumnSpacing = 12, MaxWidth = interaction.IsDesktop ? 1040 : 700 };
        grid.AddColumn(new Proportion(ProportionType.Pixels, interaction.IsDesktop ? 260 : 210));
        grid.AddColumn(new Proportion(ProportionType.Fill));
        var actions = new HorizontalStackPanel { Spacing = 4 };
        var addActions = new HorizontalStackPanel { Spacing = 4 };
        var manual = new HorizontalStackPanel { Spacing = 4, Visible = false };
        var search = new VerticalStackPanel { Spacing = 4, Visible = false };
        var anyHue = new MyraCheckButton("Match any hue when targeting");

        var rename = new MyraButton("Rename");
        var duplicate = new MyraButton("Copy");
        var delete = new MyraButton("Remove");
        var edit = new MyraButton("Edit");
        var remove = new MyraButton("Remove");
        names.Widgets.Add(new MyraLabel("Create, copy, or select a list to edit.", MyraLabel.TextStyle.P) { Wrap = true });
        names.Widgets.Add(listPicker);
        names.Widgets.Add(name);
        names.Widgets.Add(actions);
        actions.Widgets.Add(new MyraButton("New", () =>
        {
            Select(library.Create(name.Text ?? ""));
            Report($"Created {selected!.Name}.");
        }));
        rename.OnClick = () =>
        {
            if (selected == null || string.IsNullOrWhiteSpace(name.Text))
            {
                Report("Enter a list name to rename the selected list.");
                return;
            }

            selected.Name = library.AvailableName(name.Text, selected);
            library.SaveChanges();
            Select(selected);
        };
        duplicate.OnClick = () =>
        {
            if (selected != null)
                Select(library.Create(selected.Name, selected.Items));
        };
        actions.Widgets.Add(rename);
        actions.Widgets.Add(duplicate);
        actions.Widgets.Add(delete);

        delete.OnClick = () =>
        {
            if (selected is not { } list)
                return;

            interaction.Confirm(delete, "Remove list",
                "Agents linked to this list will stop using its items.", () =>
                {
                    if (library.Lists?.Remove(list) == true)
                    {
                        library.SaveChanges();
                        Select(library.Lists.FirstOrDefault());
                    }
                });
        };

        addActions.Widgets.Add(new MyraButton("Add from target", () => interaction.SelectTarget(target =>
        {
            if (target is Item item)
                Add([FromItem(item)]);
            else
                Report("Select an item in the game.");
        })));
        addActions.Widgets.Add(new MyraButton("Add container contents", () => interaction.SelectTarget(target =>
        {
            if (target is not Item { ItemData.IsContainer: true } container)
            {
                Report("Select a container in the game.");
                return;
            }

            var items = new List<SharedItem>();
            for (var child = container.Items; child != null; child = child.Next)
                if (child is Item item)
                    items.Add(FromItem(item));

            if (items.Count == 0)
                Report("Open the container in the game to load its contents.");
            else
                Add(items);
        })) { Tooltip = "Adds the items directly inside an open container." });
        addActions.Widgets.Add(new MyraButton("Search by name", () => search.Visible = !search.Visible));


        var graphic = new MyraInputBox { Width = 110, HintText = "Graphic ID" };
        var hue = MyraInputBox.Hue(ushort.MaxValue, tooltip: "Enter a hue or -1 for any hue.");
        var itemName = new MyraInputBox { Width = 150, HintText = "Item name" };
        manual.Widgets.Add(graphic);
        manual.Widgets.Add(hue);
        manual.Widgets.Add(itemName);
        manual.Widgets.Add(new MyraButton("Save", () =>
        {
            if (!StringHelper.TryParseInt((graphic.Text ?? "").Trim(), out int id) || id is <= 0 or > ushort.MaxValue ||
                !MyraInputBox.TryParseHue((hue.Text ?? "").Trim(), out ushort itemHue))
            {
                Report("Enter a valid graphic ID and hue. Hex IDs can start with 0x.");
                return;
            }
            var item = new SharedItem((ushort)id, itemHue, (itemName.Text ?? "").Trim());
            if (editing != null && selected != null)
            {
                int itemIndex = selected.Items.IndexOf(editing);
                if (itemIndex < 0)
                    return;

                selected.Items.RemoveAt(itemIndex);
                if (selected.AddItems([item]) == 0)
                {
                    selected.Items.Insert(itemIndex, editing);
                    Report("That graphic and hue are already on the list.");
                    return;
                }

                library.SaveChanges();
                RefreshEntries();
                Report("Item updated.");
            }
            else
            {
                Add([item]);
            }
            manual.Visible = false;
        }));

        var query = new MyraInputBox { Width = 180, HintText = "Item name" };
        var results = new VerticalStackPanel { Spacing = 3 };
        var searchRow = new HorizontalStackPanel { Spacing = 4 };
        searchRow.Widgets.Add(query);
        searchRow.Widgets.Add(new MyraButton("Search", FindItems));
        search.Widgets.Add(searchRow);
        search.Widgets.Add(new ScrollViewer { Content = results, MaxHeight = 200 });

        SharedItem? SelectedItem() => entries.SelectedIndex is int index && index >= 0 && index < visibleItems.Length
            ? visibleItems[index] : null;
        var itemActions = new HorizontalStackPanel { Spacing = 4 };
        itemActions.Widgets.Add(new MyraButton("New", () => ShowEditor(null)));
        edit.OnClick = () => ShowEditor(SelectedItem());
        remove.OnClick = () =>
        {
            if (SelectedItem() is { } item && selected?.Items.Remove(item) == true)
            {
                library.SaveChanges();
                RefreshEntries();
            }
        };
        itemActions.Widgets.Add(edit);
        itemActions.Widgets.Add(remove);
        entries.SelectedIndexChanged += (_, _) => edit.Enabled = remove.Enabled = SelectedItem() != null;

        details.Widgets.Add(new MyraLabel("Add or edit items here. Choose how to use the list in each agent.", MyraLabel.TextStyle.P) { Wrap = true });
        details.Widgets.Add(filter);
        details.Widgets.Add(entries);
        details.Widgets.Add(itemActions);
        details.Widgets.Add(addActions);
        details.Widgets.Add(anyHue);
        details.Widgets.Add(manual);
        details.Widgets.Add(search);
        grid.AddWidget(Section(interaction, "Saved lists", names), 0, 0);
        grid.AddWidget(Section(interaction, "List contents", details), 0, 1);
        root.Widgets.Add(grid);
        root.Widgets.Add(status);

        listPicker.SelectedIndexChanged += (_, _) =>
        {
            if (!refreshing && listPicker.SelectedIndex is int index && index >= 0 && index < choices.Length)
                Select(choices[index], refreshPicker: false);
        };
        filter.TextChangedByUser += (_, _) => RefreshEntries();
        Select(selected);
        return root;

        void ShowEditor(SharedItem? item)
        {
            editing = item;
            graphic.Text = item == null ? "" : $"0x{item.Graphic:X4}";
            hue.Text = item == null || item.Hue == ushort.MaxValue ? "-1" : $"0x{item.Hue:X4}";
            itemName.Text = item?.Name ?? "";
            manual.Visible = true;
        }

        void Select(SharedItemList? list, bool refreshPicker = true)
        {
            selected = list;
            if (refreshPicker)
            {
                refreshing = true;
                choices = (library.Lists ?? []).Where(itemList => itemList != null).ToArray();
                listPicker.Widgets.Clear();
                foreach (SharedItemList itemList in choices)
                    listPicker.Widgets.Add(new Label { Text = itemList.Name });
                int index = Array.IndexOf(choices, list);
                listPicker.SelectedIndex = index >= 0 ? index : null;
                listPicker.Enabled = choices.Length > 0;
                refreshing = false;
            }
            name.Text = "";
            filter.Text = "";
            status.Visible = false;
            manual.Visible = search.Visible = false;
            editing = null;
            rename.Enabled = duplicate.Enabled = delete.Enabled = details.Visible = list != null;
            if (list == null)
                Report("Create a list to start adding items.");
            RefreshEntries();
        }

        void RefreshEntries()
        {
            entries.SelectedIndex = null;
            entries.Widgets.Clear();
            string text = (filter.Text ?? "").Trim();
            visibleItems = (selected?.Items ?? []).Where(item => item != null &&
                (item.Name?.Contains(text, StringComparison.OrdinalIgnoreCase) == true
                || $"0x{item.Graphic:X4}".Contains(text, StringComparison.OrdinalIgnoreCase)
                || $"0x{item.Hue:X4}".Contains(text, StringComparison.OrdinalIgnoreCase))).ToArray();
            foreach (SharedItem item in visibleItems)
            {
                string displayHue = item.Hue == ushort.MaxValue ? "Any" : $"0x{item.Hue:X4}";
                entries.Widgets.Add(new Label { Text = $"0x{item.Graphic:X4}   {displayHue}   {item.Name}" });
            }
            edit.Enabled = remove.Enabled = false;
        }

        void FindItems()
        {
            results.Widgets.Clear();
            string text = (query.Text ?? "").Trim();
            var tiles = Client.Game.UO.FileManager?.TileData?.StaticData;
            if (text.Length == 0 || tiles == null)
                return;

            int count = 0;
            for (int i = 1; i < tiles.Length && i <= ushort.MaxValue && count < 40; i++)
            {
                string tileName = tiles[i].Name;
                if (string.IsNullOrEmpty(tileName) || !tileName.Contains(text, StringComparison.OrdinalIgnoreCase))
                    continue;
                var item = new SharedItem((ushort)i, ushort.MaxValue, tileName);
                results.Widgets.Add(ItemRow(item, "Add", () => Add([item])));
                count++;
            }

            if (count == 0)
                results.Widgets.Add(new MyraLabel("No items found.", MyraLabel.TextStyle.P));
        }

        SharedItem FromItem(Item item) =>
            new(item.Graphic, anyHue.IsChecked ? ushort.MaxValue : item.Hue, item.OPLName ?? item.Name ?? "");

        void Add(IEnumerable<SharedItem> items)
        {
            if (selected == null)
                return;

            int added = selected.AddItems(items);
            if (added > 0)
            {
                library.SaveChanges();
                RefreshEntries();
            }

            Report(added > 0 ? $"Added {added} item types to {selected.Name}." : "These item types are already on the list.");
        }

        void Report(string text)
        {
            status.Text = text;
            status.Visible = true;
        }
    }

    internal static Widget Section(AgentPageInteraction interaction, string title, params Widget[] children) =>
        interaction.IsDesktop
            ? ClassicUO.Game.UI.MyraWindows.Theme.DesktopAssistantTheme.Group(title, children)
            : new VisualContainer(new VisualContainerProps { LabelText = title }, children);

    private static Widget ItemRow(SharedItem item, string action, Action onClick)
    {
        var row = new HorizontalStackPanel { Spacing = 6 };
        if (Client.Game.UO.Arts != null)
            row.Widgets.Add(new MyraArtTexture(item.Graphic, item.Hue == ushort.MaxValue ? (ushort)0 : item.Hue, 28));
        string hue = item.Hue == ushort.MaxValue ? "any hue" : $"hue 0x{item.Hue:X4}";
        row.Widgets.Add(new MyraLabel($"{item.Name}  0x{item.Graphic:X4}, {hue}", MyraLabel.TextStyle.P));
        row.Widgets.Add(new MyraButton(action, onClick));
        return row;
    }
}
