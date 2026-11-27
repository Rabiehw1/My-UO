#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Gumps;

public partial class WorldMapGump
{
    private CancellationTokenSource? _boatSearch;
    private Task<List<Point>?>? _boatPlan;
    private List<Point>? _boatRoute;
    private int _boatStep;
    private int _boatManualVersion;
    private uint _boatSerial;
    private Point _boatPosition;
    private Direction? _boatDirection;
    private long _boatLastProgress;

    internal bool TrySailFromDesktop(int facet, Point goal)
    {
        if (World.Player is not { IsDrivingBoat: true, IsDead: false }
            || facet != World.MapIndex || !IsDesktopTileValid(facet, goal))
            return false;

        StopDesktopNavigation();
        var movement = World.BoatMovingManager;
        if (movement.HelmedBoat is not { } boat || boat.Facet != facet || boat.Helm != World.Player.Mount.Serial
            || !World.Items.TryGetValue(boat.Serial, out var multi) || multi.IsDestroyed
            || !World.HouseManager.TryGetHouse(boat.Serial, out var hull))
        {
            FinishNavigation(MapRouteStatus.BoatUnknown);
            return false;
        }

        // Leave room for the hull in every cardinal orientation when the boat turns.
        var footprint = new HashSet<Point>();
        foreach (var part in hull.Components.Where(part => !part.IsDestroyed))
        {
            int x = part.MultiOffsetX;
            int y = part.MultiOffsetY;
            footprint.Add(new Point(x, y));
            footprint.Add(new Point(-y, x));
            footprint.Add(new Point(-x, -y));
            footprint.Add(new Point(y, -x));
        }
        if (footprint.Count == 0)
        {
            FinishNavigation(MapRouteStatus.BoatUnknown);
            return false;
        }

        World.Player.Pathfinder.StopAutoWalk();
        movement.SendNavigationMove(Direction.North, 0);
        _boatSerial = boat.Serial;
        _boatPosition = boat.Position;
        _boatManualVersion = movement.ManualMoveVersion;
        _navigationFacet = facet;
        _navDest = goal;
        _navigationStatus = MapRouteStatus.Planning;
        _boatSearch = new CancellationTokenSource();
        var cancellation = _boatSearch.Token;
        var files = Client.Game.UO.FileManager;
        Point[] clearance = footprint.ToArray();
        _boatPlan = Task.Run(() => BoatRoutePlanner.Find(files.Maps, files.TileData, facet, boat.Position,
            goal, clearance, cancellation), cancellation);
        return true;
    }

    private void UpdateBoatNavigation()
    {
        if (_boatPlan == null && _boatRoute == null)
            return;

        var movement = World.BoatMovingManager;
        bool manual = movement.ManualMoveVersion != _boatManualVersion;
        if (manual || World.MapIndex != _navigationFacet || World.Player is not { IsDrivingBoat: true, IsDead: false }
            || movement.HelmedBoat is not { } boat || boat.Serial != _boatSerial || boat.Helm != World.Player.Mount.Serial)
        {
            StopBoatNavigation(MapRouteStatus.Idle, sendStop: !manual);
            return;
        }

        if (_boatPlan is { IsCompleted: true } plan)
        {
            _boatPlan = null;
            _boatSearch?.Dispose();
            _boatSearch = null;
            if (!plan.IsCompletedSuccessfully || plan.Result == null || boat.Position != _boatPosition)
            {
                if (plan.IsFaulted)
                    Utility.Logging.Log.Error($"Boat route search failed: {plan.Exception}");
                StopBoatNavigation(MapRouteStatus.NoBoatRoute);
                return;
            }
            _boatRoute = plan.Result;
            _navPath = [_boatPosition, .. _boatRoute];
            _navigationStatus = MapRouteStatus.Sailing;
            _boatLastProgress = Environment.TickCount64;
        }

        if (_boatRoute == null)
            return;

        if (boat.Position != _boatPosition)
        {
            // A frame can contain several movement packets. Accept skipped tiles only
            // along the current straight leg, never around a turn.
            Point heading = _boatStep < _boatRoute.Count ? _boatRoute[_boatStep] - _boatPosition : Point.Zero;
            Point cursor = _boatPosition;
            while (cursor != boat.Position && _boatStep < _boatRoute.Count
                && _boatRoute[_boatStep] - cursor == heading)
            {
                cursor = _boatRoute[_boatStep++];
            }
            if (cursor != boat.Position)
            {
                StopBoatNavigation(MapRouteStatus.BoatBlocked);
                return;
            }
            _boatPosition = boat.Position;
            _boatLastProgress = Environment.TickCount64;
        }

        if (_boatStep == _boatRoute.Count)
        {
            StopBoatNavigation(MapRouteStatus.Arrived);
            return;
        }

        if (Environment.TickCount64 - _boatLastProgress > 4000)
        {
            StopBoatNavigation(MapRouteStatus.BoatBlocked);
            return;
        }

        Point delta = _boatRoute[_boatStep] - boat.Position;
        Direction direction = delta.X switch
        {
            > 0 => Direction.East,
            < 0 => Direction.West,
            _ => delta.Y > 0 ? Direction.South : Direction.North
        };
        if (_boatDirection == direction)
            return;
        _boatDirection = direction;
        movement.SendNavigationMove(direction, 1);
    }

    private void StopBoatNavigation(MapRouteStatus status, bool sendStop = true)
    {
        if (_boatDirection.HasValue && sendStop && World.InGame && World.Player is { IsDrivingBoat: true }
            && World.BoatMovingManager.ManualMoveVersion == _boatManualVersion
            && World.BoatMovingManager.HelmedBoat is { } boat && boat.Helm == World.Player.Mount.Serial
            && boat.Serial == _boatSerial && boat.Facet == World.MapIndex)
            World.BoatMovingManager.SendNavigationMove(_boatDirection.Value, 0);

        _boatSearch?.Cancel();
        _boatSearch?.Dispose();
        _boatSearch = null;
        _boatPlan = null;
        _boatRoute = null;
        _boatDirection = null;
        _boatStep = 0;
        FinishNavigation(status);
    }
}
