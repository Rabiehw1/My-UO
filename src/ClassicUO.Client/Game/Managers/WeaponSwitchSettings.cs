#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using ClassicUO.Configuration;

namespace ClassicUO.Game.Managers;

internal enum WeaponLoadout
{
    Staff,
    Sword,
    Holy,
    Assassins,
    EarthElemental,
    DragonSlayer,
    Lance,
    OrcSlayer,
    FireKatana,
    VoidWeapon,
    TrollSlayer,
    AnimalSlayer,
    UndeadBane,
    GiantSlayer,
}

internal sealed record WeaponSet(uint Weapon = 0, uint Shield = 0);
internal sealed record MonsterLoadouts(WeaponLoadout Primary, WeaponLoadout? Alternate);
internal sealed record WeaponSwitchMonster(ushort Graphic, ushort? Hue, string Name, WeaponLoadout Primary, WeaponLoadout? Alternate = null)
{
    public string Key => $"{Graphic:X4}:{Hue?.ToString("X4") ?? "*"}";
}

internal static class WeaponSwitchCatalog
{
    public static string Label(WeaponLoadout loadout) => loadout switch
    {
        WeaponLoadout.Staff => "Staff of Haste",
        WeaponLoadout.Sword => "Sword",
        WeaponLoadout.Holy => "Holy Weapon",
        WeaponLoadout.Assassins => "Assassins Weapon",
        WeaponLoadout.EarthElemental => "Earth Elemental Weapon",
        WeaponLoadout.DragonSlayer => "Dragon Slayer Weapon",
        WeaponLoadout.Lance => "Banishing",
        WeaponLoadout.OrcSlayer => "Orc Slayer Weapon",
        WeaponLoadout.FireKatana => "Katana of the Fire Element",
        WeaponLoadout.VoidWeapon => "Void Weapon",
        WeaponLoadout.TrollSlayer => "Troll Slayer Weapon",
        WeaponLoadout.AnimalSlayer => "Animal Slayer Weapon",
        WeaponLoadout.UndeadBane => "Undead Slayer",
        WeaponLoadout.GiantSlayer => "Giant Slayer Weapon",
        _ => loadout.ToString()
    };

    public static IReadOnlyList<WeaponSwitchMonster> Monsters { get; } =
    [
        new(0x0310, 0x0000, "Arcane Daemon", WeaponLoadout.Staff, WeaponLoadout.Holy),
        new(0x0009, 0x069E, "Flaming Balron", WeaponLoadout.Staff, WeaponLoadout.Holy),
        new(0x002B, 0x0000, "Frost Balron", WeaponLoadout.Staff, WeaponLoadout.Holy),
        new(0x0009, 0x4001, "Balron", WeaponLoadout.Staff, WeaponLoadout.Holy),
        new(0x0026, 0x07A1, "Balrog", WeaponLoadout.Staff, WeaponLoadout.Holy),
        new(0x0026, 0x0000, "Balron General", WeaponLoadout.Staff, WeaponLoadout.Holy),
        new(0x0139, 0x0000, "Darknight Creeper", WeaponLoadout.Staff, WeaponLoadout.Holy),
        new(0x00AF, 0x0000, "Corrupt Angel", WeaponLoadout.Staff, WeaponLoadout.Holy),
        new(0x0095, 0x0000, "Succubus", WeaponLoadout.Staff, WeaponLoadout.Holy),
        new(0x0028, 0x0000, "Fallen Angel", WeaponLoadout.Staff, WeaponLoadout.Holy),
        new(0x0138, 0x0000, "Abysmal Horror", WeaponLoadout.Staff, WeaponLoadout.Holy),
        new(0x0009, 0x0519, "Infernal Balron", WeaponLoadout.Staff, WeaponLoadout.Holy),
        new(0x00C8, 0x0502, "Banehorn", WeaponLoadout.Staff, WeaponLoadout.AnimalSlayer),
        new(0x00DC, 0x0494, "Blama", WeaponLoadout.Staff, WeaponLoadout.AnimalSlayer),
        new(0x0051, 0x085F, "Wizard Bullfrog", WeaponLoadout.Staff, WeaponLoadout.AnimalSlayer),
        new(0x000E, 0x0494, "Terramental", WeaponLoadout.Staff),
        new(0x0172, null, "Horned Rat", WeaponLoadout.Staff),
        new(0x0092, 0x0000, "Wraith Lord", WeaponLoadout.Staff),
        new(0x013A, 0x0000, "Ravager", WeaponLoadout.Holy),
        new(0x02EA, 0x0000, "Water Daemon Lord", WeaponLoadout.Holy),
        new(0x0102, 0x0000, "Lady Melisande", WeaponLoadout.Holy),
        new(0x0103, 0x099D, "Monstrous Interred Grizzle", WeaponLoadout.Holy),
        new(0x0188, 0x0000, "Chimera", WeaponLoadout.Holy),
        new(0x013E, 0x0000, "Darkfather", WeaponLoadout.Holy),
        new(0x013B, 0x0000, "Flesh Renderer", WeaponLoadout.Holy),
        new(0x0137, 0x0000, "Shadow Knight", WeaponLoadout.Holy),
        new(0x0191, 0x049E, "Archangel", WeaponLoadout.Assassins),
        new(0x0190, 0x049E, "Archangel", WeaponLoadout.Assassins),
        new(0x0190, 0x0488, "Scourge Battlemaster / Infiltrator", WeaponLoadout.Assassins),
        new(0x0190, 0x0485, "Gin the Legendary Hunter", WeaponLoadout.Assassins),
        new(0x0190, 0x02DD, "Scourge Battlemaster", WeaponLoadout.Assassins),
        new(0x0190, 0x0001, "Scourge Terminator", WeaponLoadout.Assassins),
        new(0x0190, 0x0000, "Dark Lancer", WeaponLoadout.Assassins),
        new(0x000C, 0x4631, "Shadow Dragon", WeaponLoadout.EarthElemental),
        new(0x002E, 0x084D, "Scourge Wyrm", WeaponLoadout.DragonSlayer),
        new(0x0179, 0x0000, "Dragon King", WeaponLoadout.DragonSlayer),
        new(0x031E, 0x0000, "Tiamat", WeaponLoadout.DragonSlayer),
        new(0x00EC, 0x0000, "Dragon Queen", WeaponLoadout.DragonSlayer),
        new(0x000C, 0x08FD, "Silver Dragon", WeaponLoadout.DragonSlayer),
        new(0x003B, 0x0487, "Great Wyrm", WeaponLoadout.DragonSlayer),
        new(0x00E0, 0x0000, "Dragon Lord", WeaponLoadout.DragonSlayer),
        new(0x004C, 0x086F, "Titan Warmaster", WeaponLoadout.FireKatana),
        new(0x0129, 0x0000, "Sapling of the Worldtree", WeaponLoadout.FireKatana),
        new(0x0035, 0x0498, "Finntroll", WeaponLoadout.TrollSlayer),
        new(0x0101, 0x0000, "Dread Horn", WeaponLoadout.AnimalSlayer),
        new(0x0174, 0x0000, "Remorhaz", WeaponLoadout.AnimalSlayer),
        new(0x00FA, 0x0000, "Storm Beast", WeaponLoadout.AnimalSlayer),
        new(0x0115, 0x0000, "Storm Wolf", WeaponLoadout.AnimalSlayer),
        new(0x0109, 0x0000, "Hydra", WeaponLoadout.AnimalSlayer),
        new(0x0175, 0x0000, "Rhinoceros Beetle", WeaponLoadout.AnimalSlayer),
        new(0x0018, null, "Liche Monsters", WeaponLoadout.UndeadBane),
        new(0x0134, 0x0000, "Greater Bone Daemon", WeaponLoadout.UndeadBane),
        new(0x017D, 0x0000, "Termuligin", WeaponLoadout.UndeadBane),
        new(0x00FC, 0x0000, "The Summoner", WeaponLoadout.UndeadBane),
        new(0x0160, 0x0000, "Ancient Thunder God", WeaponLoadout.GiantSlayer),
        new(0x0160, 0x086F, "Scourge Commander", WeaponLoadout.GiantSlayer),
        new(0x015F, 0x0000, "Ancient Earth God", WeaponLoadout.GiantSlayer),
        new(0x02FB, 0x0799, "Vortex Beast", WeaponLoadout.GiantSlayer),
        new(0x0171, 0x0870, "Hulk", WeaponLoadout.GiantSlayer),
        new(0x015F, 0x0866, "Scourge Gladiator", WeaponLoadout.GiantSlayer),
        new(0x010B, 0x0000, "Sir Tom the Hunchback", WeaponLoadout.GiantSlayer),
        new(0x00BD, 0x0000, "Orc Brute", WeaponLoadout.OrcSlayer),
        new(0x0169, 0x0000, "Naga Queen", WeaponLoadout.OrcSlayer),
    ];

    public static WeaponSwitchMonster? Find(ushort graphic, ushort? hue) =>
        Monsters.FirstOrDefault(monster => monster.Graphic == graphic && monster.Hue == hue)
        ?? Monsters.FirstOrDefault(monster => monster.Graphic == graphic && monster.Hue == null);
}

internal sealed class WeaponSwitchSettings : JsonSave<WeaponSwitchSettings>, INotifyPropertyChanged
{
    public uint WeaponBag { get; set; }
    public WeaponLoadout DefaultLoadout { get; set; } = WeaponLoadout.Sword;
    public Dictionary<WeaponLoadout, WeaponSet> Loadouts { get; set; } = new();
    public Dictionary<WeaponLoadout, string> CustomLoadouts { get; set; } = [];
    public List<WeaponSwitchMonster> CustomMonsters { get; set; } = [];
    public Dictionary<string, MonsterLoadouts> Assignments { get; set; } = new();

    protected override SettingsScope Scope => SettingsScope.Char;
    protected override string FileName => "weapon_switch.json";
    protected override JsonTypeInfo<WeaponSwitchSettings> TypeInfo => WeaponSwitchJsonContext.Default.WeaponSwitchSettings;

    public WeaponSwitchMonster? FindMonster(ushort graphic, ushort hue)
    {
        var builtIn = WeaponSwitchCatalog.Find(graphic, hue);
        return CustomMonsters.Find(monster => monster.Graphic == graphic && monster.Hue == hue)
            ?? (builtIn?.Hue != null ? builtIn : null)
            ?? CustomMonsters.Find(monster => monster.Graphic == graphic && monster.Hue == null)
            ?? builtIn;
    }

    public bool ContainsMonster(ushort graphic, ushort? hue) =>
        WeaponSwitchCatalog.Monsters.Concat(CustomMonsters).Any(monster => monster.Graphic == graphic && monster.Hue == hue);

    public bool AddMonster(ushort graphic, ushort? hue, string name)
    {
        if (graphic == 0 || string.IsNullOrWhiteSpace(name)
            || ContainsMonster(graphic, hue))
            return false;

        CustomMonsters.Add(new(graphic, hue, name.Trim(), DefaultLoadout));
        Save();
        return true;
    }

    public IEnumerable<WeaponLoadout> GetLoadouts() => Enum.GetValues<WeaponLoadout>().Concat(CustomLoadouts.Keys);

    public string GetLoadoutLabel(WeaponLoadout loadout) =>
        CustomLoadouts.GetValueOrDefault(loadout) ?? WeaponSwitchCatalog.Label(loadout);

    public bool CanAddLoadout(string name) => !string.IsNullOrWhiteSpace(name)
        && !name.Trim().Equals("None", StringComparison.OrdinalIgnoreCase)
        && !GetLoadouts().Any(loadout => GetLoadoutLabel(loadout).Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    public WeaponLoadout AddLoadout(string name)
    {
        var id = (WeaponLoadout)(Math.Max(999, GetLoadouts().Max(loadout => (int)loadout)) + 1);
        CustomLoadouts.Add(id, name.Trim());
        Save();
        return id;
    }

    public WeaponSet GetSet(WeaponLoadout loadout) => Loadouts.GetValueOrDefault(loadout) ?? new();

    public MonsterLoadouts GetAssignment(WeaponSwitchMonster monster) =>
        Assignments.GetValueOrDefault(monster.Key) ?? new(monster.Primary, monster.Alternate);
}

[JsonSerializable(typeof(WeaponSwitchSettings))]
internal partial class WeaponSwitchJsonContext : JsonSerializerContext;
