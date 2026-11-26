#nullable enable
using System;
using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.Managers.Trainer;

internal static class LumberjackTrainer
{
    private enum Phase { Idle, Search, Approach, Roam, Cursor, Recover }
    private const int Radius = 6;
    private const int CooldownMs = 300000;
    private static readonly Dictionary<(int Facet, Point Tile), long> Chopped = new();
    private static readonly List<Point> Trees = new();
    private static readonly Point[] Directions = [new(0, -4), new(4, -4), new(4, 0), new(4, 4),
        new(0, 4), new(-4, 4), new(-4, 0), new(-4, -4)];
    private static Phase _phase;
    private static World? _world;
    private static Point _target, _roamStart;
    private static int _facet, _direction;
    private static uint _axe;
    private static long _deadline, _nextCheck;

    private static Static? TreeAt(World world, Point tile)
    {
        if (tile.X < 0 || tile.Y < 0)
            return null;
        for (var obj = world.Map.GetTile(tile.X, tile.Y, false); obj != null; obj = obj.TNext)
            if (obj is Static tree && StaticFilters.IsTree(tree.OriginalGraphic, out _) &&
                Math.Abs(tree.Z - world.Player.Z) <= 16)
                return tree;
        return null;
    }

    internal static bool WithinReach(int x, int y, Point tile) =>
        Math.Max(Math.Abs(x - tile.X), Math.Abs(y - tile.Y)) <= 1;

    internal static bool Begin(World world, uint axe)
    {
        if (world.Player.IsDead || world.Player.Pathfinder.AutoWalking || !Carrying(world, axe))
            return false;
        _world = world;
        _axe = axe;
        _facet = world.MapIndex;
        long now = Time.Ticks;
        foreach (var entry in Chopped)
            if (entry.Value <= now)
                Chopped.Remove(entry.Key);
        Trees.Clear();
        for (int x = world.Player.X - Radius; x <= world.Player.X + Radius; x++)
            for (int y = world.Player.Y - Radius; y <= world.Player.Y + Radius; y++)
            {
                var tile = new Point(x, y);
                if (!Chopped.ContainsKey((_facet, tile)) && TreeAt(world, tile) != null)
                    Trees.Add(tile);
            }
        Random.Shared.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Trees));
        Random.Shared.Shuffle(Directions.AsSpan());
        _direction = 0;
        _phase = Phase.Search;
        _nextCheck = now;
        return true;
    }

    private static bool Carrying(World world, uint axe) => world.Items.Get(axe) is { IsDestroyed: false } item &&
        (item.Container == world.Player.Serial || (world.Player.Backpack is { } bag && item.BackpackOrRootContainer == bag.Serial));

    private static bool Valid(World world) => ReferenceEquals(world, _world) && world.MapIndex == _facet &&
        world.Player is { IsDead: false } && ProfileManager.CurrentProfile?.Trainer is { } settings &&
        settings.Selected.Contains("lumberjacking") && settings.Targets.TryGetValue("lumberjacking", out var pick) &&
        pick.Serial == _axe && Carrying(world, _axe);

    internal static void Reset(bool clearCooldowns)
    {
        if (_phase is Phase.Approach or Phase.Roam && _world?.Player?.Pathfinder is { AutoWalking: true } path &&
            path.EndPoint == _target)
            path.StopAutoWalk();
        _phase = Phase.Idle;
        _world = null;
        if (clearCooldowns)
            Chopped.Clear();
    }

    internal static bool Waiting(World world)
    {
        if (_phase == Phase.Idle)
            return false;
        if (!Valid(world))
        {
            Reset(false);
            return false;
        }
        long now = Time.Ticks;
        if (now < _nextCheck)
            return true;
        _nextCheck = now + 200;
        var player = world.Player;
        var path = player.Pathfinder;
        if (_phase is Phase.Recover or Phase.Cursor)
        {
            if (now >= _deadline)
                Reset(false);
            return _phase != Phase.Idle;
        }
        if (_phase is Phase.Approach or Phase.Roam)
        {
            if (path.AutoWalking && path.EndPoint != _target)
            {
                Reset(false);
                return false;
            }
            if (path.AutoWalking && now < _deadline)
                return true;
            if (path.AutoWalking)
                path.StopAutoWalk();
            if (_phase == Phase.Roam && new Point(player.X, player.Y) != _roamStart)
            {
                Reset(false);
                return false;
            }
            if (_phase == Phase.Approach && WithinReach(player.X, player.Y, _target) && TreeAt(world, _target) != null)
            {
                if (TrainerSkillUser.CursorBusy(world))
                {
                    Reset(false);
                    return false;
                }
                _phase = Phase.Cursor;
                _deadline = now + 3000;
                GameActions.DoubleClick(world, _axe);
                return true;
            }
            _phase = Phase.Search;
        }
        if (path.AutoWalking)
        {
            Reset(false);
            return false;
        }
        if (Trees.Count > 0)
        {
            _target = Trees[^1];
            Trees.RemoveAt(Trees.Count - 1);
            if (TreeAt(world, _target) != null && (WithinReach(player.X, player.Y, _target) ||
                path.WalkTo(_target.X, _target.Y, player.Z, 1)))
            {
                _phase = Phase.Approach;
                _deadline = now + 8000;
            }
        }
        else if (_direction < Directions.Length)
        {
            _roamStart = new Point(player.X, player.Y);
            _target = _roamStart + Directions[_direction++];
            if (_target.X >= 0 && _target.Y >= 0 && path.WalkTo(_target.X, _target.Y, player.Z, 0))
            {
                _phase = Phase.Roam;
                _deadline = now + 5000;
            }
        }
        else
            Reset(false);
        return _phase != Phase.Idle;
    }

    internal static bool TryCursor(World world, CursorTarget cursor, TargetType type)
    {
        if (_phase != Phase.Cursor || Time.Ticks >= _deadline || !Valid(world) ||
            cursor != CursorTarget.Position || type != TargetType.Neutral ||
            !WithinReach(world.Player.X, world.Player.Y, _target) || TreeAt(world, _target) is not { } tree)
            return false;
        world.TargetManager.Target(tree.OriginalGraphic, tree.X, tree.Y, tree.Z);
        _phase = Phase.Recover;
        _deadline = Time.Ticks + 7500;
        Chopped[(_facet, _target)] = Time.Ticks + CooldownMs;
        return true;
    }
}
