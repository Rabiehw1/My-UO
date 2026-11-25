#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using FontStashSharp.RichText;
using Myra.Graphics2D;
using Myra.Graphics2D.UI.Styles;
using Myra.Graphics2D.UI.WrapPanel;
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
        var listPicker = new ListView { Height = 285, HorizontalAlignment = HorizontalAlignment.Stretch };
        var name = new MyraInputBox { Width = 180, HintText = "List name" };
        var status = new MyraLabel("", MyraLabel.TextStyle.P) { Visible = false };
        var details = new VerticalStackPanel { Spacing = 6 };
        var entries = new ListView { Id = "item-table-rows" };
        var rowStyle = new ListBoxStyle(entries.ListBoxStyle);
        rowStyle.Padding = new Thickness(0);
        rowStyle.BorderThickness = new Thickness(0);
        rowStyle.ListItemStyle.Padding = new Thickness(0);
        rowStyle.ListItemStyle.BorderThickness = new Thickness(0);
        entries.ApplyListBoxStyle(rowStyle);
        entries.Height = 285;
        entries.HorizontalAlignment = HorizontalAlignment.Stretch;
        entries.ScrollViewer.ShowHorizontalScrollBar = false;
        SharedItem[] visibleItems = [];
        var filter = new MyraInputBox { Width = 230, HintText = "Search by name, graphic ID, or hue" };
        var names = new VerticalStackPanel { Spacing = 5 };
        var grid = new MyraGrid { ColumnSpacing = 12, MaxWidth = interaction.IsDesktop ? 1040 : 700 };
        grid.AddColumn(new Proportion(ProportionType.Pixels, interaction.IsDesktop ? 202 : 175));
        grid.AddColumn(new Proportion(ProportionType.Fill));
        var listActions = Toolbar();
        var addActions = Toolbar();
        var transferActions = Toolbar();
        var itemActions = Toolbar();
        var matchHue = new MyraCheckButton("Match target hue") { IsChecked = true, Tooltip = "Uncheck to match any hue." };

        var rename = Button("Rename list");
        var duplicate = Button("Copy list");
        var delete = Button("Remove list");
        MyraStyle.ApplyButtonDangerStyle(delete);
        var edit = Button("Edit item");
        var remove = Button("Remove item");
        MyraStyle.ApplyButtonDangerStyle(remove);
        var earlier = Button("Move up");
        var later = Button("Move down");
        earlier.OnClick = () => MoveSelected(-1);
        later.OnClick = () => MoveSelected(1);
        var addManual = Button("Add item", primary: true);
        var importCharacter = Button("Import from Character");
        names.Widgets.Add(listPicker);
        names.Widgets.Add(name);
        names.Widgets.Add(listActions);
        listActions.Widgets.Add(Button("New list", () =>
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
            {
                var copy = library.Create(selected.Name);
                copy.Items = selected.Items.ToList();
                library.SaveChanges();
                Select(copy);
            }
        };
        listActions.Widgets.Add(rename);
        listActions.Widgets.Add(duplicate);
        listActions.Widgets.Add(delete);

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

        addActions.Widgets.Add(Button("Add from Target", () => interaction.SelectTarget(target =>
        {
            if (target is Item item)
                Add([FromItem(item)]);
            else
                Report("Select an item in the game.");
        })));
        addActions.Widgets.Add(Button("Add container contents", () => interaction.SelectTarget(target =>
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
        })));


        transferActions.Widgets.Add(Button("Import", () =>
        {
            try
            {
                string text = Clipboard.GetClipboardText() ?? "";
                List<SharedItem>? items;
                try
                {
                    items = JsonSerializer.Deserialize(text, ItemListJsonContext.Default.ListSharedItem);
                }
                catch (JsonException)
                {
                    items = JsonSerializer.Deserialize(text, AutoLootJsonContext.Default.ListAutoLootConfigEntry)?
                        .Where(rule => rule != null && rule.Graphic is >= -1 and <= ushort.MaxValue)
                        .Select(GatheringListRules.From).ToList();
                }
                if (items == null || items.Count == 0)
                    Report("The clipboard has no item rules.");
                else
                    Add(items.Where(item => item != null && IsValidRegex(item.RegexSearch)));
            }
            catch (JsonException)
            {
                Report("Copy a valid shared or legacy item-list export first.");
            }
        }));
        transferActions.Widgets.Add(Button("Export", () =>
        {
            if (selected != null)
            {
                JsonSerializer.Serialize(selected.Items, ItemListJsonContext.Default.ListSharedItem).CopyToClipboard();
                Report("Copied the list to the clipboard.");
            }
        }));
        importCharacter.OnClick = ShowCharacterImports;
        transferActions.Widgets.Add(importCharacter);

        addManual.OnClick = () => ShowEditor(addManual, null);
        edit.OnClick = () =>
        {
            if (SelectedItem() is { } item)
                ShowEditor(edit, item);
        };

        SharedItem? SelectedItem() => entries.SelectedIndex is int index && index >= 0 && index < visibleItems.Length
            ? visibleItems[index] : null;
        remove.OnClick = () =>
        {
            if (SelectedItem() is { } item && selected?.Items.Remove(item) == true)
            {
                library.SaveChanges();
                RefreshEntries();
            }
        };
        addActions.Widgets.Insert(0, addManual);
        itemActions.Widgets.Add(edit);
        itemActions.Widgets.Add(earlier);
        itemActions.Widgets.Add(later);
        itemActions.Widgets.Add(remove);
        entries.SelectedIndexChanged += (_, _) => UpdateItemActions();

        var headers = BuildColumns("Art", "Graphic ID", "Hue", "Item name", "Regex", "Priority", "Max", "Destination");
        headers.Id = "item-table-header";
        headers.Height = 26;
        if (interaction.IsDesktop)
        {
            headers.Background = DesktopAssistantTheme.TabStrip;
            foreach (Label heading in headers.Widgets.OfType<Label>())
                heading.Font = DesktopAssistantTheme.Font(-1, bold: true);
        }
        // Both header and rows pan together. The extra width leaves room for the vertical scrollbar.
        var table = new VerticalStackPanel { Width = ItemColumns.Sum() + 24 };
        table.Widgets.Add(headers);
        table.Widgets.Add(entries);
        details.Widgets.Add(addActions);
        details.Widgets.Add(Toolbar(filter, matchHue));
        details.Widgets.Add(new ItemTableViewport
        {
            Content = table,
            ShowVerticalScrollBar = false,
            HorizontalAlignment = HorizontalAlignment.Stretch
        });
        var footer = Toolbar(itemActions, transferActions);
        footer.HorizontalSpacing = 14;
        details.Widgets.Add(footer);
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

        void ShowEditor(Widget anchor, SharedItem? editing)
        {
            if (selected is not { } list)
                return;

            var graphic = new MyraInputBox { Text = editing == null ? null : $"0x{editing.Graphic:X4}", HintText = "0x0000" };
            var hue = MyraInputBox.Hue(editing?.Hue ?? ushort.MaxValue, tooltip: "Enter a hue or -1 for any hue.");
            var itemName = new MyraInputBox { Text = editing?.Name ?? "" };
            var regex = new MyraInputBox { Text = editing?.RegexSearch ?? "" };
            var priority = new ComboView { MinWidth = 80 };
            foreach (string value in new[] { "Low", "Normal", "High" })
                priority.ListView.Widgets.Add(new Label { Text = value });
            priority.ListView.SelectedIndex = Math.Clamp(editing?.Priority ?? 1, 0, 2);
            var max = new MyraInputBox { Text = (editing?.MaxAmount ?? 0).ToString(), Tooltip = "0 allows any amount." };
            var destination = new MyraInputBox
            {
                Text = editing is { DestinationContainer: > 0 } ? $"0x{editing.DestinationContainer:X8}" : null,
                HintText = "Default container"
            };
            var message = new MyraLabel("", MyraLabel.TextStyle.P) { Wrap = true, Visible = false };
            var fields = new MyraGrid { ColumnSpacing = 8, RowSpacing = 5 };
            fields.AddColumn(new Proportion(ProportionType.Pixels, 94));
            fields.AddColumn(new Proportion(ProportionType.Fill));
            fields.AddRow(count: 7);
            AddField(0, "Item name", itemName);
            AddField(1, "Graphic ID", graphic);
            AddField(2, "Hue", hue);
            AddField(3, "Regex", regex);
            AddField(4, "Priority", priority);
            AddField(5, "Max amount", max);
            ScrollViewer? editor = null;
            var destinationRow = new MyraGrid { ColumnSpacing = 5 };
            destinationRow.AddColumn(new Proportion(ProportionType.Fill));
            destinationRow.AddColumn();
            destinationRow.AddWidget(destination, 0, 0);
            destinationRow.AddWidget(Button("Target", () => interaction.SelectTarget(target =>
            {
                if (target is Item { ItemData.IsContainer: true } container)
                    destination.Text = $"0x{container.Serial:X8}";
                else
                    Error("Select a container in the game.");
            })), 0, 1);
            AddField(6, "Destination", destinationRow);

            var body = new VerticalStackPanel { Spacing = 6 };
            body.Widgets.Add(message);
            if (editing == null)
                body.Widgets.Add(BuildCatalogSearch(graphic, itemName));
            body.Widgets.Add(fields);
            editor = DialogBody(anchor, body, 345);
            SharedItem? updated = null;
            interaction.ShowDialog(anchor, editing == null ? "Add item" : "Edit item", editor, confirmed =>
                {
                    interaction.CancelTarget();
                    if (!confirmed || updated == null)
                        return;

                    if (editing == null)
                        list.AddItems([updated], preserveRules: true);
                    else
                    {
                        int index = list.Items.IndexOf(editing);
                        if (index < 0)
                            return;
                        list.Items[index] = updated;
                    }
                    library.SaveChanges();
                    RefreshEntries(updated);
                    Report(editing == null ? "Item added." : "Item updated.");
                }, Validate);

            void AddField(int row, string label, Widget input)
            {
                fields.AddWidget(new Label { Text = label, VerticalAlignment = VerticalAlignment.Center }, row, 0);
                input.HorizontalAlignment = HorizontalAlignment.Stretch;
                fields.AddWidget(input, row, 1);
            }

            bool Error(string text)
            {
                message.Text = text;
                message.Visible = true;
                editor?.ResetScroll();
                return false;
            }

            bool Validate()
            {
                if (!StringHelper.TryParseInt((graphic.Text ?? "").Trim(), out int id) || id is <= 0 or > ushort.MaxValue
                    || !MyraInputBox.TryParseHue((hue.Text ?? "").Trim(), out ushort itemHue))
                    return Error("Enter a graphic ID and hue. Hex IDs can start with 0x. Use -1 for any hue.");

                string pattern = regex.Text ?? "";
                if (!IsValidRegex(pattern))
                    return Error("Enter a valid regex or leave it blank.");
                if (!int.TryParse(max.Text, out int limit) || limit < 0)
                    return Error("Enter a non-negative maximum amount. Use 0 for any amount.");
                if (!TryParseDestination(destination.Text, out uint container))
                    return Error("Enter a valid destination serial or leave it blank.");

                if (list.Items.Any(item => item != null && !ReferenceEquals(item, editing)
                    && item.Graphic == id && item.Hue == itemHue && item.RegexSearch == pattern))
                    return Error("An item with this graphic, hue, and regex is already on the list.");

                updated = new SharedItem((ushort)id, itemHue, (itemName.Text ?? "").Trim())
                {
                    RegexSearch = pattern, Priority = priority.ListView.SelectedIndex ?? 1,
                    MaxAmount = limit, DestinationContainer = container
                };
                return true;
            }
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
            ResetInput(name);
            ResetInput(filter);
            status.Visible = false;
            rename.Enabled = duplicate.Enabled = delete.Enabled = details.Visible = list != null;
            if (list == null)
                Report("Create a list to start adding items.");
            RefreshEntries();
        }

        void RefreshEntries(SharedItem? keepSelected = null)
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
                var row = BuildColumns("", item.Graphic == ushort.MaxValue ? "Any" : $"0x{item.Graphic:X4}",
                    item.Hue == ushort.MaxValue ? "Any" : $"0x{item.Hue:X4}",
                    Display(item.Name), Display(item.RegexSearch),
                    item.Priority switch { 0 => "Low", 2 => "High", _ => "Normal" },
                    item.MaxAmount == 0 ? "Any" : item.MaxAmount.ToString(),
                    item.DestinationContainer == 0 ? "Default" : $"0x{item.DestinationContainer:X8}");
                if (Client.Game.UO.Arts != null && item.Graphic != ushort.MaxValue)
                    row.AddWidget(new MyraArtTexture(item.Graphic, item.Hue == ushort.MaxValue ? (ushort)0 : item.Hue, 24)
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }, 0, 0);
                entries.Widgets.Add(row);
            }

            if (keepSelected != null)
            {
                int index = Array.IndexOf(visibleItems, keepSelected);
                if (index >= 0)
                    entries.SelectedIndex = index;
            }
            UpdateItemActions();
        }

        void UpdateItemActions()
        {
            int index = SelectedItem() is { } item && selected != null ? selected.Items.IndexOf(item) : -1;
            edit.Enabled = remove.Enabled = index >= 0;
            earlier.Enabled = index > 0;
            later.Enabled = index >= 0 && index < selected!.Items.Count - 1;
        }

        void ShowCharacterImports()
        {
            var choices = new VerticalStackPanel { Spacing = 4 };
            var picker = new ListView { Height = 190 };
            var rulesByChoice = new List<SharedItem[]>();

            foreach (var (character, rules) in AutoLootManager.Instance.GetOtherCharacterConfigs().OrderBy(entry => entry.Key))
                AddChoice($"{character} · Auto Loot", rules.Select(GatheringListRules.From));
            foreach (var (character, rules) in ScavengerManager.GetOtherCharacterConfigs().OrderBy(entry => entry.Key))
                AddChoice($"{character} · Scavenger", rules.Select(GatheringListRules.From));
            foreach (var (name, items) in OrganizerConfigSave.GetOtherCharacterLists())
                AddChoice(name, items);

            if (rulesByChoice.Count > 0)
            {
                choices.Widgets.Add(picker);
                picker.SelectedIndex = 0;
            }
            else
                choices.Widgets.Add(new Label { Text = "No legacy item rules found for other characters.", Wrap = true });

            interaction.ShowDialog(importCharacter, "Import from Character", DialogBody(importCharacter, choices, 340), confirmed =>
            {
                if (confirmed && picker.SelectedIndex is int index)
                    Add(rulesByChoice[index]);
            }, () => picker.SelectedIndex.HasValue);

            void AddChoice(string label, IEnumerable<SharedItem> rules)
            {
                var valid = rules.Where(rule => IsValidRegex(rule.RegexSearch)).ToArray();
                if (valid.Length == 0)
                    return;

                rulesByChoice.Add(valid);
                picker.Widgets.Add(new Label { Text = label, Wrap = true });
            }
        }

        SharedItem FromItem(Item item) =>
            new(item.Graphic, matchHue.IsChecked ? item.Hue : ushort.MaxValue, item.OPLName ?? item.Name ?? "");

        void Add(IEnumerable<SharedItem> items)
        {
            if (selected == null)
                return;

            int added = selected.AddItems(items, preserveRules: true);
            if (added > 0)
            {
                library.SaveChanges();
                RefreshEntries();
            }

            Report(added > 0 ? $"Added {added} item types to {selected.Name}." : "These item types are already on the list.");
        }

        MyraButton Button(string title, Action? onClick = null, bool primary = false) => interaction.IsDesktop
            ? DesktopAssistantTheme.ActionButton(title, onClick, primary)
            : new MyraButton(title, onClick);

        void MoveSelected(int offset)
        {
            if (selected == null || SelectedItem() is not { } item)
                return;

            int index = selected.Items.IndexOf(item);
            int destinationIndex = index + offset;
            if (index < 0 || destinationIndex < 0 || destinationIndex >= selected.Items.Count)
                return;

            (selected.Items[index], selected.Items[destinationIndex]) = (selected.Items[destinationIndex], selected.Items[index]);
            library.SaveChanges();
            RefreshEntries(item);
        }

        void Report(string text)
        {
            status.Text = text;
            status.Visible = true;
        }
    }

    private static void ResetInput(MyraInputBox input)
    {
        // Resetting Text hides the hint, so set the placeholder again after clearing.
        string hint = input.HintText;
        input.Text = null;
        input.HintText = hint;
    }

    private static Widget BuildCatalogSearch(MyraInputBox graphic, MyraInputBox itemName)
    {
        var search = new MyraInputBox
        {
            HintText = "Find an item by name",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var matches = new ListView { Height = 100, Visible = false, HorizontalAlignment = HorizontalAlignment.Stretch };
        var results = new List<(int Graphic, string Name)>();
        var status = new Label { Text = "No items found.", Visible = false };
        var panel = new VerticalStackPanel { Spacing = 4 };
        panel.Widgets.Add(search);
        panel.Widgets.Add(matches);
        panel.Widgets.Add(status);

        search.TextChangedByUser += (_, _) =>
        {
            matches.Widgets.Clear();
            results.Clear();
            matches.Visible = status.Visible = false;
            string text = (search.Text ?? "").Trim();
            var tiles = Client.Game.UO.FileManager?.TileData?.StaticData;
            if (text.Length == 0 || tiles == null)
                return;

            for (int id = 1; id < tiles.Length && id <= ushort.MaxValue && results.Count < 40; id++)
            {
                string name = tiles[id].Name;
                if (string.IsNullOrEmpty(name) || !name.Contains(text, StringComparison.OrdinalIgnoreCase))
                    continue;

                results.Add((id, name));
                matches.Widgets.Add(new Label { Text = $"{name}  ·  0x{id:X4}", Wrap = true });
            }
            matches.Visible = results.Count > 0;
            status.Visible = results.Count == 0;
        };
        matches.SelectedIndexChanged += (_, _) =>
        {
            if (matches.SelectedIndex is not int index || index < 0 || index >= results.Count)
                return;

            var item = results[index];
            graphic.Text = $"0x{item.Graphic:X4}";
            itemName.Text = item.Name;
            matches.Visible = false;
        };
        return panel;
    }

    private static readonly int[] ItemColumns = [34, 66, 58, 132, 125, 65, 49, 100];

    private static MyraGrid BuildColumns(params string[] values)
    {
        var row = new MyraGrid { Width = ItemColumns.Sum(), ColumnSpacing = 0, MinHeight = 26 };
        for (int i = 0; i < ItemColumns.Length; i++)
        {
            row.AddColumn(new Proportion(ProportionType.Pixels, ItemColumns[i]));
            if (i == 0 && values[i].Length == 0)
                continue;

            row.AddWidget(new Label
            {
                Text = values[i], Tooltip = values[i], Padding = new Thickness(4, 0),
                AutoEllipsisMethod = AutoEllipsisMethod.Character,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                ClipToBounds = true
            }, 0, i);
        }
        return row;
    }

    private static WrapPanel Toolbar(params Widget[] widgets)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalSpacing = 4, VerticalSpacing = 4, UniformSizing = false };
        foreach (Widget widget in widgets)
            row.Widgets.Add(widget);
        return row;
    }

    private sealed class ItemTableViewport : ScrollViewer
    {
        protected override Microsoft.Xna.Framework.Point InternalMeasure(Microsoft.Xna.Framework.Point availableSize)
        {
            var desired = base.InternalMeasure(availableSize);
            // Keep the surrounding buttons and panes within the window while the table pans inside it.
            desired.X = Math.Min(desired.X, availableSize.X);
            return desired;
        }
    }

    private static ScrollViewer DialogBody(Widget anchor, Widget body, int preferredWidth)
    {
        var viewport = anchor.Desktop?.BoundsFetcher() ?? new Microsoft.Xna.Framework.Rectangle(
            0, 0, ScaleHelper.LogicalWindowWidth, ScaleHelper.LogicalWindowHeight);
        return new ScrollViewer
        {
            Content = body,
            Width = Math.Min(preferredWidth, Math.Max(160, viewport.Width - 52)),
            MaxHeight = Math.Max(85, viewport.Height - 116),
            ShowHorizontalScrollBar = false
        };
    }

    private static string Display(string? value) => string.IsNullOrWhiteSpace(value) ? "None" : value;

    private static bool IsValidRegex(string? pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return true;
        try
        {
            _ = new Regex(pattern, RegexOptions.Multiline, TimeSpan.FromMilliseconds(100));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryParseDestination(string? text, out uint serial)
    {
        serial = 0;
        text = text?.Trim();
        return string.IsNullOrEmpty(text) || (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? uint.TryParse(text.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out serial)
            : uint.TryParse(text, out serial));
    }

    internal static Widget Section(AgentPageInteraction interaction, string title, params Widget[] children) =>
        interaction.IsDesktop
            ? ClassicUO.Game.UI.MyraWindows.Theme.DesktopAssistantTheme.Group(title, children)
            : new VisualContainer(new VisualContainerProps { LabelText = title }, children);
}
