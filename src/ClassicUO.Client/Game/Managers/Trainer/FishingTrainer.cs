#nullable enable
using System;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Map;

namespace ClassicUO.Game.Managers.Trainer;

internal static class FishingTrainer
{
    internal const uint CastDelay = 3000;
    private static Profile? _profile;
    private static uint _pole, _player;
    private static int _facet;
    private static long _deadline, _nextCast;
    private static string? _warning;

    private static bool Carried(World world, uint pole) => world.Items.Get(pole) is { IsDestroyed: false } item &&
        (item.Container == world.Player.Serial || item.BackpackOrRootContainer == world.Player.Backpack?.Serial);

    private static (ushort X, ushort Y)? ChooseWater(World world)
    {
        (ushort, ushort)? chosen = null;
        int count = 0;
        for (int x = Math.Max(0, world.Player.X - 5); x <= world.Player.X + 5; x++)
            for (int y = Math.Max(0, world.Player.Y - 10); y < world.Player.Y; y++)
                if (TileDetectionHelper.IsWaterTile(world.Map, x, y) && Random.Shared.Next(++count) == 0)
                    chosen = ((ushort)x, (ushort)y);
        return chosen;
    }

    internal static bool Begin(World world, uint pole)
    {
        if (world.Player.IsDead || Time.Ticks < _nextCast)
            return false;
        string? warning = !Carried(world, pole) ? "trainer_fish_pole" :
            ChooseWater(world) == null ? "trainer_fish_water" : null;
        if (warning != _warning && warning != null)
            GameActions.Print(world, TazLang.Get(warning), Constants.HUE_WARN);
        _warning = warning;
        if (warning != null) return false;
        _profile = ProfileManager.CurrentProfile;
        _player = world.Player.Serial;
        _facet = world.MapIndex;
        _pole = pole;
        _deadline = Time.Ticks + 1500;
        GameActions.DoubleClick(world, pole);
        return true;
    }

    internal static bool Waiting(World world)
    {
        if (_profile == null) return false;
        if (Time.Ticks < _deadline && ReferenceEquals(_profile, ProfileManager.CurrentProfile) &&
            !world.Player.IsDead && world.Player.Serial == _player && world.MapIndex == _facet &&
            _profile.Trainer.Selected.Contains("fishing") && _profile.Trainer.Targets.TryGetValue("fishing", out var pick) &&
            pick.Serial == _pole && Carried(world, _pole))
            return true;
        Finish();
        return false;
    }

    internal static bool TryCursor(World world, CursorTarget cursor, TargetType type)
    {
        if (cursor != CursorTarget.Position || type != TargetType.Neutral || !Waiting(world))
            return false;
        var water = ChooseWater(world);
        Finish();
        if (water is { } tile)
            world.TargetManager.Target(0, tile.X, tile.Y, world.Player.Z, wet: true);
        else
            world.TargetManager.CancelTarget();
        return true;
    }

    private static void Finish()
    {
        _profile = null;
        _nextCast = Time.Ticks + CastDelay;
    }

    internal static void Reset()
    {
        _profile = null;
        _warning = null;
        _nextCast = 0;
    }
}
