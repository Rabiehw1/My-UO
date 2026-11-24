#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.MyraWindows.Widgets.ArtTexture;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;
using ClassicUO.Utility;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.ItemLists;

internal static class ItemListsTabContent
{
    public static string Title => TazLang.Get("assistantwindow_tab_itemlists", "Item Lists");

    public static Widget Build(AgentPageInteraction interaction)
    {
        var library = ItemListLibrary.Instance;
        SharedItemList? selected = library.Lists?.FirstOrDefault();
        SharedItemList[] choices = [];
        bool refreshing = false;

        var root = new VerticalStackPanel { Spacing = 8 };
        var listPicker = new ComboView { MinWidth = 180 };
        var name = new MyraInputBox { Width = 180, HintText = "List name" };
        var status = new MyraLabel("", MyraLabel.TextStyle.P) { Visible = false };
        var details = new VerticalStackPanel { Spacing = 6 };
        var entries = new VerticalStackPanel { Spacing = 4 };
        var filter = new MyraInputBox { Width = 180, HintText = "Filter this list" };
        var deleteConfirmation = new HorizontalStackPanel { Spacing = 6, Visible = false };
        var actions = new HorizontalStackPanel { Spacing = 4 };
        var addActions = new HorizontalStackPanel { Spacing = 4 };
        var manual = new HorizontalStackPanel { Spacing = 4, Visible = false };
        var search = new VerticalStackPanel { Spacing = 4, Visible = false };
        var anyHue = new MyraCheckButton("Match any hue when targeting");

        var rename = new MyraButton("Rename");
        var duplicate = new MyraButton("Duplicate");
        var delete = MyraStyle.ApplyButtonDangerStyle(new MyraButton("Delete", () => deleteConfirmation.Visible = true));
        actions.Widgets.Add(listPicker);
        actions.Widgets.Add(name);
        actions.Widgets.Add(new MyraButton("New list", () =>
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

        deleteConfirmation.Widgets.Add(new MyraLabel("Agents linked to a deleted list stop using shared items until another source is selected.", MyraLabel.TextStyle.P));
        deleteConfirmation.Widgets.Add(MyraStyle.ApplyButtonDangerStyle(new MyraButton("Delete list", () =>
        {
            if (selected != null && library.Lists.Remove(selected))
            {
                library.SaveChanges();
                Select(library.Lists.FirstOrDefault());
            }
        })));
        deleteConfirmation.Widgets.Add(new MyraButton("Cancel", () => deleteConfirmation.Visible = false));

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
        addActions.Widgets.Add(new MyraButton("Add manually", () => manual.Visible = !manual.Visible));

        var graphic = new MyraInputBox { Width = 110, HintText = "Graphic ID" };
        var hue = MyraInputBox.Hue(ushort.MaxValue, tooltip: "Enter a hue or -1 for any hue.");
        var itemName = new MyraInputBox { Width = 150, HintText = "Item name" };
        manual.Widgets.Add(graphic);
        manual.Widgets.Add(hue);
        manual.Widgets.Add(itemName);
        manual.Widgets.Add(new MyraButton("Add", () =>
        {
            if (!StringHelper.TryParseInt((graphic.Text ?? "").Trim(), out int id) || id is <= 0 or > ushort.MaxValue ||
                !MyraInputBox.TryParseHue((hue.Text ?? "").Trim(), out ushort itemHue))
            {
                Report("Enter a valid graphic ID and hue. Hex IDs can start with 0x.");
                return;
            }
            Add([new SharedItem((ushort)id, itemHue, (itemName.Text ?? "").Trim())]);
        }));

        var query = new MyraInputBox { Width = 180, HintText = "Item name" };
        var results = new VerticalStackPanel { Spacing = 3 };
        var searchRow = new HorizontalStackPanel { Spacing = 4 };
        searchRow.Widgets.Add(query);
        searchRow.Widgets.Add(new MyraButton("Search", FindItems));
        search.Widgets.Add(searchRow);
        search.Widgets.Add(new ScrollViewer { Content = results, MaxHeight = 200 });

        details.Widgets.Add(addActions);
        details.Widgets.Add(anyHue);
        details.Widgets.Add(manual);
        details.Widgets.Add(search);
        details.Widgets.Add(filter);
        details.Widgets.Add(new ScrollViewer { Content = entries, MaxHeight = 280 });
        root.Widgets.Add(actions);
        root.Widgets.Add(deleteConfirmation);
        root.Widgets.Add(status);
        root.Widgets.Add(details);

        listPicker.ListView.SelectedIndexChanged += (_, _) =>
        {
            if (!refreshing && listPicker.ListView.SelectedIndex is int index && index >= 0 && index < choices.Length)
                Select(choices[index], refreshPicker: false);
        };
        filter.TextChangedByUser += (_, _) => RefreshEntries();
        Select(selected);

        return ReferenceEquals(interaction, AgentPageInteraction.InGame)
            ? new ScrollViewer { Content = root, MaxHeight = Math.Max(180, ScaleHelper.LogicalWindowHeight - 140) }
            : root;

        void Select(SharedItemList? list, bool refreshPicker = true)
        {
            selected = list;
            if (refreshPicker)
            {
                refreshing = true;
                choices = (library.Lists ?? []).Where(itemList => itemList != null).ToArray();
                listPicker.ListView.Widgets.Clear();
                foreach (SharedItemList itemList in choices)
                    listPicker.ListView.Widgets.Add(new Label { Text = itemList.Name });
                int index = Array.IndexOf(choices, list);
                listPicker.ListView.SelectedIndex = index >= 0 ? index : null;
                listPicker.Enabled = choices.Length > 0;
                refreshing = false;
            }
            name.Text = "";
            filter.Text = "";
            status.Visible = false;
            deleteConfirmation.Visible = false;
            rename.Enabled = duplicate.Enabled = delete.Enabled = details.Visible = list != null;
            if (list == null)
                Report("Create a list to start adding items.");
            RefreshEntries();
        }

        void RefreshEntries()
        {
            entries.Widgets.Clear();
            string text = (filter.Text ?? "").Trim();
            foreach (SharedItem item in selected?.Items ?? [])
            {
                if (item == null || !(item.Name?.Contains(text, StringComparison.OrdinalIgnoreCase) == true ||
                    $"0x{item.Graphic:X4}".Contains(text, StringComparison.OrdinalIgnoreCase)))
                    continue;

                entries.Widgets.Add(ItemRow(item, "Remove", () =>
                {
                    if (selected?.Items.Remove(item) == true)
                    {
                        library.SaveChanges();
                        RefreshEntries();
                    }
                }));
            }

            if (entries.Widgets.Count == 0)
                entries.Widgets.Add(new MyraLabel(string.IsNullOrEmpty(text) ? "This list is empty." : "No matching items.", MyraLabel.TextStyle.P));
        }

        void FindItems()
        {
            results.Widgets.Clear();
            string text = (query.Text ?? "").Trim();
            var tiles = Client.Game.UO.FileManager?.TileData.StaticData;
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
