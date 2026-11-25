#nullable enable
using System;
using System.Linq;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.MyraWindows.Theme;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;
using ClassicUO.Utility;
using Microsoft.Xna.Framework;
using Myra.Graphics2D;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.ItemLists;

internal static class WeaponSwitchListContent
{
    public static Widget Build(AgentPageInteraction interaction)
    {
        if (GameScene.Instance?.WeaponSwitch.Settings is not { } settings)
            return new MyraLabel("Log in to configure weapon switching.", MyraLabel.TextStyle.P);

        WeaponLoadout[] loadouts = Enum.GetValues<WeaponLoadout>();
        string[] labels = loadouts.Select(WeaponSwitchCatalog.Label).ToArray();
        WeaponSwitchMonster[] monsters = [];

        var root = new VerticalStackPanel { Spacing = 8 };
        var grid = new MyraGrid { ColumnSpacing = 12, RowSpacing = 6, MaxWidth = interaction.IsDesktop ? 1040 : 700 };
        grid.AddColumn(new Proportion(ProportionType.Pixels, interaction.IsDesktop ? 260 : 210));
        grid.AddColumn(new Proportion(ProportionType.Fill));
        var monsterList = new MonsterListView
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var fallback = Combo(labels);
        var primary = Combo(labels);
        var alternate = Combo(["None", .. labels]);
        var editor = Combo(labels);
        var selectedMonster = new MyraLabel("", MyraLabel.TextStyle.H3);
        var status = new MyraLabel("", MyraLabel.TextStyle.P) { Wrap = true };
        var weapon = new MyraInputBox { Width = 125 };
        var shield = new MyraInputBox { Width = 125 };
        var bagSummary = Help(DescribeBag());
        var addMonster = new MyraButton("Add Monster");
        var setBag = new MyraButton("Set Weapon Bag");
        bool refreshing = false;

        var names = new MyraGrid { RowSpacing = 6, Padding = new Thickness(10) };
        names.AddColumn(new Proportion(ProportionType.Fill));
        names.AddRow(count: 3);
        names.AddRow(new Proportion(ProportionType.Fill));
        names.AddWidget(new MyraLabel("Monsters", MyraLabel.TextStyle.H3), 0, 0);
        names.AddWidget(Help("Choose a monster to edit its assignments."), 1, 0);
        names.AddWidget(addMonster, 2, 0);
        names.AddWidget(monsterList, 3, 0);
        var monsterPanel = new Panel { BorderThickness = new Thickness(1) };
        if (interaction.IsDesktop)
        {
            monsterPanel.Background = DesktopAssistantTheme.Canvas;
            monsterPanel.Border = DesktopAssistantTheme.Divider;
        }
        else
        {
            monsterPanel.Border = monsterList.Border;
        }
        monsterPanel.Widgets.Add(names);
        grid.AddWidget(monsterPanel, 0, 0);

        var sections = new VerticalStackPanel { Spacing = 8 };
        sections.Widgets.Add(Section("Monster assignments",
            Help("First press equips Primary. Press again on the same monster to cycle to Alternate when set."),
            selectedMonster,
            Row("Primary", primary),
            Row("Alternate", alternate)));
        sections.Widgets.Add(Section("Shared loadouts",
            Help("Set the weapon and optional shield serials used by every monster assigned to this loadout."),
            Row("Loadout to edit", editor),
            SerialRow("Weapon serial", weapon, item => (Layer)item.ItemData.Layer is Layer.OneHanded or Layer.TwoHanded),
            SerialRow("Shield serial (optional)", shield, item => (Layer)item.ItemData.Layer == Layer.TwoHanded),
            new MyraButton("Save Loadout", SaveLoadout),
            status));

        var defaults = new MyraGrid { ColumnSpacing = 8 };
        defaults.AddColumn(new Proportion(ProportionType.Part, 1), count: 2);
        defaults.AddWidget(Section("Default loadout",
            Help("Used when the last attacked target has no monster assignment."), fallback), 0, 0);
        defaults.AddWidget(Section("Weapon bag",
            Help("Unequipped gear goes here during a switch."), setBag, bagSummary), 0, 1);
        sections.Widgets.Add(defaults);
        grid.AddWidget(sections, 0, 1);
        root.Widgets.Add(grid);
        root.Widgets.Add(Help("Bind Auto Switch Weapon under Hotkeys & Macros → Hotkeys."));

        addMonster.OnClick = ShowAddMonster;
        setBag.OnClick = ShowWeaponBag;

        fallback.ListView.SelectedIndex = Array.IndexOf(loadouts, settings.DefaultLoadout);
        fallback.ListView.SelectedIndexChanged += (_, _) =>
        {
            if (fallback.ListView.SelectedIndex is not int index)
                return;

            settings.DefaultLoadout = loadouts[index];
            settings.Save();
        };
        primary.ListView.SelectedIndexChanged += (_, _) => SaveAssignment();
        alternate.ListView.SelectedIndexChanged += (_, _) => SaveAssignment();
        editor.ListView.SelectedIndexChanged += (_, _) => RefreshSerials();
        monsterList.SelectedIndexChanged += (_, _) => SelectMonster();
        RefreshMonsters();
        return root;

        Widget Section(string title, params Widget[] children) => ItemListsTabContent.Section(interaction, title, children);

        void RefreshMonsters(string? selectedKey = null)
        {
            monsters = WeaponSwitchCatalog.Monsters.Concat(settings.CustomMonsters).OrderBy(m => m.Name).ToArray();
            monsterList.Widgets.Clear();
            foreach (var monster in monsters)
                monsterList.Widgets.Add(new Label { Text = MonsterLabel(monster) });

            int index = Array.FindIndex(monsters, monster => monster.Key == selectedKey);
            monsterList.SelectedIndex = index < 0 ? 0 : index;
        }

        void ShowAddMonster()
        {
            var name = new MyraInputBox { Width = 215 };
            var graphic = new MyraInputBox { Width = 120 };
            var hue = new MyraInputBox { Width = 120, Text = "*" };
            var message = Help("");
            var body = new VerticalStackPanel { Spacing = 6, Width = 385 };
            body.Widgets.Add(Help("Target a monster to fill its name, body graphic, and hue, or enter them manually. Use * for any hue."));
            body.Widgets.Add(Row("Name", name));
            body.Widgets.Add(Row("Body graphic", graphic));
            body.Widgets.Add(Row("Hue", hue));
            body.Widgets.Add(new MyraButton("Target Monster", () => interaction.SelectTarget(selected =>
            {
                if (selected is not Mobile mobile || mobile.Serial == World.Instance.Player?.Serial)
                {
                    message.Text = "Select a monster in game.";
                    return;
                }

                name.Text = mobile.Name ?? "";
                graphic.Text = $"0x{mobile.Graphic:X4}";
                hue.Text = $"0x{mobile.Hue:X4}";
                message.Text = "";
            })));
            body.Widgets.Add(message);

            ushort bodyId = 0;
            ushort? hueId = null;
            interaction.ShowDialog(addMonster, "Add Monster", body, confirmed =>
            {
                interaction.CancelTarget();
                if (!confirmed)
                    return;

                var monster = new WeaponSwitchMonster(bodyId, hueId, name.Text.Trim(), settings.DefaultLoadout);
                if (settings.AddMonster(bodyId, hueId, name.Text))
                    RefreshMonsters(monster.Key);
            }, () =>
            {
                if (string.IsNullOrWhiteSpace(name.Text) || !TryReadGraphic(graphic.Text, out bodyId) || bodyId == 0)
                {
                    message.Text = "Enter a name and a valid body graphic.";
                    return false;
                }

                hueId = null;
                if (hue.Text.Trim() != "*")
                {
                    if (!TryReadGraphic(hue.Text, out ushort value))
                    {
                        message.Text = "Enter a valid hue or * for any hue.";
                        return false;
                    }

                    hueId = value;
                }

                if (settings.ContainsMonster(bodyId, hueId))
                {
                    message.Text = "A monster with this body graphic and hue already exists.";
                    return false;
                }

                return true;
            });
        }

        void ShowWeaponBag()
        {
            var bag = new MyraInputBox { Width = 125, Text = SerialText(settings.WeaponBag) };
            var message = Help("");
            var body = new VerticalStackPanel { Spacing = 6, Width = 385 };
            body.Widgets.Add(Help("Target a container in your backpack or enter its serial. Clear the bag to use your backpack."));
            var row = SerialRow("Bag serial", bag, item => item.ItemData.IsContainer, message);
            row.Widgets.Add(new MyraButton("Clear", () => bag.Text = "0"));
            body.Widgets.Add(row);
            body.Widgets.Add(message);
            uint serial = 0;
            interaction.ShowDialog(setBag, "Set Weapon Bag", body, confirmed =>
            {
                interaction.CancelTarget();
                if (!confirmed)
                    return;

                settings.WeaponBag = serial;
                settings.Save();
                bagSummary.Text = DescribeBag();
            }, () => ReadSerial(bag, out serial, message));
        }

        string DescribeBag() => settings.WeaponBag == 0 ? "Using your backpack." : $"Bag serial {SerialText(settings.WeaponBag)}";

        void SelectMonster()
        {
            if (monsterList.SelectedIndex is not int index)
                return;

            var monster = monsters[index];
            var assignment = settings.GetAssignment(monster);
            refreshing = true;
            selectedMonster.Text = MonsterLabel(monster);
            primary.ListView.SelectedIndex = Array.IndexOf(loadouts, assignment.Primary);
            alternate.ListView.SelectedIndex = assignment.Alternate is { } choice ? Array.IndexOf(loadouts, choice) + 1 : 0;
            editor.ListView.SelectedIndex = primary.ListView.SelectedIndex;
            RefreshSerials();
            refreshing = false;
        }

        void SaveAssignment()
        {
            if (refreshing || monsterList.SelectedIndex is not int index || primary.ListView.SelectedIndex is not int first
                || alternate.ListView.SelectedIndex is not int second)
                return;

            settings.Assignments[monsters[index].Key] = new(loadouts[first], second == 0 ? null : loadouts[second - 1]);
            settings.Save();
        }

        void RefreshSerials()
        {
            if (editor.ListView.SelectedIndex is not int index)
                return;

            var set = settings.GetSet(loadouts[index]);
            weapon.Text = SerialText(set.Weapon);
            shield.Text = SerialText(set.Shield);
            status.Text = "";
        }

        void SaveLoadout()
        {
            if (editor.ListView.SelectedIndex is not int index || !ReadSerial(weapon, out uint weaponSerial)
                || !ReadSerial(shield, out uint shieldSerial))
                return;

            if (weaponSerial != 0 && weaponSerial == shieldSerial)
            {
                status.Text = "Choose different serials for the weapon and shield.";
                return;
            }

            settings.Loadouts[loadouts[index]] = new(weaponSerial, shieldSerial);
            settings.Save();
            status.Text = $"Saved {labels[index]} for all its assigned monsters.";
        }

        bool ReadSerial(MyraInputBox input, out uint serial, MyraLabel? message = null)
        {
            if (StringHelper.TryParseUint(input.Text.Trim(), out serial) && (serial == 0 || SerialHelper.IsItem(serial)))
            {
                input.Text = SerialText(serial);
                return true;
            }

            (message ?? status).Text = "Enter an item serial in decimal or 0x hex. Enter 0 to clear it.";
            return false;
        }

        HorizontalStackPanel SerialRow(string label, MyraInputBox input, Func<Item, bool> accepts, MyraLabel? message = null)
        {
            var row = Row(label, input);
            row.Widgets.Add(new MyraButton("Target", () => interaction.SelectTarget(selected =>
            {
                if (selected is Item item && accepts(item))
                    input.Text = SerialText(item.Serial);
                else
                    (message ?? status).Text = $"Select a valid item for {label.ToLowerInvariant()}.";
            })));
            return row;
        }
    }

    private sealed class MonsterListView : ListView
    {
        // Let the settings column determine the row height, while the list fills the arranged space.
        protected override Point InternalMeasure(Point availableSize)
        {
            Point desired = base.InternalMeasure(availableSize);
            return new Point(desired.X, Math.Min(220, desired.Y));
        }
    }

    private static MyraLabel Help(string text) => new(text, MyraLabel.TextStyle.P) { Wrap = true };

    private static bool TryReadGraphic(string text, out ushort value)
    {
        bool valid = StringHelper.TryParseInt(text.Trim(), out int number) && number is >= 0 and <= ushort.MaxValue;
        value = valid ? (ushort)number : (ushort)0;
        return valid;
    }

    private static HorizontalStackPanel Row(string label, Widget widget)
    {
        var row = new HorizontalStackPanel { Spacing = 6 };
        row.Widgets.Add(new MyraLabel(label, MyraLabel.TextStyle.P) { Width = 145 });
        row.Widgets.Add(widget);
        return row;
    }

    private static ComboView Combo(string[] labels)
    {
        var combo = new ComboView { MinWidth = 155 };
        foreach (string label in labels)
            combo.ListView.Widgets.Add(new Label { Text = label });
        return combo;
    }

    private static string MonsterLabel(WeaponSwitchMonster monster) =>
        $"{monster.Name} · {monster.Graphic:X4}/{monster.Hue?.ToString("X4") ?? "*"}";

    private static string SerialText(uint serial) => serial == 0 ? "0" : $"0x{serial:X8}";
}
