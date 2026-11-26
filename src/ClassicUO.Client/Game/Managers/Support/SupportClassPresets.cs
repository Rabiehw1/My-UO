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

    /// <summary>
    /// Bumped when a class's default list changes in a way old saves can't just append to; saves below it get
    /// that class's list replaced once.
    /// </summary>
    public const int PRESET_VERSION = 1;

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
    private const int NATURES_TOUCH_INDEX = 7;
    internal const int SONG_OF_LIFE_INDEX = 6;

    #endregion

    #region Public methods

    public static bool CanAttack(SupportClass supportClass) => supportClass != SupportClass.Bard;

    public static bool CanUseRole(SupportClass supportClass, SupportRole role) => supportClass switch
    {
        SupportClass.Bard => role == SupportRole.Heal,
        SupportClass.Ranger => false,
        _ => true
    };

    /// <summary>Builds a fresh copy of the default abilities for <paramref name="supportClass" />.</summary>
    /// <param name="supportClass">Class to build defaults for.</param>
    /// <returns>A new list the caller owns.</returns>
    public static List<SupportAbility> Create(SupportClass supportClass) => supportClass switch
    {
        SupportClass.Warrior => CreateWarrior(),
        SupportClass.Mage => CreateMage(),
        SupportClass.Ranger => [],
        SupportClass.MysticArcher => CreateMysticArcher(),
        SupportClass.BladeSinger => CreateBladeSinger(),
        SupportClass.Paladin => CreatePaladin(),
        _ => CreateBard()
    };

    public static SupportClassSettings CreateSettings(SupportClass supportClass)
    {
        var settings = new SupportClassSettings { Class = supportClass, Abilities = Create(supportClass) };
        if (supportClass is SupportClass.Ranger or SupportClass.MysticArcher)
        {
            bool mysticArcher = supportClass == SupportClass.MysticArcher;
            settings.Follow = new SupportFollowSettings
            {
                FollowDistance = mysticArcher ? 5 : 4,
                MaxLeaderDistance = mysticArcher ? 9 : 8
            };
            settings.Combat = new SupportCombatSettings { PreferredMinRange = mysticArcher ? 6 : 5 };
        }
        else if (supportClass is SupportClass.BladeSinger or SupportClass.Paladin)
        {
            settings.Follow = new SupportFollowSettings();
            settings.Combat = new SupportCombatSettings { AttackRange = 1, KeepDistance = false };
        }

        return settings;
    }

    #endregion

    #region Private methods

    private static List<SupportAbility> CreateBard() =>
    [
        BookHeal("bard.healsong", "Song of Life", ShardSpellBook.Songs, SONG_OF_LIFE_INDEX, SupportTargetMode.None, 3000, 4000)
    ];

    internal static uint ConvertLegacyBardSong(SupportAgentSave settings, out int oldButton)
    {
        oldButton = 0;
        SupportAbility? oldSong = settings.Classes.Find(group => group.Class == SupportClass.Bard)?.Abilities
            .Find(ability => ability.Id == "bard.healsong" && ability.Action == SupportActionType.UseItemAndGumpButton);
        if (oldSong == null)
            return 0;

        uint serial = oldSong.ItemSerial;
        oldButton = oldSong.GumpButton;
        oldSong.Action = SupportActionType.CastShardSpell;
        oldSong.ShardBook = ShardSpellBook.Songs;
        oldSong.SpellId = SONG_OF_LIFE_INDEX;
        oldSong.Target = SupportTargetMode.None;
        oldSong.ItemSerial = 0;
        oldSong.GumpButton = 0;
        return serial;
    }

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

    private static List<SupportAbility> CreateMysticArcher() =>
    [
        MagerySpell("mysticarcher.cure", "Cure", HealthBarQuickActions.MageryCure, SupportRole.Cure, 1250),
        BookHeal("mysticarcher.naturestouch", "Nature's Touch", ShardSpellBook.Earth, NATURES_TOUCH_INDEX, SupportTargetMode.Patient, 2000, 750),
        MagerySpell("mysticarcher.greaterheal", "Greater Heal", HealthBarQuickActions.MageryGreaterHeal, SupportRole.Heal, 1750)
    ];

    private static List<SupportAbility> CreateBladeSinger() =>
    [
        BookHeal("bladesinger.songoflife", "Song of Life", ShardSpellBook.Songs, SONG_OF_LIFE_INDEX, SupportTargetMode.None, 3000, 4000),
        MagerySpell("bladesinger.heal", "Heal", HealthBarQuickActions.MageryHeal, SupportRole.Heal, 1000)
    ];

    private static List<SupportAbility> CreatePaladin() =>
    [
        BookHeal("paladin.angelicaura", "Angelic Aura", ShardSpellBook.Holy, ANGELIC_AURA_INDEX, SupportTargetMode.Patient, 2000, 750)
    ];

    private static SupportAbility MagerySpell(string id, string name, int spellId, SupportRole role, int castTimeMs) => new()
    {
        Id = id,
        Name = name,
        Role = role,
        Action = SupportActionType.CastSpell,
        Target = SupportTargetMode.Patient,
        SpellId = spellId,
        CastTimeMs = castTimeMs,
        RecoveryMs = 750
    };

    private static SupportAbility BookHeal(
        string id, string name, ShardSpellBook book, int spellIndex, SupportTargetMode target, int castTimeMs, int recoveryMs
    ) => new()
    {
        Id = id,
        Name = name,
        Role = SupportRole.Heal,
        Action = SupportActionType.CastShardSpell,
        Target = target,
        ShardBook = book,
        SpellId = spellIndex,
        CastTimeMs = castTimeMs,
        RecoveryMs = recoveryMs
    };

    #endregion
}
