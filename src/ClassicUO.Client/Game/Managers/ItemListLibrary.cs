#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Utility;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game.Managers;

internal sealed class ItemListLibrary : JsonSave<ItemListLibrary>, INotifyPropertyChanged
{
    private const string LibraryFileName = "ItemLists.json";
    private static ItemListLibrary? _instance;
    private bool _unavailable;
    public static ItemListLibrary Instance => _instance ??= new ItemListLibrary { _unavailable = true };

    public List<SharedItemList> Lists { get; set; } = [];

    [JsonIgnore]
    internal int Revision { get; private set; }

    [JsonIgnore]
    internal bool IsAvailable => !_unavailable;

    protected override SettingsScope Scope => SettingsScope.Char;
    protected override string FileName => LibraryFileName;
    protected override JsonTypeInfo<ItemListLibrary> TypeInfo => ItemListJsonContext.Default.ItemListLibrary;

    internal static bool InitializeProfile(string profilePath, bool importLegacy)
    {
        try
        {
            _instance = OpenProfile(profilePath, importLegacy ? JsonSaveLocationHelper.DataDirectory : null);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _instance = new ItemListLibrary { _unavailable = true };
            Log.Error($"Could not initialize item lists for '{profilePath}': {e.Message}");
            return false;
        }
    }

    internal static ItemListLibrary OpenProfile(string profilePath, string? legacyDirectory)
    {
        string path = Path.Combine(profilePath, LibraryFileName);
        var library = LoadFrom(path, legacyDirectory == null ? null : Path.Combine(legacyDirectory, LibraryFileName));
        if (!HasSavedCopy(path))
            throw new IOException($"The character's item list file '{path}' could not be saved.");

        return library;
    }

    internal static void ResetProfile() => _instance = null;

    internal ItemListLibrary? ReadPersisted() => SourcePath is { } path && File.Exists(path) ? LoadFrom(path) : null;

    public override void Save()
    {
        if (IsAvailable)
            base.Save();
    }

    public SharedItemList? Find(Guid id) => Lists?.Find(list => list?.Id == id);

    internal static SharedItemList? Resolve(Guid? id) => id is Guid listId ? Instance.Find(listId) : null;

    public SharedItemList Create(string name, IEnumerable<SharedItem>? items = null)
    {
        var list = new SharedItemList { Name = AvailableName(name) };
        if (items != null)
            list.AddItems(items);

        (Lists ??= []).Add(list);
        SaveChanges();
        return list;
    }

    public string AvailableName(string name, SharedItemList? except = null)
    {
        string baseName = string.IsNullOrWhiteSpace(name) ? "Item List" : name.Trim();
        string candidate = baseName;
        for (int suffix = 2; Lists?.Any(list => list != null && list != except &&
            string.Equals(list.Name, candidate, StringComparison.OrdinalIgnoreCase)) == true; suffix++)
            candidate = $"{baseName} ({suffix})";

        return candidate;
    }

    public void SaveChanges()
    {
        Revision++;
        Save();
    }

    // Agent-specific rules apply to the identical item when the shared list is active.
    // With a missing link, return no rules so changing a list never silently enables local rules.
    internal static IEnumerable<T> Rules<T>(Guid? id, IEnumerable<T>? local,
        Func<T, (ushort Graphic, ushort Hue)> key, Func<SharedItem, T> create) where T : class
    {
        if (id == null)
            return local ?? [];

        var list = Resolve(id);
        return list == null ? [] : list.ResolveRules(local, key, create);
    }

    internal static T? MatchRule<T>(Guid id, Item item, IEnumerable<T> local,
        Func<T, (int Graphic, ushort Hue)> key, Func<T, Item, bool> matches, Func<SharedItem, T> create) where T : class
    {
        SharedItem? shared = Instance.Find(id)?.Find(item.Graphic, item.Hue);
        if (shared == null)
            return null;

        bool hasOverride = false;
        foreach (T rule in local)
        {
            if (key(rule) != (shared.Graphic, shared.Hue))
                continue;

            hasOverride = true;
            if (matches(rule, item))
                return rule;
        }

        return hasOverride ? null : create(shared);
    }
}

internal sealed class SharedItemList
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public List<SharedItem> Items { get; set; } = [];

    internal int AddItems(IEnumerable<SharedItem> items, bool preserveRules = false)
    {
        int added = 0;
        Items ??= [];
        foreach (SharedItem item in items)
        {
            if (item == null || item.Graphic == 0 || Items.Any(existing => existing != null && existing.Graphic == item.Graphic
                && (preserveRules ? existing.Hue == item.Hue && existing.RegexSearch == item.RegexSearch
                    : existing.Hue == item.Hue || existing.Hue == ushort.MaxValue)))
                continue;

            if (!preserveRules && item.Hue == ushort.MaxValue)
                Items.RemoveAll(existing => existing?.Graphic == item.Graphic);
            Items.Add(item);
            added++;
        }
        return added;
    }

    internal SharedItem? Find(ushort graphic, ushort hue)
    {
        if (Items == null)
            return null;

        foreach (SharedItem item in Items)
            if (item != null && item.Graphic == graphic && (item.Hue == hue || item.Hue == ushort.MaxValue))
                return item;

        return null;
    }

    internal SharedItem? Find(Item item)
    {
        string? search = null;
        foreach (SharedItem rule in Items ?? [])
            if (rule != null && rule.Matches(item, ref search))
                return rule;

        return null;
    }

    internal IEnumerable<T> ResolveRules<T>(IEnumerable<T>? local, Func<T, (ushort Graphic, ushort Hue)> key,
        Func<SharedItem, T> create) where T : class
    {
        var overrides = local?.ToLookup(key);
        foreach (SharedItem item in Items ?? [])
        {
            if (item != null && item.Graphic != 0)
                yield return overrides?[(item.Graphic, item.Hue)].FirstOrDefault() ?? create(item);
        }
    }
}

internal sealed record SharedItem(ushort Graphic, ushort Hue, string Name)
{
    public string Name { get; init => field = StringHelper.RemoveBaseFontTags(value); } = StringHelper.RemoveBaseFontTags(Name);
    public string RegexSearch { get; init; } = "";
    public int Priority { get; init; } = 1;
    public int MaxAmount { get; init; }
    public uint DestinationContainer { get; init; }

    internal bool Matches(Item item)
    {
        string? search = null;
        return Matches(item, ref search);
    }

    internal bool Matches(Item item, ref string? search)
    {
        if (Graphic == 0 || (Graphic != ushort.MaxValue && Graphic != item.Graphic)
            || (Hue != ushort.MaxValue && Hue != item.Hue))
            return false;

        if (string.IsNullOrEmpty(RegexSearch))
            return true;

        search ??= (string.IsNullOrEmpty(item.OPLName) ? item.GetNormalizedName(false) : item.OPLName) + item.OPLData;
        return RegexHelper.IsMatch(RegexSearch, search, RegexOptions.Multiline);
    }
}

internal static class GatheringListRules
{
    internal static SharedItem From(AutoLootManager.AutoLootConfigEntry rule) =>
        new((ushort)rule.Graphic, rule.Hue, rule.Name)
        {
            RegexSearch = rule.RegexSearch, Priority = (int)rule.Priority,
            MaxAmount = rule.MaxAmount, DestinationContainer = rule.DestinationContainer
        };

    internal static SharedItem From(ScavengerManager.ScavengerEntry rule) =>
        new((ushort)rule.Graphic, rule.Hue, rule.Name)
        {
            RegexSearch = rule.RegexSearch, Priority = (int)rule.Priority,
            MaxAmount = rule.MaxAmount, DestinationContainer = rule.DestinationContainer
        };

    internal static Guid? Migrate(Guid? linked, string name, IEnumerable<SharedItem> oldRules)
    {
        var rules = oldRules.Where(item => item.Graphic != 0).ToArray();
        if (rules.Length == 0)
            return linked;

        var main = ItemListLibrary.Resolve(linked);
        if (linked.HasValue && main == null)
            return linked;

        var items = main == null ? rules : main.Items.SelectMany(item =>
        {
            if (item == null)
                return Array.Empty<SharedItem>();
            var overrides = rules.Where(rule => rule.Graphic == item.Graphic && rule.Hue == item.Hue).ToArray();
            return overrides.Length == 0 ? new[] { item } : overrides.Select(rule => rule with { Name = item.Name });
        }).ToArray();

        if (main != null && main.Items.SequenceEqual(items))
            return linked;

        var library = ItemListLibrary.Instance;
        var migrated = library.Lists.FirstOrDefault(list => list.Name == name && list.Items.SequenceEqual(items));
        if (migrated != null)
            return migrated.Id;

        // Preserve ordered regex variants and legacy any-graphic entries during migration.
        var created = new SharedItemList { Name = library.AvailableName(name), Items = items.ToList() };
        library.Lists.Add(created);
        library.SaveChanges();
        return created.Id;
    }
}


[JsonSerializable(typeof(List<SharedItem>))]
[JsonSerializable(typeof(ItemListLibrary), GenerationMode = JsonSourceGenerationMode.Metadata)]
internal partial class ItemListJsonContext : JsonSerializerContext;
