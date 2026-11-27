#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Utility.Logging;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.Managers;

[Flags]
public enum MapSightingLayers
{
    None = 0,
    Npcs = 1,
    Vendors = 2,
    Monsters = 4,
    Bosses = 8,
    SuperBosses = 16
}

internal sealed record MapSighting(int Facet, int X, int Y, string Name, string Details, ushort Graphic, ushort Hue, MapSightingLayers Kind, DateTimeOffset? LastSeen = null);
internal sealed record MapTown(string Name, int Facet, int X, int Y, int Radius)
{
    internal bool Contains(int facet, int x, int y) => Facet == facet
        && Math.Abs((long)x - X) <= Radius && Math.Abs((long)y - Y) <= Radius;
}

internal sealed class MapSightingsData
{
    public List<MapSighting> Sightings { get; set; } = [];
    public List<MapTown> Towns { get; set; } = [];
    public Dictionary<string, MapSightingLayers> Tiers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

[JsonSerializable(typeof(MapSightingsData))]
internal sealed partial class MapSightingsJsonContext : JsonSerializerContext;

internal sealed class MapSightings(World world)
{
    private const int CellSize = 16;
    private static readonly string[] VendorRoles =
    [
        "vendor", "shopkeeper", "merchant", "banker", "blacksmith", "armorer", "weaponsmith",
        "alchemist", "provisioner", "innkeeper", "baker", "butcher", "carpenter", "tailor",
        "tanner", "jeweler", "fletcher", "stable master", "stablemaster", "tinker", "reagent seller"
    ];
    private static readonly string[] BossRoles = ["boss", "superboss"];
    private static readonly string[] SuperBossRoles = ["super boss", "superboss"];
    private static readonly string[] NpcRoles = ["guard", "healer", "quest giver", "town crier", "guildmaster", "guild master"];

    private static readonly string[] ProfessionRoles = new[]
    {
        "bard", "musician", "mage", "scribe", "fisherman", "miner", "lumberjack",
        "farmer", "brewer", "cook", "bowyer", "cartographer", "weaver", "beggar"
    }.Concat(VendorRoles.OrderByDescending(role => role.Length)).Concat(NpcRoles).ToArray();

    private readonly record struct LocationKey(int Facet, int X, int Y, ushort Graphic, ushort Hue, string Name, MapSightingLayers Group);
    private readonly Dictionary<LocationKey, MapSighting> _locations = new();
    private MapSightingsData _data = new();
    private string? _path;
    private bool _dirty;
    private bool _readFailed;
    private long _nextSample;
    private long _nextSave;

    internal IEnumerable<MapSighting> Visible(int facet, MapSightingLayers layers, bool currentTownOnly, Rectangle? bounds = null)
    {
        if (layers == MapSightingLayers.None)
            yield break;

        EnsureLoaded();
        currentTownOnly &= _data.Towns.Count > 0;
        MapTown? town = currentTownOnly ? CurrentTown() : null;
        foreach (MapSighting sighting in _locations.Values)
        {
            if (sighting.Facet != facet || bounds is Rectangle viewport && !viewport.Contains(sighting.X + 1, sighting.Y + 1)
                || (layers & LayerFor(sighting)) == 0)
                continue;

            if (currentTownOnly && sighting.Kind == MapSightingLayers.Vendors
                && (town == null || !town.Contains(facet, sighting.X, sighting.Y)))
                continue;

            yield return sighting;
        }
    }

    internal MapTown? CurrentTown()
    {
        EnsureLoaded();
        if (world.Player == null)
            return null;

        return _data.Towns.Where(town => town.Contains(world.MapIndex, world.Player.X, world.Player.Y))
            .MinBy(town => town.Radius);
    }

    internal IReadOnlyList<MapTown> Towns { get { EnsureLoaded(); return _data.Towns; } }

    internal bool SaveTown(string name, int radius)
    {
        EnsureLoaded();
        if (world.Player == null || !world.InGame || string.IsNullOrWhiteSpace(name) || radius is < 8 or > 512 || _readFailed)
            return false;

        name = name.Trim();
        int index = _data.Towns.FindIndex(town => town.Facet == world.MapIndex && town.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        var updated = new MapTown(name, world.MapIndex, world.Player.X, world.Player.Y, radius);
        if (index < 0)
            _data.Towns.Add(updated);
        else
            _data.Towns[index] = updated;
        _dirty = true;
        return true;
    }

    internal void RemoveTown(MapTown town)
    {
        if (_data.Towns.Remove(town))
            _dirty = true;
    }

    internal IEnumerable<(uint Serial, string Name, string Profession)> NearbyPeople()
    {
        foreach (Mobile mobile in world.Mobiles.Values.OrderBy(mobile => mobile.Distance))
        {
            if (mobile == world.Player || mobile.IsDestroyed || mobile.IsDead || mobile.IsRenamable
                || (!mobile.IsHuman && !mobile.IsGargoyle) || mobile.Distance > world.ClientViewRange)
                continue;

            world.OPL.TryGetNameAndData(mobile.Serial, out string? propertyName, out string? properties);
            string name = CleanName(string.IsNullOrWhiteSpace(mobile.Name) ? propertyName : mobile.Name);
            string details = CleanName(mobile.Title);
            string cachedProperties = CleanName(properties);
            string profession = ProfessionRoles.FirstOrDefault(role =>
                HasRole($"{details} {cachedProperties}", [role]) || HasRole(name, [role])) ?? "";
            yield return (mobile.Serial, name, profession);
        }
    }

    internal IEnumerable<string> MonsterNames()
    {
        EnsureLoaded();
        return _locations.Values.Where(sighting => IsMonsterLayer(sighting.Kind))
            .Select(sighting => sighting.Name).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase);
    }

    internal MapSightingLayers TierFor(string name) =>
        _data.Tiers.TryGetValue(name, out var tier) ? tier : MapSightingLayers.None;

    internal void SetTier(string name, MapSightingLayers tier)
    {
        EnsureLoaded();
        if (tier == MapSightingLayers.None)
            _data.Tiers.Remove(name);
        else
            _data.Tiers[name] = tier;
        _dirty = true;
    }

    internal MapSightingLayers LayerFor(MapSighting sighting)
    {
        MapSightingLayers tier = IsMonsterLayer(sighting.Kind) ? TierFor(sighting.Name) : MapSightingLayers.None;
        return tier == MapSightingLayers.None ? sighting.Kind : tier;
    }

    internal void Update()
    {
        long now = Environment.TickCount64;
        if (now >= _nextSample && world.InGame && ProfileManager.CurrentProfile?.WorldMapRecordSightings == true)
        {
            _nextSample = now + 1000;
            EnsureLoaded();
            foreach (Mobile mobile in world.Mobiles.Values)
                Observe(mobile, confirmedVendor: false);
        }

        if (_dirty && now >= _nextSave)
        {
            _nextSave = now + 20000;
            Flush();
        }
    }

    internal void RecordVendor(Mobile mobile)
    {
        if (world.InGame && ProfileManager.CurrentProfile?.WorldMapRecordSightings == true)
        {
            EnsureLoaded();
            Observe(mobile, confirmedVendor: true);
        }
    }

    private void Observe(Mobile mobile, bool confirmedVendor)
    {
        if (mobile == world.Player || mobile.IsPlayer || mobile.InParty || mobile.IsRenamable || mobile.IsDead
            || mobile.IsDestroyed || mobile.Distance > world.ClientViewRange)
            return;

        world.OPL.TryGetNameAndData(mobile.Serial, out string? propertyName, out string? properties);
        string name = CleanName(string.IsNullOrWhiteSpace(mobile.Name) ? propertyName : mobile.Name);
        string details = CleanName(string.IsNullOrWhiteSpace(mobile.Title) ? properties : mobile.Title);
        string classification = $"{name} {details}";
        var knownCreature = WeaponSwitchCatalog.Find(mobile.Graphic, mobile.Hue);
        bool vendor = confirmedVendor || mobile.IsHuman && HasRole(classification, VendorRoles);
        MapSightingLayers kind;
        if (vendor)
            kind = MapSightingLayers.Vendors;
        else if (HasRole(classification, BossRoles))
            kind = MonsterTier(classification);
        else if (knownCreature != null && (!mobile.IsHuman
            || knownCreature.Name.Split(" / ").Any(candidate => HasRole(classification, [candidate]))))
            kind = MapSightingLayers.Monsters;
        else if (HasRole(classification, NpcRoles) || mobile.IsHuman && mobile.NotorietyFlag == NotorietyFlag.Invulnerable)
            kind = MapSightingLayers.Npcs;
        else if (!mobile.IsHuman)
            kind = MapSightingLayers.Monsters;
        else
            return;

        if (name.Length == 0)
        {
            if (!IsMonsterLayer(kind))
                return;

            name = knownCreature?.Name ?? $"Creature 0x{mobile.Graphic:X4}";
        }

        Add(new MapSighting(world.MapIndex, mobile.X, mobile.Y, name, details, mobile.Graphic, mobile.Hue, kind));
    }

    internal void RecordShop(int facet, int x, int y, string? name)
    {
        if (!world.InGame || ProfileManager.CurrentProfile?.WorldMapRecordSightings != true)
            return;

        EnsureLoaded();
        Add(new MapSighting(facet, x, y, CleanName(name) is { Length: > 0 } label ? label : "Shop",
            "Shop waypoint", 0, 0, MapSightingLayers.Vendors));
    }

    private void Add(MapSighting sighting)
    {
        if (_path == null || _readFailed)
            return;

        sighting = sighting with { LastSeen = DateTimeOffset.UtcNow };
        LocationKey key = KeyFor(sighting);
        if (_locations.TryGetValue(key, out var existing))
        {
            // Keep a stable pin for moving creatures and upgrade details when richer data arrives.
            if (Rank(sighting.Kind) > Rank(existing.Kind)
                || sighting.Details.Length > existing.Details.Length
                || !existing.LastSeen.HasValue || sighting.LastSeen.Value - existing.LastSeen.Value >= TimeSpan.FromMinutes(5))
            {
                _locations[key] = sighting with
                {
                    X = existing.X, Y = existing.Y,
                    Kind = Rank(sighting.Kind) >= Rank(existing.Kind) ? sighting.Kind : existing.Kind,
                    Details = sighting.Details.Length >= existing.Details.Length ? sighting.Details : existing.Details
                };
                _dirty = true;
            }
        }
        else
        {
            _locations.Add(key, sighting);
            _dirty = true;
        }
    }

    private static LocationKey KeyFor(MapSighting sighting) => new(sighting.Facet, sighting.X / CellSize, sighting.Y / CellSize,
        sighting.Graphic, sighting.Hue, sighting.Name.ToUpperInvariant(),
        IsMonsterLayer(sighting.Kind) ? MapSightingLayers.Monsters : MapSightingLayers.Npcs);

    private static bool IsMonsterLayer(MapSightingLayers kind) =>
        kind is MapSightingLayers.Monsters or MapSightingLayers.Bosses or MapSightingLayers.SuperBosses;

    private static int Rank(MapSightingLayers kind) => kind switch
    {
        MapSightingLayers.Vendors or MapSightingLayers.Bosses => 1,
        MapSightingLayers.SuperBosses => 2,
        _ => 0
    };

    private static MapSightingLayers MonsterTier(string text) =>
        HasRole(text, SuperBossRoles) ? MapSightingLayers.SuperBosses : MapSightingLayers.Bosses;

    private static bool HasRole(string text, string[] roles)
    {
        foreach (string role in roles)
        {
            int start = 0;
            while ((start = text.IndexOf(role, start, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                int end = start + role.Length;
                if ((start == 0 || !char.IsLetterOrDigit(text[start - 1]))
                    && (end == text.Length || !char.IsLetterOrDigit(text[end])))
                    return true;
                start = end;
            }
        }
        return false;
    }

    private static string CleanName(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        ReadOnlySpan<char> value = text.AsSpan().Trim();
        int lineEnd = value.IndexOfAny('\r', '\n');
        if (lineEnd >= 0)
            value = value[..lineEnd];
        return value[..Math.Min(100, value.Length)].ToString();
    }

    private void EnsureLoaded()
    {
        if (_path != null || string.IsNullOrEmpty(ProfileManager.ProfilePath))
            return;

        _path = Path.Combine(ProfileManager.ProfilePath, "map-sightings.json");
        try
        {
            if (!File.Exists(_path))
                return;

            var loaded = JsonSerializer.Deserialize(File.ReadAllText(_path), MapSightingsJsonContext.Default.MapSightingsData);
            if (loaded?.Sightings == null || loaded.Towns == null || loaded.Tiers == null)
                throw new JsonException("Incomplete map sightings file.");

            foreach (MapSighting sighting in loaded.Sightings)
                if (sighting != null && !string.IsNullOrWhiteSpace(sighting.Name))
                    _locations.TryAdd(KeyFor(sighting), sighting);
            loaded.Sightings.Clear();
            loaded.Towns.RemoveAll(town => town == null || string.IsNullOrWhiteSpace(town.Name));
            loaded.Tiers = new Dictionary<string, MapSightingLayers>(loaded.Tiers, StringComparer.OrdinalIgnoreCase);
            _data = loaded;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _readFailed = true;
            Log.Error($"Could not read map sightings: {ex}");
        }
    }

    internal void Flush()
    {
        if (!_dirty || _path == null || _readFailed)
            return;

        string temporary = _path + ".tmp";
        try
        {
            _data.Sightings = _locations.Values.ToList();
            File.WriteAllText(temporary, JsonSerializer.Serialize(_data, MapSightingsJsonContext.Default.MapSightingsData));
            File.Move(temporary, _path, true);
            _dirty = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Could not save map sightings: {ex}");
        }
        finally
        {
            _data.Sightings.Clear();
        }
    }

    internal void Clear()
    {
        Flush();
        _locations.Clear();
        _data = new();
        _path = null;
        _dirty = false;
        _readFailed = false;
        _nextSample = _nextSave = 0;
    }
}
