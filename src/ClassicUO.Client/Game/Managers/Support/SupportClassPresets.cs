#nullable enable

using System.Collections.Generic;
using ClassicUO.Game.Managers.ShardSpells;

namespace ClassicUO.Game.Managers.Support;

/// <summary>
/// Default abilities per helper class. Adding a class: add it to <see cref="SupportClass" /> and a case here.
/// Ability <see cref="SupportAbility.Id" />s must stay stable once released, since saved settings refer to them.
/// </summary>
internal static class SupportClassPresets
{
    #region Private members

    /// <summary>
    /// Bumped when a class's default list changes in a way old saves can't just append to; saves below it get
    /// that class's list replaced once.
    /// </summary>
    public const int PRESET_VERSION = 1;

    /// <summary>Gump button of the area heal song in the Zulu Hotel songbook.</summary>
    private const int BARD_HEAL_SONG_BUTTON = 7;

    /// <summary>Shard bandage time is 4.5 s; 4.7 s leaves a margin before the next one.</summary>
    private const int BANDAGE_TIME_MS = 4700;

    /// <summary>Nagash's staff bless can only be used once every 30 minutes.</summary>
    private const ushort NAGASH_STAFF_GRAPHIC = 0x13F9;

    private const ushort NAGASH_STAFF_HUE = 1950;
    private const int NAGASH_BLESS_MS = 30 * 60 * 1000;

    /// <summary>Positions in their books (see ShardSpellCatalog): Antidote is the Earth Book's 1st spell,
    /// Angelic Aura the Holy Book's 4th.</summary>
    private const int ANTIDOTE_INDEX = 0;

    private const int ANGELIC_AURA_INDEX = 3;

    #endregion

    #region Public methods

    /// <summary>Builds a fresh copy of the default abilities for <paramref name="supportClass" />.</summary>
    /// <param name="supportClass">Class to build defaults for.</param>
    /// <returns>A new list the caller owns.</returns>
    public static List<SupportAbility> Create(SupportClass supportClass) => supportClass switch
    {
        SupportClass.Warrior => CreateWarrior(),
        SupportClass.Mage => CreateMage(),
        _ => CreateBard()
    };

    #endregion

    #region Private methods

    private static List<SupportAbility> CreateBard() =>
    [
        new()
        {
            Id = "bard.healsong",
            Name = "Healing song (area, pick songbook)",
            Role = SupportRole.Heal,
            Action = SupportActionType.UseItemAndGumpButton,
            Target = SupportTargetMode.None,
            GumpButton = BARD_HEAL_SONG_BUTTON,
            CastTimeMs = 3000,
            RecoveryMs = 4000
        },
        new()
        {
            Id = "bard.invisibility",
            Name = "Invisibility item (pick it below)",
            Role = SupportRole.Emergency,
            Action = SupportActionType.UseItem,
            Target = SupportTargetMode.None,
            RecoveryMs = 1000
        }
    ];

    private static List<SupportAbility> CreateWarrior() =>
    [
        new()
        {
            Id = "warrior.bandage",
            Name = "Bandage",
            Role = SupportRole.Heal,
            Action = SupportActionType.Bandage,
            Target = SupportTargetMode.Patient,
            CooldownMs = BANDAGE_TIME_MS
        }
    ];

    private static List<SupportAbility> CreateMage() =>
    [
        new()
        {
            Id = "mage.antidote",
            Name = "Antidote (Earth Book)",
            Role = SupportRole.Cure,
            Action = SupportActionType.CastShardSpell,
            Target = SupportTargetMode.None,
            ShardBook = ShardSpellBook.Earth,
            SpellId = ANTIDOTE_INDEX,
            CastTimeMs = 2000,
            RecoveryMs = 750
        },
        new()
        {
            Id = "mage.nagashbless",
            Name = "Bless (Nagash's staff)",
            Role = SupportRole.Bless,
            Action = SupportActionType.UseItem,
            Target = SupportTargetMode.Leader,
            ItemGraphic = NAGASH_STAFF_GRAPHIC,
            ItemHue = NAGASH_STAFF_HUE,
            CastTimeMs = 1000,
            RecoveryMs = 750,
            CooldownMs = NAGASH_BLESS_MS
        },
        new()
        {
            Id = "mage.angelicaura",
            Name = "Angelic Aura (Holy Book)",
            Role = SupportRole.Heal,
            Action = SupportActionType.CastShardSpell,
            Target = SupportTargetMode.Patient,
            ShardBook = ShardSpellBook.Holy,
            SpellId = ANGELIC_AURA_INDEX,
            CastTimeMs = 2000,
            RecoveryMs = 750
        }
    ];

    #endregion
}
