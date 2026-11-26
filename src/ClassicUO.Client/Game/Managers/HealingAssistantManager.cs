using System;
using System.Linq;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers.ShardSpells;
using ClassicUO.Game.Managers.SpellVisualRange;
using ClassicUO.Network;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game.Managers;

internal sealed class HealingAssistantManager
{
    internal const int MinimumCooldownMs = 20_500;
    internal const int MinimumSpellCooldownMs = 1000;
    private const int SpellTargetWaitMs = 3000;

    private static readonly HealingMethod[] Methods = Enum.GetValues<HealingMethod>();

    private long? _lastPotionUse;
    private long? _lastFishUse;
    private long? _lastSpellUse;
    private long _spellTargetDeadline;
    private int _lastMagerySpellIndex;
    private bool _waitingForSpellBook;
    public long NextActionTime { get; private set; }

    internal bool IsWaitingForSpellCursor =>
        (_spellTargetDeadline != 0 && Time.Ticks <= _spellTargetDeadline)
        || (_waitingForSpellBook && ShardSpellCaster.Instance.IsCasting);

    public bool TryHeal(World world, PlayerMobile player, Profile profile, bool needsBandage, Func<bool> tryBandage)
    {
        long now = Time.Ticks;
        if (player.IsDead)
        {
            _spellTargetDeadline = 0;
            _waitingForSpellBook = false;
            return false;
        }

        if (FinishSpell(world, player, now))
            return true;

        if (Client.Game.UO.GameCursor.ItemHold.Enabled)
            return false;

        return TryHeal(profile, player.Hits, player.HitsMax, now, UseStack, needsBandage, tryBandage, IsKnownStack, CastHeal);

        bool CastHeal(HealingMethod method)
        {
            var caster = ShardSpellCaster.Instance;
            if (now < NextActionTime || player.IsCasting || world.TargetManager.IsTargeting || caster.IsCasting
                || AutoSkinningManager.Instance.IsWaitingForCursor || TargetManager.NextAutoTarget.IsSet)
                return false;

            if (method is HealingMethod.HealSpell or HealingMethod.GreaterHealSpell)
            {
                int spell = method == HealingMethod.HealSpell
                    ? HealthBarQuickActions.MageryHeal : HealthBarQuickActions.MageryGreaterHeal;
                GameActions.CastSpellDirect(spell);
                _lastMagerySpellIndex = spell;
                _spellTargetDeadline = now + SpellTargetWaitMs;
                return true;
            }

            var (book, index) = method switch
            {
                HealingMethod.NaturesTouch => (ShardSpellBook.Earth, 7),
                HealingMethod.AngelicAura => (ShardSpellBook.Holy, 3),
                HealingMethod.SongOfLife => (ShardSpellBook.Songs, 6),
                _ => throw new ArgumentOutOfRangeException(nameof(method))
            };

            _waitingForSpellBook = caster.TryCast(book, index, () =>
            {
                if (!_waitingForSpellBook)
                    return;

                _waitingForSpellBook = false;
                _lastSpellUse = Time.Ticks;
                _lastMagerySpellIndex = GameActions.LastSpellIndex;
                if (profile.EnableHealingAssistant)
                    _spellTargetDeadline = Time.Ticks + SpellTargetWaitMs;
            });
            return _waitingForSpellBook;
        }

        bool UseStack(uint serial)
        {
            if (now < NextActionTime)
                return false;

            // Send now so the item use cannot remain queued after a bandage starts.
            AsyncNetClient.Socket.Send_DoubleClick(serial, fromHealingAssistant: true);
            Log.Debug($"Healing Assistant sent double-click for item 0x{serial:X8}.");
            NextActionTime = now + Math.Max(1, GlobalActionCooldown.CooldownDuration);
            GlobalActionCooldown.BeginCooldown();
            return true;
        }

        bool IsKnownStack(uint serial) => world.Items.TryGetValue(serial, out Item item) && !item.IsDestroyed;
    }

    private bool FinishSpell(World world, PlayerMobile player, long now)
    {
        if (_spellTargetDeadline == 0)
        {
            _waitingForSpellBook = _waitingForSpellBook && ShardSpellCaster.Instance.IsCasting;
            return _waitingForSpellBook;
        }

        if (now > _spellTargetDeadline || SpellVisualRangeManager.Instance.LastCastFailedTick > _lastSpellUse.GetValueOrDefault()
            || GameActions.LastSpellIndex != _lastMagerySpellIndex)
        {
            _spellTargetDeadline = 0;
            return false;
        }

        var target = world.TargetManager;
        if (!target.IsTargeting || AutoSkinningManager.Instance.IsWaitingForCursor)
            return true;

        _spellTargetDeadline = 0;
        if (target.TargetingState == CursorTarget.Object && target.TargetingType is TargetType.Neutral or TargetType.Beneficial)
        {
            target.Target(player.Serial);
            NextActionTime = now + Math.Max(1, GlobalActionCooldown.CooldownDuration);
        }
        return true;
    }

    internal bool TryHeal(
        Profile profile, int hits, int maxHits, long now, Func<uint, bool> tryUse,
        bool needsBandage = false, Func<bool> tryBandage = null, Func<uint, bool> isKnownStack = null,
        Func<HealingMethod, bool> tryCast = null)
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
            case HealingMethod.GreaterHeal:
            case HealingMethod.TamalaHeal:
                if (!IsReady(_lastPotionUse, profile.HealingAssistantPotionCooldownMs))
                    return false;

                bool used = method == HealingMethod.TamalaHeal
                    ? UseTamala()
                    : Use(profile.HealingAssistantGreaterHealSerial);
                if (used)
                    _lastPotionUse = now;

                return used;
            default:
                if ((_lastSpellUse.HasValue && now - _lastSpellUse.Value < Math.Max(MinimumSpellCooldownMs, profile.HealingAssistantSpellCooldownMs))
                    || tryCast?.Invoke(method.Value) != true)
                    return false;

                _lastSpellUse = now;
                return true;
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

    internal long GetCooldownRemaining(Profile profile, HealingMethod method, long now)
    {
        var (lastUsed, delay) = method switch
        {
            HealingMethod.Bandages => (null, 0),
            HealingMethod.MagicFish => (_lastFishUse, Math.Max(MinimumCooldownMs, profile.HealingAssistantFishCooldownMs)),
            HealingMethod.GreaterHeal or HealingMethod.TamalaHeal => (_lastPotionUse, Math.Max(MinimumCooldownMs, profile.HealingAssistantPotionCooldownMs)),
            _ => (_lastSpellUse, Math.Max(MinimumSpellCooldownMs, profile.HealingAssistantSpellCooldownMs))
        };
        return lastUsed.HasValue ? Math.Max(0, delay - (now - lastUsed.Value)) : 0;
    }

    internal static HealingMethod? GetMethodForHealth(Profile profile, int hits, int maxHits)
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

    internal static bool HasNonBandageMethod(Profile profile) =>
        Methods.Any(method => method != HealingMethod.Bandages && IsEnabled(profile, method));

    internal static bool IsEnabled(Profile profile, HealingMethod method) => method switch
    {
        HealingMethod.Bandages => profile.EnableBandageAgent,
        HealingMethod.GreaterHeal => profile.HealingAssistantUseGreaterHeal,
        HealingMethod.TamalaHeal => profile.HealingAssistantUseTamalaHeal,
        HealingMethod.MagicFish => profile.HealingAssistantUseMagicFish,
        HealingMethod.NaturesTouch => profile.HealingAssistantUseNaturesTouch,
        HealingMethod.HealSpell => profile.HealingAssistantUseHealSpell,
        HealingMethod.GreaterHealSpell => profile.HealingAssistantUseGreaterHealSpell,
        HealingMethod.AngelicAura => profile.HealingAssistantUseAngelicAura,
        HealingMethod.SongOfLife => profile.HealingAssistantUseSongOfLife,
        _ => false
    };

    internal static HealingMethod[] GetThresholdOrder(HealingMethod first) =>
        [first, .. Methods.Where(method => method != first)];

    internal static void ChangeFirstMethod(Profile profile, HealingMethod first)
    {
        if (profile.HealingAssistantFirstMethod == first)
            return;

        // Keep the player's cutoff values where possible, but assign the earliest one to the new first choice.
        HealingMethod[] order = GetThresholdOrder(first)
            .Where(method => method == first || (int)method <= (int)HealingMethod.MagicFish || IsEnabled(profile, method))
            .ToArray();
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
        HealingMethod.NaturesTouch => profile.HealingAssistantNaturesTouchMissingHPPercentage,
        HealingMethod.HealSpell => profile.HealingAssistantHealSpellMissingHPPercentage,
        HealingMethod.GreaterHealSpell => profile.HealingAssistantGreaterHealSpellMissingHPPercentage,
        HealingMethod.AngelicAura => profile.HealingAssistantAngelicAuraMissingHPPercentage,
        HealingMethod.SongOfLife => profile.HealingAssistantSongOfLifeMissingHPPercentage,
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
            case HealingMethod.NaturesTouch:
                profile.HealingAssistantNaturesTouchMissingHPPercentage = threshold;
                break;
            case HealingMethod.HealSpell:
                profile.HealingAssistantHealSpellMissingHPPercentage = threshold;
                break;
            case HealingMethod.GreaterHealSpell:
                profile.HealingAssistantGreaterHealSpellMissingHPPercentage = threshold;
                break;
            case HealingMethod.AngelicAura:
                profile.HealingAssistantAngelicAuraMissingHPPercentage = threshold;
                break;
            case HealingMethod.SongOfLife:
                profile.HealingAssistantSongOfLifeMissingHPPercentage = threshold;
                break;
        }
    }

    internal static bool HasMissingHealth(int hits, int maxHits, int percentage) =>
        maxHits > 0 && (maxHits - (long)hits) * 100 >= (long)maxHits * Math.Clamp(percentage, 1, 99);
}
