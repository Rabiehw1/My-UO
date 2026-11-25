#nullable enable

using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using ClassicUO.Configuration;

namespace ClassicUO.Game.Managers.Support;

/// <summary>Helper class the logged-in character plays; decides which ability presets apply.</summary>
public enum SupportClass
{
    Bard,
    Warrior,
    Mage
}

/// <summary>When the support agent uses an ability.</summary>
public enum SupportRole
{
    /// <summary>Someone in the group is below the heal threshold.</summary>
    Heal,

    /// <summary>Someone in the group is poisoned.</summary>
    Cure,

    /// <summary>Recast on a timer (the ability's cooldown) to keep a protection or buff up.</summary>
    Buff,

    /// <summary>The helper itself dropped to the emergency threshold; used once before falling back to heals.</summary>
    Emergency
}

/// <summary>How an ability is performed.</summary>
public enum SupportActionType
{
    /// <summary>Cast a spell by its number (e.g. 29 = Greater Heal).</summary>
    CastSpell,

    /// <summary>Use the picked item (e.g. a songbook) and press a button on the gump it opens.</summary>
    UseItemAndGumpButton,

    /// <summary>Use the picked item.</summary>
    UseItem,

    /// <summary>Apply a bandage, the same way the Bandage agent does.</summary>
    Bandage,

    /// <summary>Say a line, for shards with spoken commands.</summary>
    Say
}

/// <summary>Who an ability is aimed at.</summary>
public enum SupportTargetMode
{
    /// <summary>No target cursor (area songs and spells, items).</summary>
    None,

    /// <summary>The helper itself.</summary>
    Self,

    /// <summary>Whoever needs it: the most hurt (heals) or a poisoned member (cures).</summary>
    Patient,

    /// <summary>The followed character.</summary>
    Leader
}

/// <summary>One thing a helper class can do. Seeded from <see cref="SupportClassPresets" />, then editable.</summary>
public sealed class SupportAbility
{
    /// <summary>Stable key matching the preset, so presets can be updated without losing the player's choices.</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public SupportRole Role { get; set; }
    public SupportActionType Action { get; set; }
    public SupportTargetMode Target { get; set; }

    /// <summary>Spell number for <see cref="SupportActionType.CastSpell" />.</summary>
    public int SpellId { get; set; }

    /// <summary>Graphic of the picked item; shown for reference only, items are always used by serial.</summary>
    public ushort ItemGraphic { get; set; }

    /// <summary>The item picked in the tab for <see cref="SupportActionType.UseItem" /> and
    /// <see cref="SupportActionType.UseItemAndGumpButton" />. The ability is skipped until one is picked.</summary>
    public uint ItemSerial { get; set; }

    /// <summary>Gump button pressed after the item opens its gump.</summary>
    public int GumpButton { get; set; }

    /// <summary>Text for <see cref="SupportActionType.Say" />.</summary>
    public string Text { get; set; } = "";

    /// <summary>Time the action takes; the helper stands still meanwhile so casting isn't interrupted.</summary>
    public int CastTimeMs { get; set; }

    /// <summary>Wait after the cast before the helper acts again (heal ticks, recovery).</summary>
    public int RecoveryMs { get; set; }

    /// <summary>Minimum time between two uses of this ability. For buffs, the recast interval.</summary>
    public int CooldownMs { get; set; }

    /// <returns>A copy safe to edit without touching the original.</returns>
    public SupportAbility Clone() => (SupportAbility)MemberwiseClone();
}

/// <summary>The abilities chosen for one class; every class keeps its own list.</summary>
public sealed class SupportClassSettings
{
    public SupportClass Class { get; set; }
    public List<SupportAbility> Abilities { get; set; } = new();
}

/// <summary>Following the leader.</summary>
public sealed class SupportFollowSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Distance the helper walks up to when following.</summary>
    public int FollowDistance { get; set; } = 2;

    /// <summary>Beyond this distance the helper stops fighting and catches up first.</summary>
    public int MaxLeaderDistance { get; set; } = 6;
}

/// <summary>Survival when the helper itself is badly hurt.</summary>
public sealed class SupportEmergencySettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Health % at or below which the helper stops everything to save itself.</summary>
    public int EmergencyHealth { get; set; } = 35;

    /// <summary>Health % at which the helper goes back to following and fighting.</summary>
    public int RecoveryHealth { get; set; } = 75;
}

/// <summary>Fighting alongside the leader.</summary>
public sealed class SupportCombatSettings
{
    public bool Enabled { get; set; } = true;
    public int AttackRange { get; set; } = 8;

    /// <summary>Ranged style: back away from enemies that get too close.</summary>
    public bool KeepDistance { get; set; } = true;

    /// <summary>Enemies at or within this distance make a ranged helper back off.</summary>
    public int MeleeDangerRange { get; set; } = 2;

    /// <summary>Distance a ranged helper backs off to.</summary>
    public int PreferredMinRange { get; set; } = 4;

    public bool AttackGray { get; set; } = true;
    public bool AttackRed { get; set; } = true;

    /// <summary>Time between attack commands while fighting.</summary>
    public int AttackIntervalMs { get; set; } = 2000;
}

/// <summary>Spoken commands from the leader.</summary>
public sealed class SupportCommandSettings
{
    public bool Enabled { get; set; } = true;
    public string FollowCommand { get; set; } = "follow me";
    public string HealCommand { get; set; } = "heal me";

    /// <summary>After "follow me", the helper only follows (no fighting) for this long.</summary>
    public int FollowOverrideMs { get; set; } = 5000;
}

/// <summary>
/// Per-character settings of the support agent, saved to <c>support_agent.json</c> in the character's profile folder.
/// Edited from the Support agent tab on the game thread; <see cref="SupportAgentManager" /> reads it every tick.
/// </summary>
public sealed class SupportAgentSave : JsonSave<SupportAgentSave>, INotifyPropertyChanged
{
    public const string SUPPORT_AGENT_FILE_NAME = "support_agent.json";

    public bool Enabled { get; set; }
    public SupportClass Class { get; set; } = SupportClass.Bard;

    /// <summary>Character the helper follows, protects and listens to.</summary>
    public uint LeaderSerial { get; set; }

    public string LeaderName { get; set; } = "";

    /// <summary>Show what the helper is doing, and any problems, as client-only text above its head.</summary>
    public bool ShowStatusOverhead { get; set; } = true;

    /// <summary>Health % below which the helper heals someone.</summary>
    public int HealThreshold { get; set; } = 80;

    /// <summary>Heal and cure friends-list members too, not only the helper and the leader.</summary>
    public bool HealFriends { get; set; } = true;

    /// <summary>Friends farther away than this are ignored for healing.</summary>
    public int HealRange { get; set; } = 10;

    public SupportFollowSettings Follow { get; set; } = new();
    public SupportEmergencySettings Emergency { get; set; } = new();
    public SupportCombatSettings Combat { get; set; } = new();
    public SupportCommandSettings Commands { get; set; } = new();
    public List<SupportClassSettings> Classes { get; set; } = new();

    protected override SettingsScope Scope => SettingsScope.Char;
    protected override string FileName => SUPPORT_AGENT_FILE_NAME;
    protected override JsonTypeInfo<SupportAgentSave> TypeInfo => SupportAgentJsonContext.Default.SupportAgentSave;

    /// <summary>The ability list for <paramref name="supportClass" />, created from its presets on first use.</summary>
    /// <param name="supportClass">Class to look up.</param>
    /// <returns>The class's settings; never null.</returns>
    public SupportClassSettings GetClassSettings(SupportClass supportClass)
    {
        foreach (SupportClassSettings settings in Classes)
        {
            if (settings.Class == supportClass)
                return settings;
        }

        var created = new SupportClassSettings { Class = supportClass, Abilities = SupportClassPresets.Create(supportClass) };
        Classes.Add(created);
        return created;
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(SupportAgentSave))]
internal partial class SupportAgentJsonContext : JsonSerializerContext
{
}
