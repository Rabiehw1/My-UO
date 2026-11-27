#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.IO;
using ClassicUO.Network;

namespace ClassicUO.Game.Managers.Trainer;

/// <summary>One craft and unstitch cycle on the main game thread.</summary>
internal static class TailoringTrainer
{
    private enum Step { Idle, LeatherCursor, Category, Recipe, Craft, UnstitchCursor, Recover }
    private static Step _step;
    private static readonly HashSet<uint> Existing = new();
    private static Profile? _profile;
    private static bool _paused;
    private static uint _tool, _leather, _unstitch, _piece, _player;
    private static int _initialAmount;
    private static long _ready, _deadline;
    private static string? _warning;

    private static string[] _path = [];
    private static int _menuIndex;
    private static ushort _menuGraphic, _hue;

    // Zulu Hotel Omega Info Vault: Tailoring (leather armor) and Hides.
    internal sealed record Recipe(string Name, int Skill, int Cost);
    private static readonly Recipe[] Armor =
    [
        new("Leather Cap", 17, 2), new("Leather Gloves", 62, 3), new("Leather Gorget", 64, 4),
        new("Leather Skirt", 68, 6), new("Leather Shorts", 72, 8), new("Leather Leggings", 76, 10),
        new("Leather Tunic", 80, 12), new("Studded Gorget", 89, 6), new("Studded Gloves", 93, 8),
        new("Studded Sleeves", 97, 10), new("Studded Leggings", 101, 12), new("Studded Tunic", 105, 14)
    ];
    // Shard crafting menu measurements. Specialty thresholds are recipe specific.
    private static readonly Dictionary<ushort, (int Leggings, int Tunic)> StuddedMinimums = new()
    {
        [0x048D] = (150, 150),
        [0x0485] = (142, 146),
        [0x0030] = (144, 148),
        [0x0000] = (101, 105),
        [0x0492] = (137, 141),
        [0x0486] = (131, 135),
        [0x0496] = (134, 138),
        [0x0852] = (117, 121),
        [0x0054] = (127, 131),
        [0x041E] = (119, 123),
        [0x0415] = (126, 130),
        [0x07E2] = (104, 108),
        [0x08FD] = (114, 118),
        [0x0482] = (149, 150),
        [0x0505] = (124, 128),
        [0x044E] = (107, 111),
        [0x0487] = (141, 145),
        [0x0494] = (139, 143),
        [0x066D] = (136, 140),
        [0x0488] = (146, 150),
        [0x054A] = (121, 125),
    };

    private static int Minimum(Recipe recipe, ushort hue)
    {
        if (hue == 0)
            return recipe.Skill;

        if (!StuddedMinimums.TryGetValue(hue, out var measured))
            return -1;

        return recipe.Name switch
        {
            "Studded Leggings" => measured.Leggings,
            "Studded Tunic" => measured.Tunic,
            _ => -1
        };
    }

    internal static Recipe? SelectRecipe(int skillFixed, ushort hue, int amount)
    {
        Recipe? best = null;
        int highest = -1;
        foreach (var recipe in Armor)
        {
            int minimum = Minimum(recipe, hue);
            if (minimum < 0 || minimum * 10 > skillFixed || recipe.Cost > amount || minimum <= highest)
                continue;

            best = recipe;
            highest = minimum;
        }

        return best;
    }

    internal static bool MatchesOutput(ushort graphic, ushort hue, string name, ushort menuGraphic,
        ushort materialHue, string recipe) => hue == materialHue && (menuGraphic != 0
            ? graphic == menuGraphic : name.Trim().Equals(recipe, StringComparison.OrdinalIgnoreCase));

    private static Item? Carried(World world, uint serial) =>
        world.Items.Get(serial) is { IsDestroyed: false } item && (item.Container == world.Player.Serial ||
        world.Player.Backpack is { } bag && item.BackpackOrRootContainer == bag.Serial) ? item : null;

    internal static bool Begin(World world, uint tool)
    {
        if (_paused || world.Player.IsDead)
            return false;
        var profile = ProfileManager.CurrentProfile;
        var settings = profile.Trainer;
        if (Carried(world, tool) == null || Carried(world, settings.UnstitchTool) == null)
            return Warn(world, "trainer_tailor_setup");
        var bag = world.Player.Backpack;
        int skill = world.Player.Skills?.FirstOrDefault(s => s?.Index == 34)?.ValueFixed ?? 0;
        Item? leather = null;
        Recipe? best = null;
        int highest = -1;
        foreach (var stack in world.Items.Values)
        {
            if (bag == null || stack.IsDestroyed || stack.BackpackOrRootContainer != bag.Serial ||
                stack.Graphic is not (0x1067 or 0x1068 or 0x1078 or 0x1079))
                continue;
            var recipe = SelectRecipe(skill, stack.Hue, stack.Amount);
            if (recipe == null)
                continue;
            int score = Minimum(recipe, stack.Hue);
            if (score < highest || (score == highest && best != null &&
                (recipe.Skill < best.Skill || (recipe.Skill == best.Skill && stack.Serial >= leather!.Serial))))
                continue;
            highest = score;
            leather = stack;
            best = recipe;
        }
        if (leather == null || best == null)
            return Warn(world, "trainer_tailor_material");
        _path = [best.Name.StartsWith("Studded", StringComparison.Ordinal) ? "Studded Leather Armors" : "Leather Armors", best.Name];
        _menuIndex = 0;
        _hue = leather.Hue;
        _profile = profile;
        _player = world.Player.Serial;
        _tool = tool;
        _leather = leather.Serial;
        _unstitch = settings.UnstitchTool;
        _warning = null;
        Advance(Step.LeatherCursor);
        GameActions.DoubleClick(world, tool);
        return true;
    }

    private static bool Warn(World world, string key)
    {
        if (_warning != key)
            GameActions.Print(world, TazLang.Get(key), Constants.HUE_WARN);
        _warning = key;
        return false;
    }

    private static void Advance(Step step, int delay = 0)
    {
        _step = step;
        _ready = Time.Ticks + delay;
        _deadline = _ready + 5000;
    }

    private static bool Current(World world) => world.Player is { IsDead: false } p && p.Serial == _player &&
        _profile != null && ReferenceEquals(_profile, ProfileManager.CurrentProfile);

    internal static void Reset()
    {
        _paused = false;
        _warning = null;
        _profile = null;
        _step = Step.Idle;
        Existing.Clear();
    }

    private static void Pause(World world)
    {
        Reset();
        _paused = true;
        GameActions.Print(world, TazLang.Get("trainer_tailor_pause"), Constants.HUE_WARN);
    }

    private static bool Active(World world)
    {
        if (_step == Step.Idle)
            return false;
        if (!Current(world) || !_profile!.Trainer.Selected.Contains("tailoring"))
            Reset();
        else if (Time.Ticks >= _deadline)
            Pause(world);
        return _step != Step.Idle;
    }

    internal static bool TryMenu(World world, uint serial, ushort id, string title, int count, StackDataReader entries)
    {
        if (_step is not (Step.Category or Step.Recipe) || !Active(world))
            return false;
        if (!title.Trim().Equals("What would you like to make?", StringComparison.OrdinalIgnoreCase))
            return false;
        bool gray = false;
        for (ushort index = 1; index <= count; index++)
        {
            ushort graphic = entries.ReadUInt16BE(), hue = entries.ReadUInt16BE();
            if (index == 1)
                gray = graphic == 0;
            if (!entries.ReadASCII(entries.ReadUInt8()).Trim().Equals(_path[_menuIndex], StringComparison.OrdinalIgnoreCase))
                continue;
            if (++_menuIndex < _path.Length)
                Advance(Step.Recipe);
            else
            {
                _menuGraphic = graphic;
                foreach (var item in world.Items.Values)
                    Existing.Add(item.Serial);
                _initialAmount = Carried(world, _leather)?.Amount ?? 0;
                Advance(Step.Craft, 4000);
            }
            if (gray)
                AsyncNetClient.Socket.Send_GrayMenuResponse(serial, id, index);
            else
                AsyncNetClient.Socket.Send_MenuResponse(serial, id, index, graphic, hue);
            return true;
        }
        return false;
    }

    private static uint NewPiece(World world)
    {
        uint found = 0;
        foreach (var item in world.Items.Values)
        {
            if (Existing.Contains(item.Serial) || Carried(world, item.Serial) == null ||
                !MatchesOutput(item.Graphic, item.Hue, _menuGraphic == 0 ? item.GetNormalizedName(false) : "",
                    _menuGraphic, _hue, _path[^1]))
                continue;
            if (found != 0)
                return 0;
            found = item.Serial;
        }
        return found;
    }

    internal static bool TryCursor(World world, CursorTarget cursor, TargetType type)
    {
        if (_step is not (Step.LeatherCursor or Step.UnstitchCursor) || type != TargetType.Neutral ||
            !TargetManager.IsLiveServerCursor(cursor, type) || !Active(world))
            return false;
        bool crafting = _step == Step.LeatherCursor;
        uint target = crafting ? _leather : _piece;
        if (Carried(world, target) == null || (!crafting && NewPiece(world) != target))
        {
            Pause(world);
            return false;
        }
        if (crafting)
            Advance(Step.Category);
        else
            Advance(Step.Recover, 4500);
        world.TargetManager.Target(target);
        return true;
    }

    internal static bool Waiting(World world)
    {
        if (!Active(world))
            return false;
        if (Time.Ticks < _ready)
            return true;
        _ready = Time.Ticks + 200;
        switch (_step)
        {
            case Step.Craft:
                _piece = NewPiece(world);
                if (_piece == 0 || (Carried(world, _leather)?.Amount ?? 0) >= _initialAmount)
                    break;
                if (TrainerSkillUser.CursorBusy(world) || Carried(world, _unstitch) == null)
                    break;
                Advance(Step.UnstitchCursor);
                GameActions.DoubleClick(world, _unstitch);
                break;
            case Step.Recover:
                if (world.Items.Get(_piece) != null)
                    break;
                Reset();
                break;
        }
        return _step != Step.Idle;
    }
}
