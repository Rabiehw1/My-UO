#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.IO;
using ClassicUO.Network;

namespace ClassicUO.Game.Managers.Trainer;

internal static class PracticeTrainer
{
    private enum Phase { Idle, Approach, Target, Menu, Taming }
    private sealed record Recipe(string Name, int Skill, int Amount, string Category);
    private sealed record Potion(string Family, ushort Graphic, int Skill, int Amount);
    private static readonly Recipe BlackStaff = new("Black Staff", 94, 10, "Weapons");
    // Measured Black Staff minimums from the shard crafting menu, keyed by log hue.
    private static readonly Dictionary<ushort, int> BlackStaffMinimums = new()
    {
        [0x0000] = 94,
        [0x046C] = 97,
        [0x0654] = 101,
        [0x0415] = 103,
        [0x0203] = 106,
        [0x0030] = 108,
        [0x0505] = 110,
        [0x048A] = 111,
        [0x0455] = 113,
        [0x0482] = 115,
        [0x0498] = 116,
        [0x0504] = 117,
        [0x0499] = 118,
        [0x0489] = 120,
        [0x08FD] = 121,
        [0x0542] = 122,
        [0x0487] = 123,
        [0x066D] = 124,
        [0x0507] = 125,
        [0x0492] = 125,
        [0x049F] = 126,
        [0x0501] = 126,
        [0x0488] = 126,
        [0x0485] = 129,
        [0x048D] = 130,
    };

    private static readonly Recipe[] Carpentry =
    [
        new("Barrel Staves", 2, 5, "Miscellaneous"), new("Bench", 10, 10, "Furniture"),
        new("Barrel Lid", 21, 4, "Miscellaneous"), new("Foot Stool", 28, 10, "Furniture"),
        new("Box", 44, 10, "Containers"), new("Strong Box", 64, 15, "Containers"),
        new("Chest", 84, 20, "Containers"), new("Gnarled Staff", 89, 7, "Weapons"),
        BlackStaff, new("Club", 99, 7, "Weapons"),
        new("Dresser", 100, 50, "Furniture")
    ];
    private static readonly Recipe[] Bows =
    [
        new("Kindling", 5, 1, "Miscellaneous"), new("Shafts", 10, 1, "Ammunition"),
        new("Bow", 20, 7, "Bows"), new("Crossbow", 50, 7, "Bows"), new("Heavy Crossbow", 80, 10, "Bows")
    ];
    private static readonly Potion[] Potions =
    [
        new("heal", 0x0F85, 14, 1), new("heal", 0x0F85, 60, 4), new("heal", 0x0F85, 85, 9),
        new("cure", 0x0F84, 15, 1), new("cure", 0x0F84, 50, 4), new("cure", 0x0F84, 90, 9),
        new("refresh", 0x0F7A, 10, 1), new("refresh", 0x0F7A, 50, 9),
        new("agility", 0x0F7B, 10, 1), new("agility", 0x0F7B, 30, 3), new("agility", 0x0F7B, 70, 9),
        new("strength", 0x0F86, 10, 1), new("strength", 0x0F86, 30, 3), new("strength", 0x0F86, 70, 7),
        new("night_sight", 0x0F8D, 10, 3), new("poison", 0x0F88, 15, 1), new("poison", 0x0F88, 50, 4),
        new("poison", 0x0F88, 90, 12), new("poison", 0x0F88, 100, 15),
        new("explosion", 0x0F8C, 25, 3), new("explosion", 0x0F8C, 70, 6), new("explosion", 0x0F8C, 90, 20)
    ];
    internal static IEnumerable<string> PotionFamilies => Potions.Select(p => p.Family).Distinct();
    private static readonly Dictionary<string, int> Animals = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dog"] = 3,
        ["rabbit"] = 5,
        ["bird"] = 5,
        ["gorilla"] = 5,
        ["squirrel"] = 5,
        ["ferret"] = 5,
        ["chicken"] = 5,
        ["raven"] = 10,
        ["bird dog"] = 10,
        ["hunting dog"] = 10,
        ["guard dog"] = 10,
        ["slime"] = 20,
        ["bull frog"] = 20,
        ["cat"] = 20,
        ["rat"] = 20,
        ["eagle"] = 25,
        ["mongbat"] = 25,
        ["pig"] = 25,
        ["firecrow"] = 25,
        ["sheep"] = 30,
        ["goat"] = 30,
        ["cow"] = 30,
        ["rabid cat"] = 30,
        ["giant rat"] = 35,
        ["hildisvini"] = 35,
        ["rabid dog"] = 35,
        ["golden hawk"] = 35,
        ["plagued bat"] = 35,
        ["bat"] = 35,
        ["hind"] = 40,
        ["pack horse"] = 40,
        ["horse"] = 45,
        ["desert ostard"] = 45,
        ["pack llama"] = 45,
        ["black slime"] = 45,
        ["green slime"] = 45,
        ["swamp boar"] = 45,
        ["plains ostard"] = 45,
        ["brown bear"] = 50,
        ["panda"] = 50,
        ["forest ostard"] = 50,
        ["llama"] = 50,
        ["walrus"] = 50,
        ["war dog"] = 50,
        ["golden ostard"] = 50,
        ["black bear"] = 55,
        ["timber wolf"] = 55,
        ["swamp ostard"] = 55,
        ["valley ostard"] = 55,
        ["rabid rabbit"] = 55,
        ["plum ostard"] = 55,
        ["wind ostard"] = 55,
        ["snake"] = 60,
        ["alligator"] = 60,
        ["grizzly bear"] = 60,
        ["hart"] = 60,
        ["cougar"] = 60,
        ["gremlin"] = 60,
        ["mist ostard"] = 60,
        ["polar bear"] = 65,
        ["panther"] = 65,
        ["highland ostard"] = 65,
        ["shadow ostard"] = 65,
        ["terror ostard"] = 65,
        ["plum frenzied ostard"] = 65,
        ["wind frenzied ostard"] = 65,
        ["giant beetle"] = 70,
        ["giant spider"] = 70,
        ["giant scorpion"] = 70,
        ["bull"] = 70,
        ["storm wolf"] = 75,
        ["rock lizard"] = 75,
        ["frost wolf"] = 75,
        ["mountain ostard"] = 75,
        ["stone ostard"] = 75,
        ["emerald ostard"] = 75,
        ["ruby ostard"] = 75,
        ["amethyst ostard"] = 75,
        ["sapphire ostard"] = 75,
        ["phased ostard"] = 75,
        ["terror frenzied ostard"] = 75,
        ["jungle ostard"] = 75,
        ["water ostard"] = 75,
        ["giant serpent"] = 80,
        ["giant toad"] = 80,
        ["dire wolf"] = 80,
        ["giant rock spider"] = 80,
        ["giant rock scorpion"] = 80,
        ["giant frost spider"] = 80,
        ["golden frenzied ostard"] = 80,
        ["plains frenzied ostard"] = 80,
        ["swamp frenzied ostard"] = 80,
        ["mist frenzied ostard"] = 80,
        ["blood ostard"] = 80,
        ["ridgeback"] = 85,
        ["skittering hopper"] = 85,
        ["frenzied ostard"] = 85,
        ["highland frenzied ostard"] = 85,
        ["shadow frenzied ostard"] = 85,
        ["valley frenzied ostard"] = 85,
        ["phased frenzied ostard"] = 85,
        ["obsidian ostard"] = 85,
        ["fire lizard"] = 90,
        ["imp"] = 90,
        ["giant flaming spider"] = 90,
        ["fire serpent"] = 90,
        ["blood frenzied ostard"] = 90,
        ["vampire bat"] = 95,
        ["phase spider"] = 95,
        ["hell hound"] = 95,
        ["raptor"] = 95,
        ["mountain frenzied ostard"] = 95,
        ["stone frenzied ostard"] = 95,
        ["emerald frenzied ostard"] = 95,
        ["ruby frenzied ostard"] = 95,
        ["snow ostard"] = 95,
        ["ice ostard"] = 95,
        ["amethyst frenzied ostard"] = 95,
        ["sapphire frenzied ostard"] = 95,
        ["water frenzied ostard"] = 95,
        ["tropical frenzied ostard"] = 105,
        ["snow frenzied ostard"] = 105,
        ["ice frenzied ostard"] = 105,
        ["tropical ostard"] = 105,
        ["heavenly ostard"] = 105,
        ["phoenix"] = 105,
        ["drake"] = 110,
        ["heavenly frenzied ostard"] = 110,
        ["earth ostard"] = 110,
        ["necro ostard"] = 110,
        ["jungle frenzied ostard"] = 110,
        ["obsidian frenzied ostard"] = 110,
        ["skeletal drake"] = 115,
        ["frost drake"] = 115,
        ["fire frenzied ostard"] = 115,
        ["fire ostard"] = 115,
        ["fire drake"] = 115,
        ["water drake"] = 115,
        ["earth drake"] = 115,
        ["air drake"] = 115,
        ["undead drake"] = 115,
        ["silver serpent"] = 115,
        ["mongrel dog"] = 115,
        ["tidal drake"] = 115,
        ["infernal drake"] = 115,
        ["sand drake"] = 115,
        ["celestial drake"] = 115,
        ["rock drake"] = 115,
        ["sphinx"] = 120,
        ["skeletal horse"] = 120,
        ["spectral drake"] = 120,
        ["giant sand worm"] = 120,
        ["snapjaw"] = 120,
        ["wyvern"] = 120,
        ["poison drake"] = 120,
        ["earth frenzied ostard"] = 120,
        ["necro frenzied ostard"] = 120,
        ["silver steed"] = 120,
        ["cave drake"] = 120,
        ["storm drake"] = 120,
        ["earth wyrm"] = 125,
        ["heavenly drake"] = 125,
        ["ethereal horse"] = 125,
        ["wyrm"] = 125,
        ["silver drake"] = 125,
        ["golden drake"] = 125,
        ["grave spider"] = 130,
        ["cave dragon"] = 130,
        ["dragon"] = 130,
        ["dracoliche"] = 135,
        ["poison dragon"] = 140,
        ["inferno dragon"] = 140,
        ["frost dragon"] = 140,
        ["tidal dragon"] = 140,
        ["rock dragon"] = 140,
        ["storm dragon"] = 140,
        ["sand dragon"] = 140,
        ["nightmare"] = 145,
        ["poisonmare"] = 145,
        ["undead mare"] = 145,
        ["infernal ostard"] = 145,
        ["shadow dragon"] = 147,
        ["unicorn"] = 150,
        ["omega horse"] = 150,
        ["omega llama"] = 150,
        ["omega frenzied ostard"] = 150,
        ["omega ostard"] = 150,
        ["infernal horse"] = 150,
        ["infernal llama"] = 150,
        ["infernal frenzied ostard"] = 150,
        ["ki-rin"] = 150,
        ["swamp dragonling"] = 150,
        ["armored swamp dragonling"] = 150,
        ["armored undead dragonling"] = 150,
        ["undead horse"] = 150,
        ["undead llama"] = 150,
        ["undead ostard"] = 150,
        ["undead frenzied ostard"] = 150,
        ["rainbow ostard"] = 150,
        ["rainbow frenzied ostard"] = 150,
        ["golden dragon"] = 150,
        ["silver dragom"] = 150,
        ["great wyrm"] = 150,
        ["great earth wyrm"] = 150,
    };

    private static readonly HashSet<uint> AttemptedAnimals = new();
    private static readonly Dictionary<(int, ushort, ushort), long> Mined = new();
    private static readonly HashSet<string> Warned = new();
    private static Phase _phase;
    private static Profile? _profile;
    private static TrainerSkill? _skill;
    private static Recipe? _recipe;
    private static uint _tool, _target;
    private static int _facet, _depth;
    private static long _deadline, _lockReplyUntil;
    private static uint _unlockedChest;
    private static World? _walkingWorld;
    private static Microsoft.Xna.Framework.Point _destination;
    private static (ushort X, ushort Y, short Z, ushort Graphic) _tile;

    private static bool Carried(World world, Item item) => !item.IsDestroyed &&
        (item.Container == world.Player.Serial || item.BackpackOrRootContainer == world.Player.Backpack?.Serial);

    internal static int BlackStaffRank(int skillFixed, ushort hue, int amount) =>
        amount >= BlackStaff.Amount && BlackStaffMinimums.TryGetValue(hue, out int minimum) &&
        skillFixed >= minimum * 10 ? minimum : -1;

    private static int SkillValue(World world, int index) =>
        world.Player.Skills?.FirstOrDefault(s => s?.Index == index)?.ValueFixed ?? 0;

    private static bool Warn(TrainerSkill skill)
    {
        if (Warned.Add(skill.Id))
            GameActions.Print(TazLang.Get("trainer_practice_setup", [skill.Name]), Constants.HUE_WARN);
        return false;
    }

    internal static bool Begin(World world, TrainerSkill skill, uint picked)
    {
        var player = world.Player;
        if (player.IsDead) return false;
        var profile = ProfileManager.CurrentProfile;
        var settings = profile.Trainer;
        uint tool = skill.Use == TrainerUse.Unlock ? settings.Lockpicks : picked;
        if (skill.Use != TrainerUse.Tame && (world.Items.Get(tool) is not { } item || !Carried(world, item)))
            return Warn(skill);

        uint target = 0;
        _recipe = null;
        switch (skill.Use)
        {
            case TrainerUse.Tame:
                if (player.FollowersMax == 0 || player.Followers >= player.FollowersMax) return false;
                int taming = SkillValue(world, 35);
                var animal = world.Mobiles.Values.Where(m => !m.IsDead && !m.IsDestroyed && !m.IsHuman &&
                    !m.IsRenamable && m.Distance <= 3 && !AttemptedAnimals.Contains(m.Serial) &&
                    Animals.TryGetValue(AnimalName(m), out int required) && required * 10 <= taming)
                    .OrderByDescending(m => Animals[AnimalName(m)]).ThenBy(m => m.Distance).FirstOrDefault();
                if (animal == null) return false;
                target = animal.Serial;
                break;
            case TrainerUse.Unlock:
                if (_unlockedChest == picked) return false;
                if (world.Items.Get(picked) is not { IsDestroyed: false, Distance: <= 2 } || picked == tool) return Warn(skill);
                target = picked;
                break;
            case TrainerUse.Gather:
                if (player.WeightMax == 0 || player.Weight >= player.WeightMax) return false;
                if (!FindMiningTile(world, settings.MiningGround))
                {
                    var ground = settings.MiningGround;
                    if (ground == null || ground.Facet != world.MapIndex || player.Pathfinder.AutoWalking ||
                        Math.Max(Math.Abs(player.X - ground.X), Math.Abs(player.Y - ground.Y)) is <= 2 or > 12)
                        return Warn(skill);
                    var tile = world.Map.GetTile(ground.X, ground.Y, false);
                    if (tile == null || !player.Pathfinder.WalkTo(ground.X, ground.Y, tile.Z, 2))
                        return Warn(skill);
                    _walkingWorld = world;
                    _destination = player.Pathfinder.EndPoint;
                    _profile = profile;
                    _skill = skill;
                    _facet = world.MapIndex;
                    _phase = Phase.Approach;
                    _deadline = Time.Ticks + 15000;
                    return true;
                }
                break;
            case TrainerUse.Craft:
                var supplies = world.Items.Values.Where(i => Carried(world, i)).ToArray();
                if (skill.Id == "alchemy")
                {
                    if (!supplies.Any(i => i.Graphic == 0x0F0E)) return Warn(skill);
                    int alchemy = SkillValue(world, 0);
                    foreach (var potion in Potions.OrderByDescending(p => p.Skill))
                    {
                        if (potion.Skill * 10 > alchemy || !settings.AlchemyFamilies.Contains(potion.Family))
                            continue;
                        var reagent = supplies.FirstOrDefault(i => i.Graphic == potion.Graphic && i.Amount >= potion.Amount);
                        if (reagent == null) continue;
                        target = reagent.Serial;
                        break;
                    }
                }
                else
                {
                    bool bows = skill.Id == "bowcraft";
                    int skillFixed = SkillValue(world, bows ? 8 : 11);
                    int reserve = bows ? settings.FletchingLogReserve : 0;
                    var wood = supplies.Where(i => i.Graphic is >= 0x1BDD and <= 0x1BE2).ToArray();
                    if (!bows)
                    {
                        int highest = -1;
                        foreach (var stack in wood)
                        {
                            int rank = BlackStaffRank(skillFixed, stack.Hue, stack.Amount);
                            if (rank <= highest)
                                continue;

                            highest = rank;
                            target = stack.Serial;
                            _recipe = BlackStaff;
                        }
                        if (target != 0)
                            break;
                    }
                    var logs = wood.Where(i => i.Hue == 0).ToArray();
                    int expendable = logs.Sum(i => i.Amount) - reserve;
                    foreach (var recipe in (bows ? Bows : Carpentry).Reverse())
                    {
                        if (recipe.Skill * 10 > skillFixed || recipe.Amount > expendable) continue;
                        var stack = logs.FirstOrDefault(i => i.Amount >= recipe.Amount);
                        if (stack == null) continue;
                        target = stack.Serial;
                        _recipe = recipe;
                        break;
                    }
                }
                if (target == 0) return Warn(skill);
                break;
        }

        Warned.Remove(skill.Id);
        _profile = profile;
        _skill = skill;
        _tool = tool;
        _target = target;
        _facet = world.MapIndex;
        _phase = Phase.Target;
        _depth = 0;
        _deadline = Time.Ticks + 3000;
        if (skill.Use == TrainerUse.Tame) GameActions.UseSkill(35);
        else GameActions.DoubleClick(world, tool, ignoreQueue: true);
        return true;
    }

    private static string AnimalName(Mobile animal)
    {
        string name = animal.Name?.Trim() ?? "";
        if (name.StartsWith("a ", StringComparison.OrdinalIgnoreCase)) return name[2..];
        return name.StartsWith("an ", StringComparison.OrdinalIgnoreCase) ? name[3..] : name;
    }

    private static bool FindMiningTile(World world, TrainerSettings.MiningTile? ground)
    {
        var player = world.Player;
        if (ground == null || ground.Facet != world.MapIndex) return false;
        for (int x = Math.Max(0, player.X - 2); x <= player.X + 2; x++)
            for (int y = Math.Max(0, player.Y - 2); y <= player.Y + 2; y++)
            {
                if (Math.Abs(x - ground.X) > 6 || Math.Abs(y - ground.Y) > 6) continue;
                if (Mined.TryGetValue((world.MapIndex, (ushort)x, (ushort)y), out long until) && Time.Ticks < until)
                    continue;
                for (var tile = world.Map.GetTile(x, y, false); tile != null; tile = tile.TNext)
                {
                    if (tile.Graphic != ground.Graphic || (ground.Land ? tile is not Land : tile is not Static)
                        || Math.Abs(tile.Z - player.Z) > 12 || !LineOfSightHelper.IsVisible(player, tile)) continue;
                    _tile = (tile.X, tile.Y, tile.Z, ground.Land ? (ushort)0 : tile.Graphic);
                    return true;
                }
            }
        return false;
    }

    internal static bool Waiting(World world)
    {
        if (_phase == Phase.Idle) return false;
        if (Time.Ticks < _deadline && world.Player is { IsDead: false } && world.MapIndex == _facet &&
            _profile != null && ReferenceEquals(_profile, ProfileManager.CurrentProfile) &&
            _profile.Trainer.Selected.Contains(_skill!.Id))
        {
            if (_phase != Phase.Approach ||
                (world.Player.Pathfinder.AutoWalking && world.Player.Pathfinder.EndPoint == _destination))
                return true;
        }
        StopApproach();
        _phase = Phase.Idle;
        return false;
    }

    internal static bool TryCursor(World world, CursorTarget cursor, TargetType type)
    {
        if (_phase != Phase.Target || type != TargetType.Neutral || !Waiting(world)) return false;
        bool mining = _skill!.Use == TrainerUse.Gather;
        if (cursor != (mining ? CursorTarget.Position : CursorTarget.Object)) return false;
        _phase = _recipe == null ? Phase.Idle : Phase.Menu;
        _deadline = Time.Ticks + 5000;
        TrainerManager.Instance.SkillSent();
        if (mining)
        {
            Mined[(world.MapIndex, _tile.X, _tile.Y)] = Time.Ticks + 60000;
            world.TargetManager.Target(_tile.Graphic, _tile.X, _tile.Y, _tile.Z);
        }
        else
        {
            if (_skill.Use == TrainerUse.Tame)
            {
                if (world.Mobiles.Get(_target) is not { IsDead: false, IsRenamable: false, Distance: <= 3 })
                {
                    world.TargetManager.CancelTarget();
                    return true;
                }
                AttemptedAnimals.Add(_target);
                _phase = Phase.Taming;
                _deadline = Time.Ticks + 12000;
            }
            else if (world.Items.Get(_target) is not { IsDestroyed: false } item ||
                (_skill.Use == TrainerUse.Craft ? !Carried(world, item) : item.Distance > 2))
            {
                _phase = Phase.Idle;
                world.TargetManager.CancelTarget();
                return true;
            }
            if (_skill.Use == TrainerUse.Unlock)
                _lockReplyUntil = Time.Ticks + 5000;
            world.TargetManager.Target(_target);
        }
        return true;
    }

    internal static bool TryMenu(World world, uint serial, ushort id, string title, int count, StackDataReader entries)
    {
        if (_phase != Phase.Menu || !Waiting(world) || _recipe is not { } recipe) return false;
        if (serial != _tool && serial != _target &&
            !title.Contains("what would you like to make", StringComparison.OrdinalIgnoreCase)) return false;
        ushort selected = 0, graphic = 0, hue = 0;
        bool direct = false, gray = false;
        for (ushort i = 1; i <= count; i++)
        {
            ushort g = entries.ReadUInt16BE(), h = entries.ReadUInt16BE();
            if (i == 1) gray = g == 0;
            string label = entries.ReadASCII(entries.ReadUInt8()).Trim();
            bool match = label.Equals(recipe.Name, StringComparison.OrdinalIgnoreCase);
            if (!match && (selected != 0 || _depth != 0 ||
                !label.Equals(recipe.Category, StringComparison.OrdinalIgnoreCase))) continue;
            (selected, graphic, hue, direct) = (i, g, h, match);
            if (match) break;
        }
        if (selected == 0) return false;
        _depth++;
        _phase = direct ? Phase.Idle : Phase.Menu;
        _deadline = Time.Ticks + 5000;
        if (direct) TrainerManager.Instance.SkillSent();
        if (gray) AsyncNetClient.Socket.Send_GrayMenuResponse(serial, id, selected);
        else AsyncNetClient.Socket.Send_MenuResponse(serial, id, selected, graphic, hue);
        return true;
    }

    internal static void OnJournal(string message)
    {
        if (Time.Ticks >= _lockReplyUntil || _skill?.Use != TrainerUse.Unlock)
            return;
        if (!message.Contains("the lock opens", StringComparison.OrdinalIgnoreCase) &&
            !message.Contains("not locked", StringComparison.OrdinalIgnoreCase) &&
            !message.Contains("successfully pick the lock", StringComparison.OrdinalIgnoreCase))
            return;
        _unlockedChest = _target;
        _lockReplyUntil = 0;
        GameActions.Print(TazLang.Get("trainer_lock_relock"), Constants.HUE_WARN);
    }

    private static void StopApproach()
    {
        if (_walkingWorld?.Player?.Pathfinder is { AutoWalking: true } path && path.EndPoint == _destination)
            path.StopAutoWalk();
        _walkingWorld = null;
    }

    internal static void Reset()
    {
        StopApproach();
        _phase = Phase.Idle;
        _profile = null;
        Warned.Clear();
        AttemptedAnimals.Clear();
        Mined.Clear();
        _lockReplyUntil = 0;
        _unlockedChest = 0;
    }
}
