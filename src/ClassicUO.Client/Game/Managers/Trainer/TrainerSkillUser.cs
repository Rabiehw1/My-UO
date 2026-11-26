#nullable enable

using System;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;

namespace ClassicUO.Game.Managers.Trainer;

/// <summary>Performs one use of a trainer skill. Main thread only.</summary>
internal static class TrainerSkillUser
{
    #region Private members

    // How long the auto-target waits for the skill's cursor before it stops answering cursors.
    private const int TARGET_TIMEOUT_MS = 3000;

    // Forensic Evaluation reach on Zulu Hotel.
    private const int CORPSE_RANGE = 2;

    private static readonly ushort[] InstrumentGraphics = [0x0EB1, 0x0EB2, 0x0EB3, 0x0EB4, 0x0E9C, 0x0E9D, 0x0E9E];

    #endregion

    #region Public methods

    /// <summary>
    /// Whether another feature owns the target cursor right now. A skill whose cursor would arrive meanwhile
    /// could be answered with that feature's target, so the trainer waits.
    /// </summary>
    /// <param name="world">Game world.</param>
    /// <returns>True while a cursor is up or another auto-target is pending.</returns>
    public static bool CursorBusy(World world) => TargetManager.NextAutoTarget.IsSet || world.TargetManager.IsTargeting;

    /// <summary>Uses <paramref name="skill"/> once.</summary>
    /// <param name="world">Game world with a player.</param>
    /// <param name="skill">Skill to use.</param>
    /// <param name="target">Picked target serial, for <see cref="TrainerUse.Picked"/>.</param>
    /// <returns>True when something was sent; false (with a message) when this turn had to be skipped.</returns>
    public static bool Use(World world, TrainerSkill skill, uint target)
    {
        PlayerMobile player = world.Player!;
        switch (skill.Use)
        {
            case TrainerUse.Blacksmith:
                return BlacksmithTrainer.Use(world, target);

            case TrainerUse.Self:
                return UseSkill(world, skill.Name, player.Serial);

            case TrainerUse.Picked:
                return UseSkill(world, skill.Name, target);

            case TrainerUse.Instrument:
                return PlayInstrument(world);

            case TrainerUse.StealthOrHide:
                return UseSkill(world, player.IsHidden ? "Stealth" : "Hiding", 0);

            case TrainerUse.NearestCorpse:
                uint corpse = FindNearestCorpse(world);
                if (corpse == 0)
                {
                    GameActions.Print(world, TazLang.Get("trainer_no_corpse"), Constants.HUE_WARN);
                    return false;
                }

                return UseSkill(world, skill.Name, corpse);

            case TrainerUse.Meditation:
                if (player.Mana >= player.ManaMax)
                    GameActions.Print(world, TazLang.Get("trainer_mana_full"), Constants.HUE_WARN);

                return UseSkill(world, skill.Name, 0);

            default:
                return UseSkill(world, skill.Name, 0);
        }
    }

    #endregion

    #region Private methods

    /// <summary>Uses a skill by name, answering its cursor with <paramref name="target"/> when not 0.</summary>
    private static bool UseSkill(World world, string name, uint target)
    {
        int index = FindSkillIndex(world.Player!, name);
        if (index < 0)
        {
            GameActions.Print(world, TazLang.Get("trainer_skill_not_known", [name]), Constants.HUE_WARN);
            return false;
        }

        // Match any cursor type: shards differ in which type their skill cursors use.
        if (target != 0)
            TargetManager.SetAutoTarget(target, TargetType.Neutral, matchAnyTargetType: true, TARGET_TIMEOUT_MS);

        GameActions.UseSkill(index);
        return true;
    }

    private static int FindSkillIndex(PlayerMobile player, string name)
    {
        Skill[]? skills = player.Skills;
        if (skills == null)
            return -1;

        for (int i = 0; i < skills.Length; i++)
            if (string.Equals(skills[i]?.Name, name, StringComparison.OrdinalIgnoreCase))
                return skills[i].Index;

        return -1;
    }

    private static bool PlayInstrument(World world)
    {
        foreach (ushort graphic in InstrumentGraphics)
        {
            if (world.Player!.FindItemByGraphic(graphic) is { } instrument)
            {
                GameActions.DoubleClick(world, instrument.Serial);
                return true;
            }
        }

        GameActions.Print(world, TazLang.Get("trainer_no_instrument"), Constants.HUE_WARN);
        return false;
    }

    private static uint FindNearestCorpse(World world)
    {
        uint best = 0;
        int bestDistance = int.MaxValue;
        foreach (Item item in world.Items.Values)
        {
            if (!item.IsCorpse || !item.OnGround || item.IsDestroyed)
                continue;

            int distance = item.Distance;
            if (distance <= CORPSE_RANGE && distance < bestDistance)
            {
                best = item.Serial;
                bestDistance = distance;
            }
        }

        return best;
    }

    #endregion
}
