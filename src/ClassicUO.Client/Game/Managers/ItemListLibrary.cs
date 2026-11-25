#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;

namespace ClassicUO.Game.Managers;

internal sealed class ItemListLibrary : JsonSave<ItemListLibrary>, INotifyPropertyChanged
{
    private static ItemListLibrary? _instance;
    public static ItemListLibrary Instance => _instance ??= Load();

    public List<SharedItemList> Lists { get; set; } = [];

    [JsonIgnore]
    internal int Revision { get; private set; }

    protected override SettingsScope Scope => SettingsScope.Global;
    protected override string FileName => "ItemLists.json";
    protected override JsonTypeInfo<ItemListLibrary> TypeInfo => ItemListJsonContext.Default.ItemListLibrary;

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
    public string RegexSearch { get; init; } = "";
    public int Priority { get; init; } = 1;
    public int MaxAmount { get; init; }
    public uint DestinationContainer { get; init; }
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
