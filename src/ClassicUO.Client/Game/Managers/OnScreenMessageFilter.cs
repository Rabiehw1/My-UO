#nullable enable

using System;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.Managers.ShardSpells;

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

    /// <summary>Whether a received message should stay off screen; the journal still records it.</summary>
    /// <param name="profile">Settings to apply; null mutes nothing.</param>
    /// <param name="text">Text as the server sent it.</param>
    /// <param name="now">Current time in ms, for the repeat windows.</param>
    /// <param name="displayedText">Text as it will be shown, when the client rewrote it (e.g. spell display format).</param>
    /// <param name="type">Message type the server marked it with.</param>
    /// <returns>True to hide it on screen.</returns>
    internal bool IsMuted(
        Profile? profile,
        string text,
        uint now,
        string? displayedText = null,
        MessageType type = MessageType.Regular
    )
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
            bool hasDifferentDisplay = displayedText != null && !string.Equals(text, displayedText, StringComparison.Ordinal);
            foreach ((string phrase, bool enabled) in phrases)
                if (enabled && !string.IsNullOrWhiteSpace(phrase)
                    && (Contains(text, phrase) || (hasDifferentDisplay && Contains(displayedText!, phrase))))
                    return true;
        }

        if (profile.MuteSpellPowerWords && IsPowerWords(text, type))
            return true;

        if (!profile.ReduceSpellAbsorptionMessages)
            _lastAbsorption = null;
        if (!profile.ReduceItemCooldownMessages)
            _lastCooldown = null;

        if (profile.ReduceSpellAbsorptionMessages && Contains(text, SpellAbsorbed))
            return SuppressRepeat(ref _lastAbsorption, now);

        return profile.ReduceItemCooldownMessages && Contains(text, ItemCooldown)
            && SuppressRepeat(ref _lastCooldown, now);
    }

    /// <summary>
    /// Spell speech is recognised by its message type, which covers spells no table here knows. Shards that send
    /// incantations as plain speech are caught by an exact match against the known words, so a sentence merely
    /// containing "In Mani" is left alone.
    /// </summary>
    private static bool IsPowerWords(string text, MessageType type)
    {
        if (type == MessageType.Spell)
            return true;

        string spoken = text.Trim();
        if (spoken.Length == 0)
            return false;

        return SpellDefinition.WordToTargettype.TryGetValue(spoken, out SpellDefinition? spell)
                && string.Equals(spell.PowerWords, spoken, StringComparison.Ordinal)
            || ShardSpellCatalog.IsPowerWords(spoken);
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
