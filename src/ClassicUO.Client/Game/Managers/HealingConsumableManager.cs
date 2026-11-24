using System;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Network;

namespace ClassicUO.Game.Managers;

internal sealed class HealingConsumableManager
{
    internal const int MinimumCooldownMs = 20_500;

    private long? _lastPotionUse;
    private long? _lastFishUse;
    public long NextActionTime { get; private set; }

    public bool TryHeal(World world, PlayerMobile player, Profile profile)
    {
        long now = Time.Ticks;
        if (!(profile.HealingAssistantUseGreaterHeal || profile.HealingAssistantUseTamalaHeal || profile.HealingAssistantUseMagicFish)
            || now < NextActionTime || player.IsDead || player.Backpack is not Item backpack
            || Client.Game.UO.GameCursor.ItemHold.Enabled)
            return false;

        if (!TryHeal(profile, player.Hits, player.HitsMax, now, UseStack))
            return false;

        NextActionTime = now + Math.Max(1, GlobalActionCooldown.CooldownDuration);
        GlobalActionCooldown.BeginCooldown();
        return true;

        bool UseStack(uint serial)
        {
            if (!world.Items.TryGetValue(serial, out Item item) || item.IsDestroyed
                || item.BackpackOrRootContainer != backpack.Serial)
                return false;

            // Send now so the item use cannot remain queued after a bandage starts.
            AsyncNetClient.Socket.Send_DoubleClick(item.Serial);
            return true;
        }
    }

    internal bool TryHeal(Profile profile, int hits, int maxHits, long now, Func<uint, bool> tryUse)
    {
        if (!profile.EnableHealingAssistant || hits <= 0 || hits >= maxHits)
            return false;

        if (profile.HealingAssistantUseMagicFish && Matches(profile.HealingAssistantMagicFishMissingHPPercentage)
            && IsReady(_lastFishUse, profile.HealingAssistantFishCooldownMs) && Use(profile.HealingAssistantMagicFishSerial))
        {
            _lastFishUse = now;
            return true;
        }

        if (!IsReady(_lastPotionUse, profile.HealingAssistantPotionCooldownMs))
            return false;

        bool usedPotion =
            (profile.HealingAssistantUseTamalaHeal && Matches(profile.HealingAssistantTamalaHealMissingHPPercentage)
                && (Use(profile.HealingAssistantHalfTamalaSerial) || Use(profile.HealingAssistantFullTamalaSerial)))
            || (profile.HealingAssistantUseGreaterHeal && Matches(profile.HealingAssistantGreaterHealMissingHPPercentage)
                && Use(profile.HealingAssistantGreaterHealSerial));

        if (usedPotion)
            _lastPotionUse = now;

        return usedPotion;

        bool Matches(int percentage) => HasMissingHealth(hits, maxHits, percentage);
        bool Use(uint serial) => SerialHelper.IsItem(serial) && tryUse(serial);
        bool IsReady(long? lastUsed, int delay) => lastUsed == null || now - lastUsed.Value >= Math.Max(MinimumCooldownMs, delay);
    }

    internal static bool HasMissingHealth(int hits, int maxHits, int percentage) =>
        maxHits > 0 && (maxHits - (long)hits) * 100 >= (long)maxHits * Math.Clamp(percentage, 1, 99);
}
