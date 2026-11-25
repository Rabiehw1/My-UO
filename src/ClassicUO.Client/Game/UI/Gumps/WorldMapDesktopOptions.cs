using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClassicUO.Assets;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Input;
using ClassicUO.Renderer;
using ClassicUO.Utility;
using ClassicUO.Utility.Logging;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Gumps;

public partial class WorldMapGump
{
    private static int _desktopTextureFacet;
    private Point? _desktopPointer;
    private int _navVersion;

    private Point? MapHoverPointer => IsDetached ? _desktopPointer : Mouse.Position;

    internal void SetDesktopPointer(Point? pointer) => _desktopPointer = pointer;

    private bool TryGetMapPointer(out Point tile)
    {
        tile = default;
        if (IsDetached)
        {
            return _desktopPointer is Point pointer
                && TryGetDesktopPosition(new Vector2(_center.X, _center.Y), _zoomIndex,
                    new Vector2(pointer.X - Width / 2, pointer.Y - Height / 2), out tile);
        }

        if (_lastMousePosition is not Point mouse)
            return false;

        CanvasToWorld(mouse.X, mouse.Y, out int x, out int y);
        tile = new Point(x, y);
        return true;
    }

    private void DrawSightings(UltimaBatcher2D batcher, Rectangle bounds, int x, int y, int halfWidth, int halfHeight)
    {
        var profile = ProfileManager.CurrentProfile;
        if (profile == null || profile.WorldMapSightingLayers == MapSightingLayers.None)
            return;

        var sightings = World.MapSightings;
        MapSighting hovered = null;
        var marker = new WMapMarker();
        foreach (MapSighting sighting in sightings.Visible(_map.Index, profile.WorldMapSightingLayers,
                     profile.WorldMapVendorsCurrentTownOnly, bounds))
        {
            marker.X = sighting.X;
            marker.Y = sighting.Y;
            marker.MapId = sighting.Facet;
            marker.Name = sighting.Name;
            marker.Color = sightings.LayerFor(sighting) switch
            {
                MapSightingLayers.Npcs => Color.DeepSkyBlue,
                MapSightingLayers.Vendors => Color.LimeGreen,
                MapSightingLayers.Bosses => Color.Orange,
                MapSightingLayers.SuperBosses => Color.Magenta,
                _ => Color.Red
            };
            if (DrawMarker(batcher, marker, x, y, halfWidth, halfHeight, Zoom, hoverOnly: true))
                hovered = sighting;
        }
        if (hovered != null)
        {
            marker.X = hovered.X;
            marker.Y = hovered.Y;
            marker.Name = string.IsNullOrEmpty(hovered.Details) || hovered.Name.Equals(hovered.Details, StringComparison.Ordinal)
                ? hovered.Name : $"{hovered.Name} · {hovered.Details}";
            DrawMarkerString(batcher, marker, x, y, halfWidth, halfHeight);
        }
    }

    internal IEnumerable<int> DesktopFacets()
    {
        var maps = Client.Game.UO.FileManager.Maps;
        for (int i = 0; i < MapLoader.MAPS_COUNT; i++)
            if (maps.GetMapFile(i) != null)
                yield return i;
    }

    internal bool IsDesktopTileValid(int facet, Point tile)
    {
        var sizes = Client.Game.UO.FileManager.Maps.MapsDefaultSize;
        return facet >= 0 && facet < sizes.GetLength(0)
            && tile.X >= 0 && tile.X < sizes[facet, 0] && tile.Y >= 0 && tile.Y < sizes[facet, 1];
    }

    internal Point DesktopFacetCenter(int facet)
    {
        var sizes = Client.Game.UO.FileManager.Maps.MapsDefaultSize;
        return new Point(sizes[facet, 0] / 2, sizes[facet, 1] / 2);
    }

    internal IEnumerable<(uint Serial, string Name)> DesktopFollowTargets()
    {
        return (World.Party?.Members ?? [])
            .Where(member => member != null && SerialHelper.IsValid(member.Serial) && member.Serial != World.Player.Serial)
            .Select(member => (member.Serial, member.Name ?? $"0x{member.Serial:X8}"));
    }

    internal Point? DesktopPartyPosition(uint serial)
    {
        Mobile mobile = World.Mobiles.Get(serial);
        if (mobile != null && mobile.Distance <= World.ClientViewRange)
            return new Point(mobile.X, mobile.Y);

        var entity = World.WMapManager.GetEntity(serial);
        return entity != null && entity.Map == World.MapIndex ? new Point(entity.X, entity.Y) : null;
    }

    internal bool TryTargetDesktopTile(Point tile)
    {
        if (!_allowPositionalTarget || _map.Index != World.MapIndex || !IsDesktopTileValid(_map.Index, tile)
            || !World.TargetManager.IsTargeting
            || World.TargetManager.TargetingState is not (CursorTarget.Position or CursorTarget.Internal))
            return false;

        World.TargetManager.Target(0, (ushort)tile.X, (ushort)tile.Y, _map.GetTileZ(tile.X, tile.Y));
        return true;
    }

    internal void SetDesktopDestination(int facet, Point tile) => _gotoMarker = new WMapMarker
    {
        X = tile.X, Y = tile.Y, MapId = facet, Color = Color.Aquamarine,
        Name = $"Go to: {tile.X}, {tile.Y}", ZoomIndex = 1
    };

    internal bool TryParseDesktopLocation(string text, int facet, out Point tile)
    {
        tile = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string[] parts = text.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y))
            tile = new Point(x, y);
        else if (_map.Index != facet || !Sextant.Parse(_map, text, out tile))
            return false;

        return IsDesktopTileValid(facet, tile);
    }

    internal void StopDesktopNavigation()
    {
        _navVersion++;
        var pathfinder = World.Player?.Pathfinder;
        if (pathfinder != null && _navStepFailedHandler != null)
            pathfinder.OnComputedPathStepFailed -= _navStepFailedHandler;
        if (_navDest.HasValue)
        {
            WorldMapPathfinder.Cancel();
            pathfinder?.StopAutoWalk();
        }
        _navStepFailedHandler = null;
        _navDest = null;
        _navPath = null;
        _navSegments = 0;
    }

    internal IEnumerable<(string Label, bool Enabled, Action<bool> Set)> DesktopDisplayOptions()
    {
        yield return ("Rotate map", _flipMap, value => { _flipMap = value; OnResize(); });
        yield return ("Grid at high zoom", _showGridIfZoomed, value => _showGridIfZoomed = value);
        yield return ("Markers", _showMarkers, value => _showMarkers = value);
        yield return ("Always show markers", _alwaysShowMarkers, value => _alwaysShowMarkers = value);
        yield return ("Marker names", _showMarkerNames, value => _showMarkerNames = value);
        yield return ("Marker icons", _showMarkerIcons, value => _showMarkerIcons = value);
        yield return ("Party members", _showPartyMembers, value => _showPartyMembers = value);
        yield return ("Nearby mobiles", _showMobiles, value => _showMobiles = value);
        yield return ("My corpse", _showCorpse, value => _showCorpse = value);
        yield return ("Houses and boats", _showMultis, value => _showMultis = value);
        yield return ("Resurrection waypoints", _showResurrectionWaypoints, value => _showResurrectionWaypoints = value);
        yield return ("Player name", _showPlayerName, value => _showPlayerName = value);
        yield return ("Player health bar", _showPlayerBar, value => _showPlayerBar = value);
        yield return ("Group names", _showGroupName, value => _showGroupName = value);
        yield return ("Group health bars", _showGroupBar, value => _showGroupBar = value);
        yield return ("Player coordinates", _showCoordinates, value => _showCoordinates = value);
        yield return ("Cursor coordinates", _showMouseCoordinates, value => _showMouseCoordinates = value);
        yield return ("Sextant coordinates", _showSextantCoordinates, value => _showSextantCoordinates = value);
        yield return ("Allow tile targeting", _allowPositionalTarget, value => _allowPositionalTarget = value);
    }

    internal int DesktopSpriteFont => _markerFontIndex;
    internal string DesktopTtfFont => _ttfFont;
    internal int DesktopTtfSize => _ttfFontSize;
    internal void SetDesktopSpriteFont(int index) { SetFont(index); SaveSettings(); }
    internal void SetDesktopTtfFont(string name) => SetTtfFont(name);
    internal void SetDesktopTtfSize(int size) => AdjustTtfFontSize(Math.Clamp(size, TTF_FONT_SIZE_MIN, TTF_FONT_SIZE_MAX) - _ttfFontSize);

    internal IEnumerable<(string Label, bool Enabled, Action<bool> Set)> DesktopFileOptions(bool zones)
    {
        if (zones)
        {
            foreach (var (file, zone) in _zoneSets.ZoneSetDict)
                yield return (zone.NiceFileName, !zone.Hidden, enabled =>
                {
                    zone.Hidden = !enabled;
                    SetFileVisibility(_hiddenZoneFiles, file, enabled);
                });
        }
        else
        {
            foreach (var file in _markerFiles)
                yield return (file.Name, !file.Hidden, enabled =>
                {
                    file.Hidden = !enabled;
                    SetFileVisibility(_hiddenMarkerFiles, file.Name, enabled);
                });
        }
    }

    private void SetFileVisibility(List<string> hidden, string name, bool enabled)
    {
        hidden.RemoveAll(file => file == name);
        if (!enabled)
            hidden.Add(name);
        SaveSettings();
    }

    internal void ReloadDesktopZones() => LoadZones();

    internal bool TryImportMarkerFile(string source)
    {
        try
        {
            string extension = Path.GetExtension(source);
            if (!File.Exists(source) || !new[] { ".map", ".csv", ".xml" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
                return false;

            string directory = Path.Combine(ShardMapDirectory, "MapMarkers");
            Directory.CreateDirectory(directory);
            string destination = Path.Combine(directory, Path.GetFileName(source));
            if (!string.Equals(Path.GetFullPath(source), destination, StringComparison.OrdinalIgnoreCase))
                File.Copy(source, destination, overwrite: true);

            LoadMarkers();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.Error($"Could not import map marker file: {ex}");
            return false;
        }
    }

    internal bool TryResetDesktopCache()
    {
        if (_loadingTask is { IsCompleted: false })
            return false;

        try
        {
            lock (Map.Map.GetMapPngLock())
            {
                if (Directory.Exists(_mapsCachePath))
                    foreach (string path in Directory.EnumerateFiles(_mapsCachePath, "*.png"))
                        File.Delete(path);
            }
            ChangeMap(_map.Index);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Could not reset map cache: {ex}");
            return false;
        }
    }
}
