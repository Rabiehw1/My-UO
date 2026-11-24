using System;
using System.Linq;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Network;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game.Managers;

internal sealed class HealingAssistantManager
{
    internal const int MinimumCooldownMs = 20_500;

    private static readonly HealingMethod[] Methods = Enum.GetValues<HealingMethod>();

    private long? _lastPotionUse;
    private long? _lastFishUse;
    public long NextActionTime { get; private set; }

    public bool TryHeal(World world, PlayerMobile player, Profile profile, bool needsBandage, Func<bool> tryBandage)
    {
        long now = Time.Ticks;
        if (player.IsDead || Client.Game.UO.GameCursor.ItemHold.Enabled)
            return false;

        return TryHeal(profile, player.Hits, player.HitsMax, now, UseStack, needsBandage, tryBandage, IsKnownStack);

        bool UseStack(uint serial)
        {
            if (now < NextActionTime)
                return false;

            // Send now so the item use cannot remain queued after a bandage starts.
            AsyncNetClient.Socket.Send_DoubleClick(serial);
            Log.Debug($"Healing Assistant sent double-click for item 0x{serial:X8}.");
            NextActionTime = now + Math.Max(1, GlobalActionCooldown.CooldownDuration);
            GlobalActionCooldown.BeginCooldown();
            return true;
        }

        bool IsKnownStack(uint serial) => world.Items.TryGetValue(serial, out Item item) && !item.IsDestroyed;
    }

    internal bool TryHeal(
        Profile profile, int hits, int maxHits, long now, Func<uint, bool> tryUse,
        bool needsBandage = false, Func<bool> tryBandage = null, Func<uint, bool> isKnownStack = null)
    {
        if (!profile.EnableHealingAssistant || hits <= 0 || maxHits <= 0)
            return false;

        HealingMethod? method = GetMethodForHealth(profile, hits, maxHits);
        switch (method)
        {
            case null: // Bandage for poison above the configured health ranges.
            case HealingMethod.Bandages:
                return profile.EnableBandageAgent && needsBandage && tryBandage?.Invoke() == true;
            case HealingMethod.MagicFish:
                if (!IsReady(_lastFishUse, profile.HealingAssistantFishCooldownMs) || !Use(profile.HealingAssistantMagicFishSerial))
                    return false;

                _lastFishUse = now;
                return true;
            default:
                if (!IsReady(_lastPotionUse, profile.HealingAssistantPotionCooldownMs))
                    return false;

                bool used = method == HealingMethod.TamalaHeal
                    ? UseTamala()
                    : Use(profile.HealingAssistantGreaterHealSerial);
                if (used)
                    _lastPotionUse = now;

                return used;
        }

        bool UseTamala()
        {
            uint half = profile.HealingAssistantHalfTamalaSerial;
            uint full = profile.HealingAssistantFullTamalaSerial;

            // Prefer the half-full stack unless the full stack is the only one still visible to the client.
            if (SerialHelper.IsItem(full) && isKnownStack?.Invoke(full) == true && !isKnownStack(half))
                return Use(full) || Use(half);

            return Use(half) || Use(full);
        }

        bool Use(uint serial) => SerialHelper.IsItem(serial) && tryUse(serial);
        bool IsReady(long? lastUsed, int delay) => lastUsed == null || now - lastUsed.Value >= Math.Max(MinimumCooldownMs, delay);
    }

    private static HealingMethod? GetMethodForHealth(Profile profile, int hits, int maxHits)
    {
        // The greatest missing-HP cutoff reached owns this interval, even if its item is on cooldown.
        HealingMethod? selected = null;
        int highest = 0;
        foreach (HealingMethod method in Methods)
        {
            if (!IsEnabled(profile, method))
                continue;

            int threshold = GetThreshold(profile, method);
            if (threshold < highest || !HasMissingHealth(hits, maxHits, threshold))
                continue;

            selected = method;
            highest = threshold;
        }

        return selected;
    }

    internal static bool IsEnabled(Profile profile, HealingMethod method) => method switch
    {
        HealingMethod.Bandages => profile.EnableBandageAgent,
        HealingMethod.GreaterHeal => profile.HealingAssistantUseGreaterHeal,
        HealingMethod.TamalaHeal => profile.HealingAssistantUseTamalaHeal,
        HealingMethod.MagicFish => profile.HealingAssistantUseMagicFish,
        _ => false
    };

    internal static HealingMethod[] GetThresholdOrder(HealingMethod first) =>
        [first, .. Methods.Where(method => method != first)];

    internal static void ChangeFirstMethod(Profile profile, HealingMethod first)
    {
        if (profile.HealingAssistantFirstMethod == first)
            return;

        // Keep the player's cutoff values where possible, but assign the earliest one to the new first choice.
        HealingMethod[] order = GetThresholdOrder(first);
        int[] thresholds = order.Select(method => GetThreshold(profile, method)).Order().ToArray();
        FitThresholds(profile, order, thresholds);
        profile.HealingAssistantFirstMethod = first;
    }

    internal static void FitEnabledThresholds(Profile profile)
    {
        HealingMethod[] order = GetThresholdOrder(profile.HealingAssistantFirstMethod).Where(method => IsEnabled(profile, method)).ToArray();
        FitThresholds(profile, order, order.Select(method => GetThreshold(profile, method)).ToArray());
    }

    private static void FitThresholds(Profile profile, HealingMethod[] order, int[] thresholds)
    {
        int min = 1;
        for (int i = 0; i < order.Length; i++)
        {
            int max = 99 - (order.Length - 1 - i);
            int threshold = Math.Clamp(thresholds[i], min, max);
            SetThreshold(profile, order[i], threshold);
            min = threshold + 1;
        }
    }

    internal static int GetThreshold(Profile profile, HealingMethod method) => Math.Clamp(method switch
    {
        HealingMethod.Bandages => 100 - Math.Clamp(profile.BandageAgentHPPercentage, 1, 99),
        HealingMethod.GreaterHeal => profile.HealingAssistantGreaterHealMissingHPPercentage,
        HealingMethod.TamalaHeal => profile.HealingAssistantTamalaHealMissingHPPercentage,
        HealingMethod.MagicFish => profile.HealingAssistantMagicFishMissingHPPercentage,
        _ => throw new ArgumentOutOfRangeException(nameof(method))
    }, 1, 99);

    internal static void SetThreshold(Profile profile, HealingMethod method, int threshold)
    {
        switch (method)
        {
            case HealingMethod.Bandages:
                profile.BandageAgentHPPercentage = 100 - threshold;
                break;
            case HealingMethod.GreaterHeal:
                profile.HealingAssistantGreaterHealMissingHPPercentage = threshold;
                break;
            case HealingMethod.TamalaHeal:
                profile.HealingAssistantTamalaHealMissingHPPercentage = threshold;
                break;
            case HealingMethod.MagicFish:
                profile.HealingAssistantMagicFishMissingHPPercentage = threshold;
                break;
        }
    }

    internal static bool HasMissingHealth(int hits, int maxHits, int percentage) =>
        maxHits > 0 && (maxHits - (long)hits) * 100 >= (long)maxHits * Math.Clamp(percentage, 1, 99);
}
