#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using ClassicUO.Configuration;

namespace ClassicUO.Game.Managers;

/// <summary>
/// One named buy or sell list: the item list it trades and the player's settings for each of its items.
/// </summary>
public sealed class ShopListPreset
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    /// <summary>Item list (Lists tab) whose items this list trades.</summary>
    public Guid? ItemListId { get; set; }

    /// <summary>Trade everything available (the vendor's stock, or all you carry), ignoring each rule's max amount.</summary>
    public bool TakeAll { get; set; }

    /// <summary>Settings per item; created automatically for the item list's items.</summary>
    public List<BuySellItemConfig> Rules { get; set; } = [];

    /// <summary>Rules for the items currently on the item list, adding a rule for any item that has none yet.</summary>
    /// <returns>One rule per list item, in list order; empty when no list is chosen.</returns>
    public List<BuySellItemConfig> SyncRules()
    {
        var result = new List<BuySellItemConfig>();
        if (ItemListLibrary.Resolve(ItemListId)?.Items is not { } items)
            return result;

        foreach (SharedItem item in items)
        {
            if (item == null || item.Graphic == 0)
                continue;

            BuySellItemConfig? rule = Rules.Find(existing => existing.Graphic == item.Graphic && existing.Hue == item.Hue);
            if (rule == null)
            {
                // A new rule has no max amount: it trades all that's available until the player sets one.
                rule = new BuySellItemConfig { Graphic = item.Graphic, Hue = item.Hue, MaxAmount = ushort.MaxValue };
                Rules.Add(rule);
            }

            result.Add(rule);
        }

        return result;
    }

    /// <summary>A copy with its own ID and copies of every rule.</summary>
    /// <param name="name">Name for the copy.</param>
    /// <returns>The copy.</returns>
    public ShopListPreset Duplicate(string name) => new()
    {
        Name = name,
        ItemListId = ItemListId,
        TakeAll = TakeAll,
        Rules = Rules.Select(rule => new BuySellItemConfig
        {
            Graphic = rule.Graphic, Hue = rule.Hue, MaxAmount = rule.MaxAmount,
            RestockUpTo = rule.RestockUpTo, MaxPrice = rule.MaxPrice, Enabled = rule.Enabled
        }).ToList()
    };
}

/// <summary>The buy or the sell side's lists; only one is active at a time.</summary>
public sealed class ShopListGroup
{
    public List<ShopListPreset> Presets { get; set; } = [];

    /// <summary>The list the agent uses; null when none is active.</summary>
    public Guid? ActiveId { get; set; }

    /// <summary>The active list, if any.</summary>
    /// <returns>The list the agent uses, or null.</returns>
    public ShopListPreset? GetActive() => ActiveId is Guid id ? Presets.Find(preset => preset.Id == id) : null;

    /// <summary>The first time, turns the old single item list and rules into one active "Default" list.</summary>
    internal bool MigrateFrom(Guid? oldListId, List<BuySellItemConfig>? oldRules)
    {
        if (Presets.Count > 0)
            return false;

        var preset = new ShopListPreset
        {
            Name = TazLang.Get("shoplists_default_name"),
            ItemListId = oldListId,
            Rules = oldRules?.ToList() ?? []
        };
        Presets.Add(preset);
        ActiveId = preset.Id;
        return true;
    }
}

/// <summary>The character's buy and sell lists for the Shopping Assistant.</summary>
/// <remarks>Main thread only. Loaded and saved with the character by <see cref="BuySellAgent" />.</remarks>
public sealed class ShopListSave : JsonSave<ShopListSave>, INotifyPropertyChanged
{
    public const string FILE_NAME = "shop_lists.json";

    public ShopListGroup Buy { get; set; } = new();

    public ShopListGroup Sell { get; set; } = new();

    protected override SettingsScope Scope => SettingsScope.Char;
    protected override string FileName => FILE_NAME;
    protected override JsonTypeInfo<ShopListSave> TypeInfo => ShopListJsonContext.Default.ShopListSave;

    /// <summary>Loads the character's lists, creating each side's "Default" list from the old settings the first time.</summary>
    /// <param name="profile">The character's profile, holding the old list choices.</param>
    /// <param name="oldBuyRules">The old Auto Buy rules.</param>
    /// <param name="oldSellRules">The old Auto Sell rules.</param>
    /// <returns>The loaded or created lists.</returns>
    public static ShopListSave LoadOrMigrate(Profile? profile, List<BuySellItemConfig>? oldBuyRules, List<BuySellItemConfig>? oldSellRules)
    {
        ShopListSave save = Load();
        bool migrated = save.Buy.MigrateFrom(profile?.AutoBuySharedItemListId, oldBuyRules);
        migrated |= save.Sell.MigrateFrom(profile?.AutoSellSharedItemListId, oldSellRules);
        if (migrated)
            save.Save();
        return save;
    }
}

[JsonSerializable(typeof(ShopListSave))]
internal partial class ShopListJsonContext : JsonSerializerContext;
