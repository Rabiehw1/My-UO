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

/// <summary>How a skill is used each turn.</summary>
public enum TrainerUse
{
    /// <summary>Use the skill; answer a target cursor, if one comes, with yourself.</summary>
    Self,

    /// <summary>Use the skill, nothing to target.</summary>
    Plain,

    /// <summary>Use the skill on the item or creature picked for it.</summary>
    Picked,

    /// <summary>Play an instrument from the backpack.</summary>
    Instrument,

    /// <summary>Use Stealth when hidden, Hiding otherwise.</summary>
    StealthOrHide,

    /// <summary>Use the skill and close the server gump it opens (Tracking's creature list).</summary>
    CloseList,

    /// <summary>Use the skill on the nearest corpse in reach.</summary>
    NearestCorpse,

    /// <summary>Use the skill, warning first when full mana means it can't train.</summary>
    Meditation,
    Blacksmith,
    Lumberjack,
    Begging,
    Tailor
}

/// <summary>What a skill needs picked.</summary>
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

/// <summary>One skill the Trainer page can train.</summary>
/// <param name="Id">Stable key used in saved settings.</param>
/// <param name="Name">Skill name as shown and as the client's skill list names it.</param>
/// <param name="Class">Class group it is listed under.</param>
/// <param name="Use">How it is used each turn.</param>
/// <param name="Target">What needs picked (only with <see cref="TrainerUse.Picked"/>).</param>
/// <param name="Busy">Running-state rule, or null when every use ends straight away.</param>
public sealed record TrainerSkill(
    string Id,
    string Name,
    TrainerClass Class,
    TrainerUse Use,
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
        new("anatomy", "Anatomy", TrainerClass.Warrior, TrainerUse.Self),
        new("evaluating_intelligence", "Evaluating Intelligence", TrainerClass.Mage, TrainerUse.Self),
        new("meditation", "Meditation", TrainerClass.Mage, TrainerUse.Meditation, Busy: new TrainerBusyRule(
            "You enter a meditative trance",
            ["You stop meditating", "You've lost your concentration"])),
        new("musicianship", "Musicianship", TrainerClass.Bard, TrainerUse.Instrument),
        new("peacemaking", "Peacemaking", TrainerClass.Bard, TrainerUse.Plain),
        new("taste_identification", "Taste Identification", TrainerClass.Bard, TrainerUse.Picked, TrainerTargetKind.Item),
        new("animal_lore", "Animal Lore", TrainerClass.Ranger, TrainerUse.Picked, TrainerTargetKind.Mobile),
        new("tracking", "Tracking", TrainerClass.Ranger, TrainerUse.CloseList),
        new("detecting_hidden", "Detecting Hidden", TrainerClass.Thief, TrainerUse.Plain),
        new("hiding", "Hiding", TrainerClass.Thief, TrainerUse.Plain),
        new("stealth", "Stealth", TrainerClass.Thief, TrainerUse.StealthOrHide),
        new("tailoring", "Tailoring", TrainerClass.Crafter, TrainerUse.Tailor, TrainerTargetKind.Item),
        new("lumberjacking", "Lumberjacking", TrainerClass.Crafter, TrainerUse.Lumberjack, TrainerTargetKind.Item),
        new("blacksmithy", "Blacksmithy", TrainerClass.Crafter, TrainerUse.Blacksmith, TrainerTargetKind.Item),
        new("arms_lore", "Arms Lore", TrainerClass.Crafter, TrainerUse.Picked, TrainerTargetKind.Item),
        new("begging", "Begging", TrainerClass.Other, TrainerUse.Begging),
        new("forensic_evaluation", "Forensic Evaluation", TrainerClass.Other, TrainerUse.NearestCorpse)
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
