#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.ItemLists;

internal static class SharedItemListPicker
{
    public static Widget Build(AgentPageInteraction interaction, string agentName, Guid? selectedId, Action<Guid?> select,
        Func<IEnumerable<SharedItem>> localItems, Func<SharedItem, bool> createLocalRule, Action refreshLocal, Widget? defaults = null)
    {
        var root = new VerticalStackPanel { Spacing = 4 };
        var row = new HorizontalStackPanel { Spacing = 4 };
        var actions = new HorizontalStackPanel { Spacing = 4 };
        var picker = new ComboView { MinWidth = 185 };
        var help = new MyraLabel("", MyraLabel.TextStyle.P);
        var status = new MyraLabel("", MyraLabel.TextStyle.P) { Visible = false };
        var library = ItemListLibrary.Instance;
        var choices = new List<SharedItemList?>();
        bool refreshing = false;

        row.Widgets.Add(new MyraLabel("Item list", MyraLabel.TextStyle.P));
        row.Widgets.Add(picker);
        var manage = new MyraButton("Manage Item Lists");
        manage.Click += (_, _) => interaction.OpenItemLists();
        row.Widgets.Add(manage);

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
        actions.Widgets.Add(new MyraButton("Save local items as a list", () =>
        {
            SharedItem[] items = localItems().Where(item => item.Graphic != 0).ToArray();
            if (items.Length == 0)
            {
                Report("There are no local items with a graphic ID to save.");
                return;
            }

            SharedItemList list = library.Create(agentName, items);
            RefreshChoices();
            Report($"Saved {list.Items.Count} item types as {list.Name}.");
        }));

        root.Widgets.Add(row);
        root.Widgets.Add(actions);
        root.Widgets.Add(help);
        if (defaults != null)
            root.Widgets.Add(defaults);
        root.Widgets.Add(status);

        picker.ListView.SelectedIndexChanged += (_, _) =>
        {
            if (refreshing || picker.ListView.SelectedIndex is not int index || index < 0 || index >= choices.Count)
                return;

            // Preserve an unresolved link until the player explicitly picks a different source.
            if (index != 0 && choices[index] == null)
                return;

            selectedId = choices[index]?.Id;
            select(selectedId);
            status.Visible = false;
            UpdateAppearance();
        };

        RefreshChoices();
        return root;

        void RefreshChoices()
        {
            refreshing = true;
            choices.Clear();
            picker.ListView.Widgets.Clear();
            choices.Add(null);
            picker.ListView.Widgets.Add(new Label { Text = "Local entries" });
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
            UpdateAppearance();
        }

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
        }

        void Report(string text)
        {
            status.Text = text;
            status.Visible = true;
        }
    }

    public static SharedItem? FromLocal(int graphic, ushort hue, string name = "") =>
        graphic is > 0 and <= ushort.MaxValue ? new SharedItem((ushort)graphic, hue, name) : null;
}
