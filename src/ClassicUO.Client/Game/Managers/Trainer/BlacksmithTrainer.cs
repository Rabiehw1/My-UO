#nullable enable
using System;
using System.Collections.Generic;
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

    private enum Recovery { None, AwaitPiece, TargetPiece, TargetForge, AwaitIngots }
    private static Recovery _recovery;
    private static readonly HashSet<uint> ExistingPieces = new();
    private static uint _piece, _bag;
    private static ushort _metalHue, _outputGraphic;
    private static int _stockBefore, _stockAfterCraft;
    private static long _nextCheck;

    internal static Recipe? SelectRecipe(int skillFixed) => Array.FindLast(Plate, r => skillFixed >= 10 * r.Minimum);

    internal static bool Eligible(int skillFixed, int difficulty) => skillFixed < 950
        ? difficulty == 0 : difficulty > 0 && skillFixed >= (85 + difficulty) * 10;

    internal static void Reset()
    {
        if (_pending != null && TargetManager.NextAutoTarget.TargetSerial == _stack &&
            TargetManager.NextAutoTarget.ExpiresAt == _cursorExpiry)
            TargetManager.NextAutoTarget.Clear();
        ExistingPieces.Clear();
        _recovery = Recovery.None;
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
            Pause(world, _recovery == Recovery.None ? "trainer_smith_timeout" : "trainer_smith_recovery_timeout");
        }
        else if (_recovery != Recovery.None && Time.Ticks >= _nextCheck)
        {
            _nextCheck = Time.Ticks + 200;
            UpdateRecovery(world);
        }
        return _pending != null;
    }

    internal static bool IsHammer(ushort graphic) => graphic is 0x13E3 or 0x13E4;
    internal static bool IsTongs(ushort graphic) => graphic is 0x0FBB or 0x0FBC;

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
            || !IsHammer(tool.Graphic))
            return Warn(world, "trainer_smith_tool");

        var tongs = world.Items.Get(ProfileManager.CurrentProfile.Trainer.SmeltTool);
        if (tongs == null || !IsTongs(tongs.Graphic) || (tongs.Container != player.Serial && tongs.BackpackOrRootContainer != bag.Serial))
            return Warn(world, "trainer_smith_tongs");

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
        _metalHue = best.Hue;
        _bag = bag.Serial;
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
        if (_recovery != Recovery.None || !Waiting(world) || _pending == null ||
            TargetManager.NextAutoTarget.TargetSerial == _stack)
            return false;
        bool smith = serial == _tool || serial == _stack ||
            title.Contains("smith", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("plate", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("armor", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("armour", StringComparison.OrdinalIgnoreCase);
        bool makePrompt = title.Trim().Equals("What would you like to make?", StringComparison.OrdinalIgnoreCase);
        if (!smith && !(makePrompt && _categoryDepth is 0 or 2))
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
                label is "armor" or "armour" or "armors" or "armours" ? 1 : 0;
            if (makePrompt && rank != (_categoryDepth == 0 ? 1 : 3))
                continue;
            if (rank <= best)
                continue;
            (best, index, graphic, hue) = (rank, i, g, h);
        }
        if (best == 0 || (best < 3 && ++_categoryDepth > 2))
            return false;
        if (best == 3)
        {
            _outputGraphic = graphic;
            foreach (var item in world.Items.Values)
                if (IsPlate(_pending.Name, item.Graphic, _outputGraphic))
                    ExistingPieces.Add(item.Serial);
            _stockBefore = CountMetal(world);
            _recovery = Recovery.AwaitPiece;
            _deadline = Time.Ticks + 15000;
            _nextCheck = Time.Ticks + 350;
        }
        if (gray)
            AsyncNetClient.Socket.Send_GrayMenuResponse(serial, id, (ushort)index);
        else
            AsyncNetClient.Socket.Send_MenuResponse(serial, id, index, graphic, hue);
        return true;
    }

    internal static bool IsPlate(string recipe, ushort graphic, ushort menuGraphic) =>
        (menuGraphic != 0 && graphic == menuGraphic) || recipe switch
        {
            "Female Breastplate" => graphic is 0x1C04 or 0x1C05,
            "Platemail Gorget" => graphic == 0x1413,
            "Platemail Gloves" => graphic is 0x1414 or 0x1418,
            "Plate Helm" => graphic is 0x1412 or 0x1419,
            "Platemail Arms" => graphic is 0x1410 or 0x1417,
            "Platemail Legs" => graphic is 0x1411 or 0x141A,
            "Breastplate" => graphic is 0x1415 or 0x1416,
            _ => false
        };

    private static int CountMetal(World world)
    {
        int amount = 0;
        foreach (var item in world.Items.Values)
            if (item.Graphic is >= 0x1BEF and <= 0x1BF5 && item.Hue == _metalHue && item.BackpackOrRootContainer == _bag)
                amount += item.Amount;
        return amount;
    }

    private static bool MatchesOutput(Item item) => _pending != null && item.Container == _bag && item.Hue == _metalHue &&
        !ExistingPieces.Contains(item.Serial) && IsPlate(_pending.Name, item.Graphic, _outputGraphic);

    private static GameObject? NearbyForge(World world)
    {
        var player = world.Player!;
        uint selected = ProfileManager.CurrentProfile.Trainer.Forge;
        if (selected != 0)
            return world.Items.Get(selected) is { OnGround: true, Distance: <= 2 } forge && Math.Abs(forge.Z - player.Z) <= 16 ? forge : null;
        GameObject? closest = null;
        for (int x = Math.Max(0, player.X - 2); x <= player.X + 2; x++)
            for (int y = Math.Max(0, player.Y - 2); y <= player.Y + 2; y++)
                for (var obj = world.Map.GetTile(x, y, false); obj != null; obj = obj.TNext)
                    if ((obj is Static || obj is Item { OnGround: true }) &&
                        (obj.Graphic == 0x0FB1 || obj.Graphic is >= 0x197A and <= 0x19A9) &&
                        Math.Abs(obj.Z - player.Z) <= 16 && (closest == null || obj.Distance < closest.Distance))
                        closest = obj;
        return closest;
    }

    private static void UpdateRecovery(World world)
    {
        if (_recovery == Recovery.AwaitIngots)
        {
            if (world.Items.Get(_piece) == null && CountMetal(world) > _stockAfterCraft)
                Reset();
            return;
        }
        if (_recovery != Recovery.AwaitPiece || TrainerSkillUser.CursorBusy(world))
            return;
        Item? piece = null;
        foreach (var item in world.Items.Values)
        {
            if (!MatchesOutput(item))
                continue;
            if (piece != null)
            {
                Pause(world, "trainer_smith_ambiguous");
                return;
            }
            piece = item;
        }
        if (piece == null)
            return;
        _stockAfterCraft = CountMetal(world);
        if (_stockAfterCraft > _stockBefore - _pending!.Ingots)
            return;
        uint smeltTool = ProfileManager.CurrentProfile.Trainer.SmeltTool;
        var tool = world.Items.Get(smeltTool);
        if (NearbyForge(world) == null || tool == null || !IsTongs(tool.Graphic) || (tool.Container != world.Player.Serial && tool.BackpackOrRootContainer != _bag))
        {
            Pause(world, "trainer_smith_recovery_timeout");
            return;
        }
        _piece = piece.Serial;
        _recovery = Recovery.TargetPiece;
        _deadline = Time.Ticks + 5000;
        GameActions.DoubleClick(world, tool.Serial);
    }

    private static bool HasAnotherOutput(World world)
    {
        foreach (var candidate in world.Items.Values)
            if (candidate.Serial != _piece && MatchesOutput(candidate))
                return true;
        return false;
    }

    internal static bool TryRecoveryCursor(World world, CursorTarget cursor, TargetType type)
    {
        if (_recovery is not (Recovery.TargetPiece or Recovery.TargetForge) || !Waiting(world) ||
            type != TargetType.Neutral || !TargetManager.IsLiveServerCursor(cursor, type))
            return false;
        var item = world.Items.Get(_piece);
        if (item == null || !MatchesOutput(item) || HasAnotherOutput(world))
        {
            Pause(world, "trainer_smith_ambiguous");
            return false;
        }
        if (_recovery == Recovery.TargetPiece)
        {
            _recovery = Recovery.TargetForge;
            _deadline = Time.Ticks + 5000;
            world.TargetManager.Target(_piece);
            return true;
        }
        var forge = NearbyForge(world);
        if (forge == null || (forge is Static && cursor != CursorTarget.Position))
        {
            Pause(world, "trainer_smith_recovery_timeout");
            return false;
        }
        _recovery = Recovery.AwaitIngots;
        _deadline = Time.Ticks + 10000;
        if (forge is Item dynamicForge)
            world.TargetManager.Target(dynamicForge.Serial);
        else
            world.TargetManager.Target(forge.Graphic, forge.X, forge.Y, forge.Z);
        return true;
    }

    private static void Pause(World world, string key)
    {
        Reset();
        _timedOut = true;
        Warn(world, key);
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
