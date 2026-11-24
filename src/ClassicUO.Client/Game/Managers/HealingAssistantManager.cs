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

    private static readonly HealingMethod[] BackupOrder =
        [HealingMethod.MagicFish, HealingMethod.TamalaHeal, HealingMethod.GreaterHeal, HealingMethod.Bandages];

    private long? _lastPotionUse;
    private long? _lastFishUse;
    private HealingMethod? _firstMethod;
    private bool _tryBackupNext;
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

        HealingMethod first = profile.HealingAssistantFirstMethod;
        if (_firstMethod != first || !NeedsHealingWith(first))
        {
            _firstMethod = first;
            _tryBackupNext = false;
        }

        if (!_tryBackupNext && TryMethod(first))
        {
            _tryBackupNext = true;
            return true;
        }

        // Give an eligible backup a turn before repeating the preferred method.
        foreach (HealingMethod method in BackupOrder)
        {
            if (method == first || !TryMethod(method))
                continue;

            _tryBackupNext = false;
            return true;
        }

        return _tryBackupNext && TryMethod(first);

        bool NeedsHealingWith(HealingMethod method) => method switch
        {
            HealingMethod.Bandages => profile.EnableBandageAgent && needsBandage,
            HealingMethod.GreaterHeal => profile.HealingAssistantUseGreaterHeal && Matches(method),
            HealingMethod.TamalaHeal => profile.HealingAssistantUseTamalaHeal && Matches(method),
            HealingMethod.MagicFish => profile.HealingAssistantUseMagicFish && Matches(method),
            _ => false
        };

        bool TryMethod(HealingMethod method)
        {
            if (!NeedsHealingWith(method))
                return false;

            switch (method)
            {
                case HealingMethod.Bandages:
                    return tryBandage?.Invoke() == true;
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

        bool Matches(HealingMethod method) => HasMissingHealth(hits, maxHits, GetThreshold(profile, method));
        bool Use(uint serial) => SerialHelper.IsItem(serial) && tryUse(serial);
        bool IsReady(long? lastUsed, int delay) => lastUsed == null || now - lastUsed.Value >= Math.Max(MinimumCooldownMs, delay);
    }

    internal static HealingMethod[] GetThresholdOrder(HealingMethod first) =>
        [first, .. Enum.GetValues<HealingMethod>().Where(method => method != first)];

    internal static void ChangeFirstMethod(Profile profile, HealingMethod first)
    {
        if (profile.HealingAssistantFirstMethod == first)
            return;

        // Keep the player's cutoff values where possible, but assign the earliest one to the new first choice.
        HealingMethod[] order = GetThresholdOrder(first);
        int[] thresholds = order.Select(method => GetThreshold(profile, method)).Order().ToArray();
        int min = 1;
        for (int i = 0; i < order.Length; i++)
        {
            int max = 99 - (order.Length - 1 - i);
            int threshold = Math.Clamp(thresholds[i], min, max);
            SetThreshold(profile, order[i], threshold);
            min = threshold + 1;
        }
        profile.HealingAssistantFirstMethod = first;
    }

    internal static int GetThreshold(Profile profile, HealingMethod method) => method switch
    {
        HealingMethod.Bandages => 100 - Math.Clamp(profile.BandageAgentHPPercentage, 1, 99),
        HealingMethod.GreaterHeal => profile.HealingAssistantGreaterHealMissingHPPercentage,
        HealingMethod.TamalaHeal => profile.HealingAssistantTamalaHealMissingHPPercentage,
        HealingMethod.MagicFish => profile.HealingAssistantMagicFishMissingHPPercentage,
        _ => throw new ArgumentOutOfRangeException(nameof(method))
    };

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
