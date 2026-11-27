#nullable enable

using System;
using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.UI.Gumps;

namespace ClassicUO.Game.Managers;

internal sealed class ConsumableCooldownMonitor
{
    private const uint ConfirmationWindowMs = 2500;
    private readonly Dictionary<uint, Attempt> _pending = new();
    private CoolDownBar? _potionBar;
    private CoolDownBar? _fishBar;

    internal void Begin(World world, Profile profile, uint serial)
    {
        if (!profile.ShowConsumableCooldowns || world.Player is not { IsDead: false, Backpack: { } backpack }
            || !SerialHelper.IsItem(serial) || _pending.ContainsKey(serial)
            || world.Items.Get(serial) is not { } item || item.BackpackOrRootContainer != backpack.Serial)
            return;

        var kind = Classify(item, profile);
        if (!IsTracked(profile, kind))
            return;

        Item? halfTamala = item.Serial == profile.HealingAssistantFullTamalaSerial
            ? world.Items.Get(profile.HealingAssistantHalfTamalaSerial) : null;
        (ushort, ushort)? halfAppearance = halfTamala == null ? null : (halfTamala.Graphic, halfTamala.Hue);
        _pending.Add(serial, new Attempt(kind, Time.Ticks, item.Container, item.Graphic, item.Hue,
            Math.Max(1, (int)item.Amount), halfAppearance));
    }

    internal void Cancel(uint serial) => _pending.Remove(serial);

    internal void OnItemChanged(object? sender, EventArgs args)
    {
        if (sender is not Item item || !_pending.TryGetValue(item.Serial, out Attempt attempt))
            return;

        if (item.Container != attempt.Container || !IsInBackpack(item.World, item))
        {
            Cancel(item.Serial);
            return;
        }

        var appearance = (item.Graphic, item.Hue);
        bool sameAppearance = appearance == (attempt.Graphic, attempt.Hue);
        int amount = Math.Max(1, (int)item.Amount);
        if ((sameAppearance && amount < attempt.Amount)
            || (!sameAppearance && attempt.HalfTamala == appearance))
            Confirm(item.World, item.Serial, attempt);
        else if (!sameAppearance || amount > attempt.Amount)
            Cancel(item.Serial);
    }

    internal void OnItemRemoved(World world, Item item)
    {
        if (!_pending.TryGetValue(item.Serial, out Attempt attempt))
            return;

        if (attempt.Amount == 1 && item.Container == attempt.Container && IsInBackpack(world, item))
            Confirm(world, item.Serial, attempt);
        else
            Cancel(item.Serial);
    }

    internal void Update(World world)
    {
        if (ProfileManager.CurrentProfile?.ShowConsumableCooldowns != true || world.Player is not { IsDead: false })
        {
            Reset();
            return;
        }

        foreach (var (serial, attempt) in _pending)
            if (Time.Ticks - attempt.StartedAt > ConfirmationWindowMs)
                _pending.Remove(serial);
    }

    internal void Reset()
    {
        _pending.Clear();
        _potionBar?.Dispose();
        _fishBar?.Dispose();
        _potionBar = _fishBar = null;
    }

    private void Confirm(World world, uint serial, Attempt attempt)
    {
        Cancel(serial);
        uint elapsed = Time.Ticks - attempt.StartedAt;
        if (elapsed > ConfirmationWindowMs || ProfileManager.CurrentProfile is not { ShowConsumableCooldowns: true } profile
            || world.Player is not { IsDead: false } || !IsTracked(profile, attempt.Kind))
            return;

        bool fish = attempt.Kind == ConsumableCooldownItems.Fish;
        ref CoolDownBar? bar = ref (fish ? ref _fishBar : ref _potionBar);
        if (bar is { IsDisposed: false } && bar.Remaining > TimeSpan.Zero)
            return;

        var duration = TimeSpan.FromSeconds(Math.Clamp(fish ? profile.FishDisplayCooldownSeconds
            : profile.PotionDisplayCooldownSeconds, 1, 300));
        var remaining = duration - TimeSpan.FromMilliseconds(elapsed);
        if (remaining <= TimeSpan.Zero)
            return;

        bar?.Dispose();
        bar = CoolDownBarManager.CreateCoolDownBar(world, duration,
            TazLang.Get(fish ? "assistant_consumable_fish_bar" : "assistant_consumable_potion_bar"),
            fish ? (ushort)0x0075 : (ushort)0x0021, replace: false);
        bar?.Update(currentValue: remaining);
    }

    private static bool IsInBackpack(World world, Item item) =>
        world.Player?.Backpack is { } backpack && item.BackpackOrRootContainer == backpack.Serial;

    private static bool IsTracked(Profile profile, ConsumableCooldownItems kind) =>
        kind == ConsumableCooldownItems.Fish || (profile.TrackedCooldownPotions & kind) != 0;

    private static ConsumableCooldownItems Classify(Item item, Profile profile)
    {
        if (item.Serial == profile.HealingAssistantMagicFishSerial)
            return ConsumableCooldownItems.Fish;
        if (item.Serial == profile.HealingAssistantGreaterHealSerial)
            return ConsumableCooldownItems.GreaterHeal;
        if (item.Serial == profile.HealingAssistantFullTamalaSerial || item.Serial == profile.HealingAssistantHalfTamalaSerial)
            return ConsumableCooldownItems.Tamala;

        return (item.Graphic, item.Hue) switch
        {
            (0x0DD6, 0x0009 or 0x03E9 or 0x0488 or 0x0000 or 0x0029 or 0x003D or 0x0033 or 0x005B) => ConsumableCooldownItems.Fish,
            (0x0F0C, 0x009B) => ConsumableCooldownItems.Tamala,
            (0x0EFC, 0x0486) => ConsumableCooldownItems.MageRefresh,
            (0x0F07, 0x0000) => ConsumableCooldownItems.GreaterCure,
            (0x0F0B, 0x0000) => ConsumableCooldownItems.FullRefresh,
            (0x0EFC, 0x0000) => ConsumableCooldownItems.Homeric,
            (0x0F04, 0x0502) => ConsumableCooldownItems.Dispel,
            _ => ConsumableCooldownItems.None
        };
    }

    private readonly record struct Attempt(ConsumableCooldownItems Kind, uint StartedAt, uint Container,
        ushort Graphic, ushort Hue, int Amount, (ushort, ushort)? HalfTamala);
}
