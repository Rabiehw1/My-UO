#nullable enable

using System;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game.Managers;

/// <summary>Records the client's inventory and HP observations after requests for configured heal potions.</summary>
internal sealed class PotionUseTrace
{
    private const uint ObservationMs = 4_000;
    private const int MaxNewItems = 4;
    private Attempt? _attempt;
    private int _sequence;

    internal void Begin(World world, Profile profile, uint serial, bool fromHealingAssistant)
    {
        if (!SerialHelper.IsItem(serial) || world.Player is not { } player)
            return;

        string potion;
        uint relatedSerial = 0;
        if (serial == profile.HealingAssistantGreaterHealSerial)
            potion = "Greater Heal";
        else if (serial == profile.HealingAssistantFullTamalaSerial)
        {
            potion = "Full Tamala";
            relatedSerial = profile.HealingAssistantHalfTamalaSerial;
        }
        else if (serial == profile.HealingAssistantHalfTamalaSerial)
            potion = "Half-full Tamala";
        else
            return;

        Observe(world);
        Finish("superseded by another request", Time.Ticks);

        ItemState? item = ReadItem(world, serial);
        ItemState? relatedItem = relatedSerial == 0 ? null : ReadItem(world, relatedSerial);
        _attempt = new Attempt(++_sequence, Time.Ticks, player, serial, item, relatedSerial, relatedItem);
        string source = fromHealingAssistant ? "Healing Assistant" : "manual or other use";
        Log.Info($"Potion trace #{_attempt.Number}: {source} requested {potion} (0x{serial:X8}), HP {player.Hits}/{player.HitsMax}, {Describe(item)}.");
        if (relatedSerial != 0 && relatedSerial != serial)
            Log.Info($"Potion trace #{_attempt.Number}: configured half-full stack 0x{relatedSerial:X8} before request: {Describe(relatedItem)}.");
    }

    internal void Observe(World world)
    {
        if (_attempt is not { } attempt)
            return;

        uint now = Time.Ticks;
        if (world.Player != attempt.Player)
        {
            Finish("character changed", now);
            return;
        }

        ObserveItem(world, attempt, attempt.Serial, ref attempt.Item, now);
        if (attempt.RelatedSerial != 0 && attempt.RelatedSerial != attempt.Serial)
            ObserveItem(world, attempt, attempt.RelatedSerial, ref attempt.RelatedItem, now);

        attempt.MinHp = Math.Min(attempt.MinHp, attempt.Player.Hits);
        attempt.MaxHp = Math.Max(attempt.MaxHp, attempt.Player.Hits);
        if (attempt.Player.Hits != attempt.LastHp)
        {
            uint elapsed = now - attempt.StartedAt;
            if (attempt.HpChanges++ < 8)
                Log.Info($"Potion trace #{attempt.Number}: +{elapsed} ms, HP {attempt.LastHp} → {attempt.Player.Hits}/{attempt.Player.HitsMax}.");
            attempt.LastHp = attempt.Player.Hits;
        }

        if (now - attempt.StartedAt >= ObservationMs)
            Finish("observation ended", now);
    }

    internal void OnItemCreated(object? sender, EventArgs e)
    {
        if (_attempt is not { } attempt || sender is not Item item || !SerialHelper.IsItem(item.Container)
            || (item.Container != attempt.SourceBag && item.Container != attempt.Backpack)
            || item.Serial == attempt.Serial || item.Serial == attempt.RelatedSerial
            || attempt.NewItems >= MaxNewItems || Time.Ticks - attempt.StartedAt >= ObservationMs)
            return;

        // A full Tamala can potentially become an item with a different serial. These nearby arrivals
        // are candidates only: the client cannot attribute a new item to the potion request yet.
        attempt.NewItems++;
        Log.Info($"Potion trace #{attempt.Number}: +{Time.Ticks - attempt.StartedAt} ms, new item 0x{item.Serial:X8}: {Describe(ItemState.From(item))}.");
    }

    internal void EndSession() => Finish("left game", Time.Ticks);

    private void Finish(string reason, uint now)
    {
        if (_attempt is not { } attempt)
            return;

        _attempt = null;
        string inventory = attempt.InventoryChanged ? "tracked inventory changed" : "no tracked inventory change";
        Log.Info($"Potion trace #{attempt.Number}: +{now - attempt.StartedAt} ms, {reason}; {inventory}; {attempt.NewItems} new items in the potion bag or backpack; HP {attempt.StartHp} → {attempt.LastHp} (min {attempt.MinHp}, max {attempt.MaxHp}).");
    }

    private static void ObserveItem(World world, Attempt attempt, uint serial, ref ItemState? previous, uint now)
    {
        ItemState? current = ReadItem(world, serial);
        if (current == previous)
            return;

        attempt.InventoryChanged = true;
        Log.Info($"Potion trace #{attempt.Number}: +{now - attempt.StartedAt} ms, item 0x{serial:X8}: {Describe(previous)} → {Describe(current)}.");
        previous = current;
    }

    private static ItemState? ReadItem(World world, uint serial) =>
        world.Items.TryGetValue(serial, out Item? item) && !item.IsDestroyed ? ItemState.From(item) : null;

    private static string Describe(ItemState? state) => state is { } item
        ? $"amount {item.Amount}, graphic 0x{item.Graphic:X4}, hue 0x{item.Hue:X4}, container 0x{item.Container:X8}"
        : "not visible";

    private readonly record struct ItemState(ushort Amount, ushort Graphic, ushort Hue, uint Container)
    {
        internal static ItemState From(Item item) => new(item.Amount, item.Graphic, item.Hue, item.Container);
    }

    private sealed class Attempt(int number, uint startedAt, PlayerMobile player, uint serial, ItemState? item,
        uint relatedSerial, ItemState? relatedItem)
    {
        internal readonly int Number = number;
        internal readonly uint StartedAt = startedAt;
        internal readonly PlayerMobile Player = player;
        internal readonly uint Serial = serial;
        internal readonly uint RelatedSerial = relatedSerial;
        internal readonly uint SourceBag = item is { } source && SerialHelper.IsItem(source.Container) ? source.Container : 0;
        internal readonly uint Backpack = player.Backpack?.Serial ?? 0;
        internal readonly int StartHp = player.Hits;
        internal ItemState? Item = item;
        internal ItemState? RelatedItem = relatedItem;
        internal int LastHp = player.Hits;
        internal int MinHp = player.Hits;
        internal int MaxHp = player.Hits;
        internal int HpChanges;
        internal int NewItems;
        internal bool InventoryChanged;
    }
}
