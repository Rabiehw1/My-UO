#nullable enable
using System;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.IO;
using ClassicUO.Network;

namespace ClassicUO.Game.Managers.Trainer;

internal static class BlacksmithTrainer
{
    internal sealed record Recipe(int Minimum, string Name, int Ingots);
    // Zulu Hotel Omega Info Vault: Blacksmithy (iron recipes), Ores & Ingots (difficulty).
    private static readonly Recipe[] Plate =
    [
        new(11, "Female Breastplate", 25), new(66, "Platemail Gorget", 10),
        new(69, "Platemail Gloves", 12), new(72, "Plate Helm", 15),
        new(76, "Platemail Arms", 18), new(79, "Platemail Legs", 20), new(85, "Breastplate", 25)
    ];
    private static readonly (string Name, int Difficulty)[] Metals =
    [
        ("Iron",0), ("Gold",1), ("Spike",5), ("Fruity",10), ("Bronze",15), ("Ice Rock",20),
        ("Black Dwarf",25), ("Dull Copper",30), ("Platinum",35), ("Silver Rock",40),
        ("Dark Pagan",45), ("Copper",50), ("Mystic",55), ("Spectral",60), ("Old Britain",65),
        ("Onyx",70), ("Red Elven",75), ("Undead",80), ("Pyrite",85), ("Virginity",90),
        ("Malachite",95), ("Lavarock",97), ("Azurite",98), ("Dripstone",100), ("Executor",104),
        ("Peachblue",108), ("Destruction",112), ("Anra",116), ("Crystal",119), ("Doom",122),
        ("Goddess",125), ("Ebon Twilight Sapphire",130), ("Dark Sable Ruby",130),
        ("Radiant Nimbus Diamond",140), ("New Zulu",150)
    ];

    private static Recipe? _pending;
    private static long _deadline, _cursorExpiry;
    private static uint _stack, _tool;
    private static int _categoryDepth;
    private static string? _warning;
    private static bool _timedOut;

    internal static Recipe? SelectRecipe(int skillFixed) => Array.FindLast(Plate, r => skillFixed >= 10 * r.Minimum);

    internal static bool Eligible(int skillFixed, int difficulty) => skillFixed < 950
        ? difficulty == 0 : difficulty > 0 && skillFixed >= (85 + difficulty) * 10;

    internal static void Reset()
    {
        if (_pending != null && TargetManager.NextAutoTarget.TargetSerial == _stack &&
            TargetManager.NextAutoTarget.ExpiresAt == _cursorExpiry)
            TargetManager.NextAutoTarget.Clear();
        _timedOut = false;
        _pending = null;
        _warning = null;
    }

    internal static bool Waiting(World world)
    {
        if (_pending == null)
            return false;
        if (world.Player is not { IsDead: false } || ProfileManager.CurrentProfile?.Trainer.Selected.Contains("blacksmithy") != true)
            Reset();
        else if (Time.Ticks >= _deadline)
        {
            Reset();
            _timedOut = true;
            Warn(world, "trainer_smith_timeout");
        }
        return _pending != null;
    }

    internal static bool Use(World world, uint toolSerial)
    {
        var player = world.Player!;
        if (player.IsDead || _timedOut)
            return false;
        var skill = player.Skills == null ? null : Array.Find(player.Skills, s => s?.Index == 7); // UO Blacksmithy skill ID.
        int value = skill?.ValueFixed ?? 0;
        var recipe = SelectRecipe(value);
        if (recipe == null)
            return Warn(world, "trainer_smith_min");
        var bag = player.Backpack;
        var tool = world.Items.Get(toolSerial);
        if (bag == null || tool == null || (tool.Container != player.Serial && tool.BackpackOrRootContainer != bag.Serial)
            || tool.Graphic is not (0x13E3 or 0x13E4 or 0xFBB or 0xFBC))
            return Warn(world, "trainer_smith_tool");

        Item? best = null;
        int highest = -1;
        foreach (Item item in world.Items.Values)
        {
            if (item.Graphic is < 0x1BEF or > 0x1BF5 || item.Amount < recipe.Ingots || item.BackpackOrRootContainer != bag.Serial)
                continue;
            string name = item.GetNormalizedName(false);
            foreach (var metal in Metals)
            {
                if (metal.Difficulty <= highest || !Eligible(value, metal.Difficulty) || (metal.Difficulty == 0 && item.Hue != 0))
                    continue;
                if (!string.Equals(name, metal.Name + " ingots", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(name, metal.Name + " ingot", StringComparison.OrdinalIgnoreCase)
                    && !(metal.Difficulty == 0 && item.Hue == 0 && string.Equals(name, "ingots", StringComparison.OrdinalIgnoreCase)))
                    continue;
                best = item;
                highest = metal.Difficulty;
            }
        }
        if (best == null)
            return Warn(world, "trainer_smith_stock");
        _warning = null;
        _pending = recipe;
        _tool = toolSerial;
        _stack = best.Serial;
        _categoryDepth = 0;
        _deadline = Time.Ticks + 5000;
        TargetManager.SetAutoTarget(_stack, TargetType.Neutral, true, 3000);
        _cursorExpiry = TargetManager.NextAutoTarget.ExpiresAt;
        GameActions.DoubleClick(world, toolSerial);
        return true;
    }

    // Inspect a copy so menus we don't own still open normally.
    internal static bool TryMenu(World world, uint serial, ushort id, string title, int count, StackDataReader entries)
    {
        if (!Waiting(world) || _pending == null ||
            TargetManager.NextAutoTarget.TargetSerial == _stack)
            return false;
        bool smith = serial == _tool || serial == _stack ||
            title.Contains("smith", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("plate", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("armor", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("armour", StringComparison.OrdinalIgnoreCase);
        if (!smith)
            return false;
        int best = 0, index = 0;
        ushort graphic = 0, hue = 0;
        bool gray = false;
        for (int i = 1; i <= count; i++)
        {
            ushort g = entries.ReadUInt16BE(), h = entries.ReadUInt16BE();
            if (i == 1)
                gray = g == 0;
            string label = entries.ReadASCII(entries.ReadUInt8()).Trim().ToLowerInvariant();
            int rank = IsRecipeLabel(_pending.Name, label) ? 3 :
                label is "platemail" or "plate mail" or "plate armor" ? 2 :
                label is "armor" or "armour" ? 1 : 0;
            if (rank <= best)
                continue;
            (best, index, graphic, hue) = (rank, i, g, h);
        }
        if (best == 0 || (best < 3 && ++_categoryDepth > 2))
            return false;
        if (best == 3)
            Reset();
        if (gray)
            AsyncNetClient.Socket.Send_GrayMenuResponse(serial, id, (ushort)index);
        else
            AsyncNetClient.Socket.Send_MenuResponse(serial, id, index, graphic, hue);
        return true;
    }

    internal static bool IsRecipeLabel(string recipe, string label) =>
        label.Equals(recipe, StringComparison.OrdinalIgnoreCase) || (recipe, label) switch
        {
            ("Breastplate", "platemail breastplate" or "platemail tunic") => true,
            ("Platemail Arms", "platemail sleeves") => true,
            ("Platemail Legs", "platemail leggings") => true,
            ("Platemail Gloves", "platemail gauntlets") => true,
            _ => false
        };

    private static bool Warn(World world, string key)
    {
        if (_warning != key)
            GameActions.Print(world, TazLang.Get(key), Constants.HUE_WARN);
        _warning = key;
        return false;
    }
}
