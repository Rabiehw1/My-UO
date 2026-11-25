#nullable enable
using System;
using System.Linq;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;
using ClassicUO.Utility;
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
        WeaponSwitchMonster[] monsters = WeaponSwitchCatalog.Monsters.OrderBy(m => m.Name).ToArray();

        var root = new VerticalStackPanel { Spacing = 8 };
        var grid = new MyraGrid { ColumnSpacing = 12, RowSpacing = 6 };
        grid.AddColumn(new Proportion(ProportionType.Part, 1));
        grid.AddColumn(new Proportion(ProportionType.Part, 2));
        var monsterList = new ListView { Height = 335 };
        foreach (var monster in monsters)
            monsterList.Widgets.Add(new Label { Text = MonsterLabel(monster) });

        var fallback = Combo(labels);
        var primary = Combo(labels);
        var alternate = Combo(["None", .. labels]);
        var editor = Combo(labels);
        var selectedMonster = new MyraLabel("", MyraLabel.TextStyle.H3);
        var status = new MyraLabel("", MyraLabel.TextStyle.P) { Wrap = true };
        var editorPanel = new VerticalStackPanel { Spacing = 5 };
        var weapon = new MyraInputBox { Width = 125 };
        var shield = new MyraInputBox { Width = 125 };
        var bag = new MyraInputBox { Width = 125 };
        bag.Text = SerialText(settings.WeaponBag);
        bool refreshing = false;

        var names = new VerticalStackPanel { Spacing = 5 };
        names.Widgets.Add(new MyraLabel("Monsters", MyraLabel.TextStyle.H3));
        names.Widgets.Add(monsterList);
        grid.AddWidget(names, 0, 0);
        grid.AddWidget(editorPanel, 0, 1);

        editorPanel.Widgets.Add(selectedMonster);
        editorPanel.Widgets.Add(Row("Primary loadout", primary));
        editorPanel.Widgets.Add(Row("Alternate loadout", alternate));
        editorPanel.Widgets.Add(new MyraButton("Edit primary loadout", () => editor.ListView.SelectedIndex = primary.ListView.SelectedIndex));
        editorPanel.Widgets.Add(Row("Default loadout", fallback));
        editorPanel.Widgets.Add(Row("Loadout to edit", editor));
        editorPanel.Widgets.Add(SerialRow("Weapon serial", weapon, item => (Layer)item.ItemData.Layer is Layer.OneHanded or Layer.TwoHanded));
        editorPanel.Widgets.Add(SerialRow("Shield serial (optional)", shield, item => (Layer)item.ItemData.Layer == Layer.TwoHanded));
        editorPanel.Widgets.Add(new MyraButton("Save loadout serials", SaveLoadout));
        editorPanel.Widgets.Add(SerialRow("Weapon bag (optional)", bag, item => item.ItemData.IsContainer));
        editorPanel.Widgets.Add(new MyraButton("Save weapon bag", () =>
        {
            if (!ReadSerial(bag, out uint serial))
                return;

            settings.WeaponBag = serial;
            settings.Save();
            status.Text = serial == 0 ? "Removed equipment goes to your backpack." : "Weapon bag saved.";
        }));

        root.Widgets.Add(new MyraLabel("Configure shared loadouts, then assign them to monsters. Bind Auto Switch Weapon in Hotkeys.", MyraLabel.TextStyle.P) { Wrap = true });
        root.Widgets.Add(grid);
        root.Widgets.Add(status);

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
        monsterList.SelectedIndex = 0;
        return root;

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

        bool ReadSerial(MyraInputBox input, out uint serial)
        {
            if (StringHelper.TryParseUint(input.Text.Trim(), out serial) && (serial == 0 || SerialHelper.IsItem(serial)))
            {
                input.Text = SerialText(serial);
                return true;
            }

            status.Text = "Enter an item serial in decimal or 0x hex. Enter 0 to clear it.";
            return false;
        }

        Widget SerialRow(string label, MyraInputBox input, Func<Item, bool> accepts)
        {
            var row = Row(label, input);
            row.Widgets.Add(new MyraButton("Target", () => interaction.SelectTarget(selected =>
            {
                if (selected is Item item && accepts(item))
                    input.Text = SerialText(item.Serial);
                else
                    status.Text = $"Select a valid item for {label.ToLowerInvariant()}.";
            })));
            return row;
        }
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
