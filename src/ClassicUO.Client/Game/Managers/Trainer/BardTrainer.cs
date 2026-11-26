#nullable enable
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;

namespace ClassicUO.Game.Managers.Trainer;

internal static class BardTrainer
{
    private enum Step { Idle, HerdAnimal, Destination, EnticeAnimal, ProvokeCreature, Opponent }
    private static Step _step;
    private static uint _first, _second, _herdedAnimal;
    private static TrainerSettings.HerdingTile? _tile;
    private static Profile? _profile;
    private static int _facet;
    private static long _deadline, _nextEntice, _nextProvoke;
    private static bool _herdWarned, _provokeWarned;

    private static bool Living(World world, uint serial) => world.Mobiles.Get(serial) is
        { IsDestroyed: false, IsDead: false } mobile && mobile != world.Player;

    internal static bool Begin(World world, TrainerSkill skill, uint target)
    {
        if (world.Player.IsDead) return false;
        var profile = ProfileManager.CurrentProfile;
        var settings = profile.Trainer;
        bool herd = skill.Use == TrainerUse.Entice;
        if (Time.Ticks < (herd ? _nextEntice : _nextProvoke)) return false;
        _first = target;
        _profile = profile;
        _facet = world.MapIndex;
        if (herd)
        {
            var tool = world.Items.Get(settings.HerdingTool);
            var tile = settings.HerdingDestination;
            if (!Living(world, target) || tile?.Facet != _facet || tool == null || tool.IsDestroyed ||
                (tool.Container != world.Player.Serial && tool.BackpackOrRootContainer != world.Player.Backpack?.Serial))
                return Warn(world, ref _herdWarned, "trainer_herd_setup");
            _herdWarned = false;
            bool entice = _herdedAnimal == target && tile == _tile;
            _tile = tile;
            _herdedAnimal = 0;
            Await(entice ? Step.EnticeAnimal : Step.HerdAnimal);
            _nextEntice = Time.Ticks + 10500;
            if (entice) GameActions.UseSkill(15); // Enticement (Discordance on newer clients).
            else GameActions.DoubleClick(world, tool.Serial);
        }
        else
        {
            _second = settings.ProvokeOpponent?.Serial ?? 0;
            if (_second == target || !Living(world, target) || !Living(world, _second))
                return Warn(world, ref _provokeWarned, "trainer_provoke_setup");
            _provokeWarned = false;
            Await(Step.ProvokeCreature);
            _nextProvoke = Time.Ticks + 10500;
            GameActions.UseSkill(22); // Provocation.
        }
        return true;
    }

    private static bool Warn(World world, ref bool warned, string key)
    {
        if (!warned) GameActions.Print(world, TazLang.Get(key), Constants.HUE_WARN);
        warned = true;
        return false;
    }

    private static void Await(Step step)
    {
        _step = step;
        _deadline = Time.Ticks + 3000;
    }

    internal static bool Waiting(World world)
    {
        if (_step == Step.Idle) return false;
        bool provoke = _step is Step.ProvokeCreature or Step.Opponent;
        string id = provoke ? "provocation" : "enticement";
        if (Time.Ticks < _deadline && !world.Player.IsDead && world.MapIndex == _facet &&
            _profile != null && ReferenceEquals(_profile, ProfileManager.CurrentProfile) &&
            _profile.Trainer.Selected.Contains(id) && _profile.Trainer.Targets.TryGetValue(id, out var pick) &&
            pick.Serial == _first && Living(world, _first) &&
            (provoke ? _profile.Trainer.ProvokeOpponent?.Serial == _second && Living(world, _second)
                     : _profile.Trainer.HerdingDestination == _tile))
            return true;
        _step = Step.Idle;
        return false;
    }

    internal static bool TryCursor(World world, CursorTarget cursor, TargetType type)
    {
        if (!Waiting(world)) return false;
        var step = _step;
        bool provoke = step is Step.ProvokeCreature or Step.Opponent;
        if (cursor != (step == Step.Destination ? CursorTarget.Position : CursorTarget.Object) ||
            (type != TargetType.Neutral && !(provoke && type == TargetType.Harmful)))
            return false;
        switch (step)
        {
            case Step.HerdAnimal: Await(Step.Destination); break;
            case Step.ProvokeCreature: Await(Step.Opponent); break;
            default: _step = Step.Idle; break;
        }
        if (step == Step.Destination)
        {
            var tile = _tile!;
            _herdedAnimal = _first;
            _nextEntice = Time.Ticks + 10500;
            world.TargetManager.Target(tile.Graphic, tile.X, tile.Y, tile.Z);
        }
        else
        {
            if (step == Step.EnticeAnimal) _nextEntice = Time.Ticks + 10500;
            if (step == Step.Opponent) _nextProvoke = Time.Ticks + 10500;
            world.TargetManager.Target(step == Step.Opponent ? _second : _first);
        }
        return true;
    }

    internal static void Reset()
    {
        _step = Step.Idle;
        _herdedAnimal = 0;
        _profile = null;
        _herdWarned = _provokeWarned = false;
        _nextEntice = _nextProvoke = 0;
    }
}
