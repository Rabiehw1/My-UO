#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.IO;
using ClassicUO.Network;

namespace ClassicUO.Game.Managers.Trainer;

/// <summary>One craft, dress, unstitch and undress cycle on the main game thread.</summary>
internal static class TailoringTrainer
{
    private enum Step { Idle, LeatherCursor, Category, Recipe, Craft, Dress, UnstitchCursor, Recover, Undress, Finish }
    private static Step _step;
    private static readonly HashSet<uint> Existing = new();
    private static Profile? _profile;
    private static DressConfig? _outfit;
    private static bool _dressed, _paused;
    private static uint _tool, _leather, _unstitch, _piece, _player;
    private static int _initialAmount;
    private static long _ready, _deadline;
    private static string? _warning;

    private static bool _tunic;
    private static ushort _hue;

    internal static bool IsOutput(ushort graphic, ushort hue, bool tunic, ushort leatherHue) =>
        hue == leatherHue && (tunic ? graphic is 0x13DB or 0x13E2 : graphic is 0x13DA or 0x13E1);

    internal static bool MatchesMenu(bool category, string title, string entry, bool tunic) =>
        title.Trim().Equals("What would you like to make?", StringComparison.OrdinalIgnoreCase) &&
        entry.Trim().Equals(category ? "Studded Leather Armors" : tunic ? "Studded Tunic" : "Studded Leggings", StringComparison.OrdinalIgnoreCase);

    private static Item? Carried(World world, uint serial) =>
        world.Items.Get(serial) is { IsDestroyed: false } item && (item.Container == world.Player.Serial ||
        world.Player.Backpack is { } bag && item.BackpackOrRootContainer == bag.Serial) ? item : null;

    internal static bool Begin(World world, uint tool)
    {
        if (_paused || world.Player.IsDead)
            return false;
        var profile = ProfileManager.CurrentProfile;
        var settings = profile.Trainer;
        var leather = Carried(world, settings.DragonLeather);
        var outfit = DressAgentManager.Instance.CurrentPlayerConfigs.Find(c => c.Name == settings.TailorOutfit);
        if (Carried(world, tool) == null || Carried(world, settings.UnstitchTool) == null ||
            leather == null || leather.Amount < (settings.TailorStuddedTunic ? 14 : 12) ||
            (leather.Graphic is not (0x1067 or 0x1068 or 0x1078 or 0x1079) &&
             !leather.GetNormalizedName(false).Contains("leather", StringComparison.OrdinalIgnoreCase) &&
             !leather.GetNormalizedName(false).Contains("hide", StringComparison.OrdinalIgnoreCase)) || outfit == null || outfit.Items.Count == 0)
        {
            if (_warning == null)
                GameActions.Print(world, TazLang.Get("trainer_tailor_setup"), Constants.HUE_WARN);
            _warning = "setup";
            return false;
        }
        _tunic = settings.TailorStuddedTunic;
        _hue = leather.Hue;
        _profile = profile;
        _player = world.Player.Serial;
        _outfit = outfit;
        _tool = tool;
        _leather = settings.DragonLeather;
        _unstitch = settings.UnstitchTool;
        _warning = null;
        Advance(Step.LeatherCursor);
        GameActions.DoubleClick(world, tool);
        return true;
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
        if (_dressed && World.Instance is { } world && Current(world) && _outfit != null)
            DressAgentManager.Instance.UndressFromConfig(_outfit);
        _dressed = _paused = false;
        _warning = null;
        _outfit = null;
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
        bool category = _step == Step.Category;
        bool gray = false;
        for (ushort index = 1; index <= count; index++)
        {
            ushort graphic = entries.ReadUInt16BE(), hue = entries.ReadUInt16BE();
            if (index == 1)
                gray = graphic == 0;
            if (!MatchesMenu(category, title, entries.ReadASCII(entries.ReadUInt8()), _tunic))
                continue;
            if (category)
                Advance(Step.Recipe);
            else
            {
                foreach (var item in world.Items.Values)
                    if (IsOutput(item.Graphic, item.Hue, _tunic, _hue))
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
            if (!IsOutput(item.Graphic, item.Hue, _tunic, _hue) || Existing.Contains(item.Serial) || Carried(world, item.Serial) == null)
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
                DressAgentManager.Instance.DressFromConfig(_outfit!);
                _dressed = true;
                Advance(Step.Dress, 5000);
                break;
            case Step.Dress:
                if (TrainerSkillUser.CursorBusy(world) || Carried(world, _unstitch) == null ||
                    !_outfit!.Items.All(i => world.Items.Get(i.Serial)?.Container == _player))
                    break;
                Advance(Step.UnstitchCursor);
                GameActions.DoubleClick(world, _unstitch);
                break;
            case Step.Recover:
                if (world.Items.Get(_piece) != null)
                    break;
                DressAgentManager.Instance.UndressFromConfig(_outfit!);
                _dressed = false;
                Advance(Step.Undress, 5000);
                break;
            case Step.Undress:
                if (_outfit!.Items.Any(i => world.Items.Get(i.Serial)?.Container == _player))
                    break;
                if (Carried(world, _leather) != null)
                    GameActions.SingleClick(world, _leather);
                Advance(Step.Finish, 500);
                break;
            case Step.Finish:
                Reset();
                break;
        }
        return _step != Step.Idle;
    }
}
