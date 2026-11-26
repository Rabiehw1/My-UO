#nullable enable
using System.Linq;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.Managers.Trainer;

internal static class BeggingTrainer
{
    private static uint _vendor, _last;
    private static World? _world;
    private static Point _destination;
    private static long _deadline, _nextBeg;
    private static bool _walking, _awaitingCursor, _warned;

    internal static bool Begin(World world)
    {
        var player = world.Player;
        if (player.IsDead || player.Pathfinder.AutoWalking || Time.Ticks < _nextBeg)
            return false;
        var route = ProfileManager.CurrentProfile.Trainer.BeggingVendors;
        int start = route.FindIndex(v => v.Serial == _last) + 1;
        for (int i = 0; i < route.Count; i++)
        {
            var vendor = world.Mobiles.Get(route[(start + i) % route.Count].Serial);
            if (vendor == null || vendor.IsDestroyed || vendor.IsDead || vendor == player)
                continue;
            _last = vendor.Serial;
            if (vendor.Distance > 1 && !player.Pathfinder.WalkTo(vendor.X, vendor.Y, vendor.Z, 1))
                continue;
            _world = world;
            _vendor = vendor.Serial;
            _destination = new Point(vendor.X, vendor.Y);
            _walking = player.Pathfinder.AutoWalking;
            _deadline = Time.Ticks + 15000;
            _warned = false;
            return true;
        }
        if (!_warned)
            GameActions.Print(world, TazLang.Get("trainer_beg_missing"), Constants.HUE_WARN);
        _warned = true;
        return false;
    }

    private static bool Valid(World world) => world == _world && world.Player is { IsDead: false } &&
        ProfileManager.CurrentProfile.Trainer.Selected.Contains("begging") &&
        ProfileManager.CurrentProfile.Trainer.BeggingVendors.Any(v => v.Serial == _vendor) &&
        world.Mobiles.Get(_vendor) is { IsDead: false, IsDestroyed: false };

    internal static bool Waiting(World world)
    {
        if (_vendor == 0)
            return false;
        if (Valid(world) && Time.Ticks < _deadline)
        {
            if (_awaitingCursor)
                return true;
            var path = world.Player.Pathfinder;
            if (_walking && path.AutoWalking && path.EndPoint == _destination)
                return true;
            if (!path.AutoWalking && world.Mobiles.Get(_vendor).Distance <= 1 && !TrainerSkillUser.CursorBusy(world))
            {
                _walking = false;
                _awaitingCursor = true;
                _deadline = Time.Ticks + 4000;
                _nextBeg = Time.Ticks + 20000;
                TrainerManager.Instance.SkillSent();
                GameActions.UseSkill(6); // UO Begging skill.
                return true;
            }
        }
        Finish();
        return false;
    }

    internal static bool TryCursor(World world, TargetType type)
    {
        if (!_awaitingCursor || type != TargetType.Neutral || Time.Ticks >= _deadline ||
            !Valid(world) || world.Mobiles.Get(_vendor).Distance > 1)
            return false;
        world.TargetManager.Target(_vendor);
        _nextBeg = Time.Ticks + 20000;
        Finish();
        return true;
    }

    private static void Finish()
    {
        if (_walking && _world?.Player?.Pathfinder is { AutoWalking: true } path && path.EndPoint == _destination)
            path.StopAutoWalk();
        _walking = _awaitingCursor = false;
        _world = null;
        _vendor = 0;
    }

    internal static void Reset()
    {
        Finish();
        _warned = false;
        _last = 0;
    }
}
