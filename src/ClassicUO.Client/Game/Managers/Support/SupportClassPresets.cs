#nullable enable

using System.Collections.Generic;
using ClassicUO.Game.Data;
using ClassicUO.Game.Managers.ShardSpells;

namespace ClassicUO.Game.Managers.Support;

/// <summary>
/// Default abilities per helper class. Adding a class: add it to <see cref="SupportClass" /> and a case here.
/// Ability <see cref="SupportAbility.Id" />s must stay stable once released, since saved settings refer to them.
/// </summary>
internal static class SupportClassPresets
{
    #region Private members

    /// <summary>Gump button of the area heal song in the Zulu Hotel songbook.</summary>
    private const int BARD_HEAL_SONG_BUTTON = 7;

    /// <summary>Shard bandage time is 4.5 s; 4.7 s leaves a margin before the next one.</summary>
    private const int BANDAGE_TIME_MS = 4700;

    private const int SPELL_HEAL = 4;
    private const int SPELL_CURE = 11;
    private const int SPELL_PROTECTION = 15;
    private const int SPELL_BLESS = 17;
    private const int SPELL_GREATER_HEAL = 29;

    /// <summary>Recast interval for long-lasting buffs.</summary>
    private const int BUFF_RECAST_MS = 120_000;

    /// <summary>Nagash's staff casts Bless; its bless lasts 30 minutes.</summary>
    private const ushort NAGASH_STAFF_GRAPHIC = 0x13F9;

    private const ushort NAGASH_STAFF_HUE = 1950;
    private const int NAGASH_BLESS_MS = 30 * 60 * 1000;

    /// <summary>Positions in their books (see ShardSpellCatalog): Nature's Touch is the Earth Book's 8th spell,
    /// Angelic Aura the Holy Book's 4th.</summary>
    private const int NATURES_TOUCH_INDEX = 7;

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
            Id = "mage.cure",
            Name = "Cure",
            Role = SupportRole.Cure,
            Action = SupportActionType.CastSpell,
            Target = SupportTargetMode.Patient,
            SpellId = SPELL_CURE,
            CastTimeMs = 1000,
            RecoveryMs = 750
        },
        new()
        {
            Id = "mage.greaterheal",
            Name = "Greater Heal",
            Role = SupportRole.Heal,
            Action = SupportActionType.CastSpell,
            Target = SupportTargetMode.Patient,
            SpellId = SPELL_GREATER_HEAL,
            CastTimeMs = 2000,
            RecoveryMs = 750
        },
        new()
        {
            Id = "mage.heal",
            Name = "Heal",
            Enabled = false,
            Role = SupportRole.Heal,
            Action = SupportActionType.CastSpell,
            Target = SupportTargetMode.Patient,
            SpellId = SPELL_HEAL,
            CastTimeMs = 750,
            RecoveryMs = 750
        },
        new()
        {
            Id = "mage.protection",
            Name = "Protection (self)",
            Role = SupportRole.Buff,
            Action = SupportActionType.CastSpell,
            Target = SupportTargetMode.Self,
            SpellId = SPELL_PROTECTION,
            CastTimeMs = 1000,
            RecoveryMs = 750,
            CooldownMs = BUFF_RECAST_MS,
            ActiveBuff = BuffIconType.Protection
        },
        new()
        {
            Id = "mage.bless",
            Name = "Bless (leader)",
            Enabled = false,
            Role = SupportRole.Buff,
            Action = SupportActionType.CastSpell,
            Target = SupportTargetMode.Leader,
            SpellId = SPELL_BLESS,
            CastTimeMs = 1250,
            RecoveryMs = 750,
            CooldownMs = BUFF_RECAST_MS
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
            Id = "mage.naturestouch",
            Name = "Nature's Touch (Earth Book)",
            Enabled = false,
            Role = SupportRole.Heal,
            Action = SupportActionType.CastShardSpell,
            Target = SupportTargetMode.Patient,
            ShardBook = ShardSpellBook.Earth,
            SpellId = NATURES_TOUCH_INDEX,
            CastTimeMs = 2000,
            RecoveryMs = 750
        },
        new()
        {
            Id = "mage.angelicaura",
            Name = "Angelic Aura (Holy Book)",
            Enabled = false,
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
