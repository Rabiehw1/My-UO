#nullable enable

using System;
using System.Collections.Generic;

namespace ClassicUO.Game.Managers.Trainer;

/// <summary>Zulu Hotel class a trainable skill belongs to, used to group the Trainer page.</summary>
public enum TrainerClass
{
    Warrior,
    Mage,
    Bard,
    Ranger,
    Thief,
    Crafter,
    Other
}

/// <summary>What a skill's script targets.</summary>
public enum TrainerTargetKind
{
    /// <summary>Nothing to pick: the script targets yourself or nothing.</summary>
    None,

    /// <summary>An item the player picks once per character.</summary>
    Item,

    /// <summary>A creature the player picks once per character.</summary>
    Mobile
}

/// <summary>
/// Journal lines that bracket a skill which keeps running after its use (e.g. Meditation's trance). The trainer
/// skips the skill from the start line until an end line, so a second use doesn't cut the first short.
/// </summary>
/// <param name="Start">Text that starts the busy spell, matched ignoring case.</param>
/// <param name="Ends">Texts that end it.</param>
public sealed record TrainerBusyRule(string Start, IReadOnlyList<string> Ends);

/// <summary>One skill the Trainer page can train, backed by a built-in script.</summary>
/// <param name="Id">Stable key used in saved settings and the script's resource name.</param>
/// <param name="Name">Skill name as shown.</param>
/// <param name="Class">Class group it is listed under.</param>
/// <param name="Target">What the script needs picked.</param>
/// <param name="Busy">Running-state rule, or null when every use ends straight away.</param>
public sealed record TrainerSkill(
    string Id,
    string Name,
    TrainerClass Class,
    TrainerTargetKind Target = TrainerTargetKind.None,
    TrainerBusyRule? Busy = null
);

/// <summary>The skills the Trainer page offers, in display order within each class.</summary>
internal static class TrainerCatalog
{
    #region Public accessors

    /// <summary>Every trainable skill.</summary>
    public static IReadOnlyList<TrainerSkill> Skills { get; } =
    [
        new("anatomy", "Anatomy", TrainerClass.Warrior),
        new("evaluating_intelligence", "Evaluating Intelligence", TrainerClass.Mage),
        new("meditation", "Meditation", TrainerClass.Mage, Busy: new TrainerBusyRule(
            "You enter a meditative trance",
            ["You stop meditating", "You've lost your concentration"])),
        new("musicianship", "Musicianship", TrainerClass.Bard),
        new("peacemaking", "Peacemaking", TrainerClass.Bard),
        new("taste_identification", "Taste Identification", TrainerClass.Bard, TrainerTargetKind.Item),
        new("animal_lore", "Animal Lore", TrainerClass.Ranger, TrainerTargetKind.Mobile),
        new("tracking", "Tracking", TrainerClass.Ranger),
        new("detecting_hidden", "Detecting Hidden", TrainerClass.Thief),
        new("hiding", "Hiding", TrainerClass.Thief),
        new("stealth", "Stealth", TrainerClass.Thief),
        new("arms_lore", "Arms Lore", TrainerClass.Crafter, TrainerTargetKind.Item),
        new("forensic_evaluation", "Forensic Evaluation", TrainerClass.Other)
    ];

    #endregion

    #region Public methods

    /// <summary>Looks up a skill by its <see cref="TrainerSkill.Id"/>.</summary>
    /// <param name="id">Saved key.</param>
    /// <returns>The skill, or null for a key no longer offered.</returns>
    public static TrainerSkill? Find(string id)
    {
        foreach (TrainerSkill skill in Skills)
            if (string.Equals(skill.Id, id, StringComparison.Ordinal))
                return skill;

        return null;
    }

    #endregion
}
