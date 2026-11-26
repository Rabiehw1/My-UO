#nullable enable

using System;
using ClassicUO.Configuration;

namespace ClassicUO.Game.Managers;

/// <summary>Display-only rules for messages already received by the client.</summary>
internal sealed class OnScreenMessageFilter
{
    private const int RepeatWindowMs = 3000;
    private const string SpellAbsorbed = "spell is absorbed by your magical protection";
    private const string ItemCooldown = "you must wait to use something again";

    private Profile? _profile;
    private uint? _lastAbsorption;
    private uint? _lastCooldown;

    internal bool IsMuted(Profile? profile, string text, uint now)
    {
        if (!ReferenceEquals(_profile, profile))
        {
            _profile = profile;
            _lastAbsorption = _lastCooldown = null;
        }

        if (profile == null)
            return false;

        if (profile.OnScreenMessagePhrases is { Count: > 0 } phrases)
        {
            foreach ((string phrase, bool enabled) in phrases)
                if (enabled && !string.IsNullOrWhiteSpace(phrase) && Contains(text, phrase))
                    return true;
        }

        if (!profile.ReduceSpellAbsorptionMessages)
            _lastAbsorption = null;
        if (!profile.ReduceItemCooldownMessages)
            _lastCooldown = null;

        if (profile.ReduceSpellAbsorptionMessages && Contains(text, SpellAbsorbed))
            return SuppressRepeat(ref _lastAbsorption, now);

        return profile.ReduceItemCooldownMessages && Contains(text, ItemCooldown)
            && SuppressRepeat(ref _lastCooldown, now);
    }

    private static bool SuppressRepeat(ref uint? lastShown, uint now)
    {
        if (lastShown is uint previous && unchecked(now - previous) < RepeatWindowMs)
            return true;

        lastShown = now;
        return false;
    }

    private static bool Contains(string text, string phrase) => text.Contains(phrase, StringComparison.OrdinalIgnoreCase);
}
