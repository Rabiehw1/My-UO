#nullable enable

using System;
using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Utility;

namespace ClassicUO.Game.Managers.Support;

/// <summary>
/// Plays the logged-in character as a helper: keeps the leader and friends alive with its class's abilities, saves
/// itself when badly hurt, follows the leader, answers spoken commands and fights nearby enemies.
/// </summary>
/// <remarks>
/// Each tick takes the first matching step, in priority order:
/// emergency → recovery → spoken commands → cure → heal → buff → catch up with leader → fight → follow.
/// An ability marks the helper busy for its cast and recovery time, during which nothing else is started.
/// Driven from GameScene.Update() on the game thread; settings are read every tick, so tab edits apply at once.
/// </remarks>
internal sealed class SupportAgentManager : IDisposable
{
    #region Public accessors

    /// <summary>The agent for the current game session; created on first use.</summary>
    public static SupportAgentManager Instance
    {
        get
        {
            field ??= new SupportAgentManager();
            return field;
        }
        private set;
    }

    /// <summary>Settings of the logged-in character, loaded on first use.</summary>
    public SupportAgentSave Settings => _settings ??= SupportAgentSave.Load();

    #endregion

    #region Private members

    /// <summary>Decision cadence; matches the sample script's check interval.</summary>
    private const int TICK_MS = 250;

    /// <summary>Bandages only reach patients this close.</summary>
    private const int BANDAGE_RANGE = 2;

    private readonly World _world;
    private readonly SupportAbilityRunner _runner;
    private readonly List<Mobile> _group = new();

    private SupportAgentSave? _settings;
    private long _nextTick;
    private long _busyUntil;
    private long _lastAttack;
    private long _followOverrideUntil;
    private uint _currentEnemy;
    private bool _retreating;
    private bool _wasEnabled;
    private bool _warModeByAgent;

    // Set by spoken commands from the leader, consumed on the next tick.
    private bool _followRequested;
    private bool _healRequested;

    #endregion

    #region Ctor

    private SupportAgentManager()
    {
        _world = World.Instance;
        _runner = new SupportAbilityRunner(_world);
        EventSink.MessageReceived += OnMessageReceived;
    }

    #endregion

    #region Public methods

    /// <summary>Runs the agent. Call every frame; decisions are throttled to <see cref="TICK_MS" />.</summary>
    public void Update()
    {
        long now = Time.Ticks;
        _runner.Update(now);

        if (now < _nextTick)
            return;

        _nextTick = now + TICK_MS;

        PlayerMobile? player = _world.Player;

        if (!_world.InGame || player == null)
            return;

        SupportAgentSave settings = Settings;

        if (!settings.Enabled || player.IsDead)
        {
            if (_wasEnabled)
                Stop(player);

            return;
        }

        _wasEnabled = true;

        if (now >= _busyUntil)
            Tick(player, settings, now);
    }

    /// <summary>Makes <paramref name="leader" /> the character to follow, protect and obey, and saves.</summary>
    /// <param name="leader">The new leader.</param>
    public void SetLeader(Mobile leader)
    {
        Settings.LeaderSerial = leader.Serial;
        Settings.LeaderName = leader.Name ?? "";
        Settings.Save();
        _currentEnemy = 0;
    }

    /// <summary>Saves settings and detaches from game events; called when the game scene unloads.</summary>
    public void Dispose()
    {
        EventSink.MessageReceived -= OnMessageReceived;
        _settings?.Save();
        Instance = null!;
    }

    #endregion

    #region Private methods

    private void Tick(PlayerMobile player, SupportAgentSave settings, long now)
    {
        SupportClassSettings classSettings = settings.GetClassSettings(settings.Class);
        Mobile? leader = settings.LeaderSerial != 0 ? _world.Mobiles.Get(settings.LeaderSerial) : null;

        if (leader is { IsDestroyed: true })
            leader = null;

        if (HandleEmergency(player, settings, classSettings, leader, now))
            return;

        if (HandleCommands(player, settings, classSettings, leader, now))
            return;

        BuildGroup(player, leader, settings);

        if (TryCure(player, settings, classSettings, leader, now) || TryHeal(player, settings, classSettings, leader, now, null))
            return;

        if (TryBuff(player, classSettings, leader, now))
            return;

        // With a leader set but out of sight, wait for them rather than wander off fighting.
        if (settings.LeaderSerial != 0 && leader == null)
            return;

        if (leader != null && (now < _followOverrideUntil || leader.Distance > settings.Follow.MaxLeaderDistance))
        {
            _currentEnemy = 0;
            SetWarMode(player, false);
            FollowLeader(player, leader, settings);
            return;
        }

        if (settings.Combat.Enabled && Fight(player, settings, leader, now))
            return;

        _currentEnemy = 0;
        SetWarMode(player, false);

        if (leader != null)
            FollowLeader(player, leader, settings);
    }

    /// <returns>True when the helper is saving itself this tick.</returns>
    private bool HandleEmergency(
        PlayerMobile player,
        SupportAgentSave settings,
        SupportClassSettings classSettings,
        Mobile? leader,
        long now
    )
    {
        SupportEmergencySettings emergency = settings.Emergency;

        if (!emergency.Enabled)
        {
            _retreating = false;
            return false;
        }

        int health = HealthPercent(player);

        if (!_retreating && health <= emergency.EmergencyHealth)
        {
            _retreating = true;
            _currentEnemy = 0;
            StopMoving(player);
            SetWarMode(player, false);
            GameActions.Print(TazLang.Get("supportagent_emergency"), Constants.HUE_WARN);

            if (TryUseRole(classSettings, SupportRole.Emergency, player, leader, null, now))
                return true;
        }

        if (!_retreating)
            return false;

        if (health >= emergency.RecoveryHealth)
        {
            _retreating = false;
            GameActions.Print(TazLang.Get("supportagent_recovered"));
            return false;
        }

        StopMoving(player);
        SetWarMode(player, false);
        TryUseRole(classSettings, SupportRole.Heal, player, leader, player, now);
        return true;
    }

    /// <returns>True when a command used up this tick.</returns>
    private bool HandleCommands(
        PlayerMobile player,
        SupportAgentSave settings,
        SupportClassSettings classSettings,
        Mobile? leader,
        long now
    )
    {
        if (_followRequested)
        {
            _followRequested = false;
            _currentEnemy = 0;
            _lastAttack = 0;
            SetWarMode(player, false);
            _followOverrideUntil = now + settings.Commands.FollowOverrideMs;
            GameActions.Print(TazLang.Get("supportagent_cmd_follow"));
        }

        if (!_healRequested)
            return false;

        _healRequested = false;
        _currentEnemy = 0;
        _lastAttack = 0;
        SetWarMode(player, false);
        GameActions.Print(TazLang.Get("supportagent_cmd_heal"));

        // The leader asked, so heal them whatever their health.
        if (TryUseRole(classSettings, SupportRole.Heal, player, leader, leader ?? player, now))
        {
            _followOverrideUntil = now + settings.Commands.FollowOverrideMs;
            return true;
        }

        return false;
    }

    private bool TryCure(
        PlayerMobile player,
        SupportAgentSave settings,
        SupportClassSettings classSettings,
        Mobile? leader,
        long now
    )
    {
        foreach (Mobile member in _group)
        {
            if (member.IsPoisoned && TryUseRole(classSettings, SupportRole.Cure, player, leader, member, now))
                return true;
        }

        return false;
    }

    /// <param name="patient">Who to heal; null picks the most hurt group member below the threshold.</param>
    private bool TryHeal(
        PlayerMobile player,
        SupportAgentSave settings,
        SupportClassSettings classSettings,
        Mobile? leader,
        long now,
        Mobile? patient
    )
    {
        patient ??= FindMostHurt(settings.HealThreshold);

        if (patient == null)
            return false;

        return TryUseRole(classSettings, SupportRole.Heal, player, leader, patient, now);
    }

    private bool TryBuff(PlayerMobile player, SupportClassSettings classSettings, Mobile? leader, long now) =>
        TryUseRole(classSettings, SupportRole.Buff, player, leader, null, now);

    /// <summary>Uses the first enabled, ready ability of <paramref name="role" /> that has a valid target.</summary>
    private bool TryUseRole(
        SupportClassSettings classSettings,
        SupportRole role,
        PlayerMobile player,
        Mobile? leader,
        Mobile? patient,
        long now
    )
    {
        foreach (SupportAbility ability in classSettings.Abilities)
        {
            if (!ability.Enabled || ability.Role != role)
                continue;

            if (!_runner.IsOffCooldown(ability, now) || !_runner.HasRequirements(ability))
                continue;

            if (!TryResolveTarget(ability, player, leader, patient, out uint target))
                continue;

            // Casting while walking gets interrupted, so stand still first.
            if (ability.CastTimeMs > 0)
                StopMoving(player);

            if (_runner.TryUse(ability, target, now))
            {
                _busyUntil = now + ability.CastTimeMs + ability.RecoveryMs;
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveTarget(
        SupportAbility ability,
        PlayerMobile player,
        Mobile? leader,
        Mobile? patient,
        out uint target
    )
    {
        Mobile? mobile = ability.Target switch
        {
            SupportTargetMode.Self => player,
            SupportTargetMode.Leader => leader,
            SupportTargetMode.Patient => patient ?? player,
            _ => null
        };

        target = mobile?.Serial ?? 0;

        if (ability.Target == SupportTargetMode.None)
            return true;

        if (mobile == null || mobile.IsDead)
            return false;

        return ability.Action != SupportActionType.Bandage || mobile.Distance <= BANDAGE_RANGE;
    }

    /// <summary>Fills <see cref="_group" /> with the helper, the leader and nearby living friends.</summary>
    private void BuildGroup(PlayerMobile player, Mobile? leader, SupportAgentSave settings)
    {
        _group.Clear();
        _group.Add(player);

        if (leader != null && !leader.IsDead && leader.Distance <= settings.HealRange)
            _group.Add(leader);

        if (!settings.HealFriends)
            return;

        foreach (uint serial in FriendsListManager.Instance.GetAllFriends())
        {
            if (serial == player.Serial || serial == settings.LeaderSerial)
                continue;

            Mobile? friend = _world.Mobiles.Get(serial);

            if (friend is { IsDestroyed: false, IsDead: false } && friend.HitsMax > 0 && friend.Distance <= settings.HealRange)
                _group.Add(friend);
        }
    }

    private Mobile? FindMostHurt(int threshold)
    {
        Mobile? mostHurt = null;
        int lowest = threshold;

        foreach (Mobile member in _group)
        {
            int health = HealthPercent(member);

            if (health < lowest)
            {
                lowest = health;
                mostHurt = member;
            }
        }

        return mostHurt;
    }

    /// <returns>True while an enemy is being fought or avoided.</returns>
    private bool Fight(PlayerMobile player, SupportAgentSave settings, Mobile? leader, long now)
    {
        SupportCombatSettings combat = settings.Combat;
        Mobile? enemy = _currentEnemy != 0 ? _world.Mobiles.Get(_currentEnemy) : null;

        if (enemy == null || !IsValidEnemy(enemy, player, settings))
            enemy = FindNearestEnemy(player, settings);

        _currentEnemy = enemy?.Serial ?? 0;

        if (enemy == null)
            return false;

        if (combat.KeepDistance && enemy.Distance <= combat.MeleeDangerRange)
        {
            SetWarMode(player, false);
            MoveAwayFrom(player, enemy, combat.PreferredMinRange);
            return true;
        }

        if (now - _lastAttack >= combat.AttackIntervalMs)
        {
            SetWarMode(player, true);
            GameActions.Attack(_world, enemy.Serial);
            _lastAttack = now;
        }

        return true;
    }

    private Mobile? FindNearestEnemy(PlayerMobile player, SupportAgentSave settings)
    {
        Mobile? nearest = null;
        int nearestDistance = int.MaxValue;

        foreach (Mobile mobile in _world.Mobiles.Values)
        {
            if (!IsValidEnemy(mobile, player, settings))
                continue;

            int distance = mobile.Distance;

            if (distance < nearestDistance)
            {
                nearest = mobile;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    /// <summary>Gray or red, alive, in range, not human-bodied, and not the leader, the helper or a friend.</summary>
    private static bool IsValidEnemy(Mobile mobile, PlayerMobile player, SupportAgentSave settings)
    {
        if (mobile.IsDestroyed || mobile.IsDead || mobile == player || mobile.Serial == settings.LeaderSerial)
            return false;

        if (mobile.IsHuman || mobile.Distance > settings.Combat.AttackRange)
            return false;

        bool hostile = (settings.Combat.AttackGray && mobile.NotorietyFlag == NotorietyFlag.Gray)
                       || (settings.Combat.AttackRed && mobile.NotorietyFlag == NotorietyFlag.Murderer);

        return hostile && !FriendsListManager.Instance.IsFriend(mobile.Serial);
    }

    /// <summary>Steps straight away from <paramref name="enemy" /> to about <paramref name="range" /> tiles.</summary>
    private static void MoveAwayFrom(PlayerMobile player, Mobile enemy, int range)
    {
        int deltaX = player.X - enemy.X;
        int deltaY = player.Y - enemy.Y;

        if (deltaX == 0 && deltaY == 0)
            deltaX = 1;

        double length = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        int targetX = (int)Math.Round(player.X + deltaX / length * range);
        int targetY = (int)Math.Round(player.Y + deltaY / length * range);

        player.Pathfinder.WalkTo(targetX, targetY, player.Z, 1);
    }

    private static void FollowLeader(PlayerMobile player, Mobile leader, SupportAgentSave settings)
    {
        if (!settings.Follow.Enabled || player.Pathfinder.AutoWalking)
            return;

        if (leader.Distance > settings.Follow.FollowDistance)
            player.Pathfinder.WalkTo(leader.X, leader.Y, leader.Z, settings.Follow.FollowDistance);
    }

    private static void StopMoving(PlayerMobile player)
    {
        if (player.Pathfinder.AutoWalking)
            player.Pathfinder.StopAutoWalk();
    }

    /// <summary>Changes war mode only when needed, and only turns off war mode the agent itself turned on.</summary>
    private void SetWarMode(PlayerMobile player, bool enabled)
    {
        if (enabled)
        {
            if (!player.InWarMode)
            {
                GameActions.RequestWarMode(player, true);
                _warModeByAgent = true;
            }

            return;
        }

        if (_warModeByAgent && player.InWarMode)
            GameActions.RequestWarMode(player, false);

        _warModeByAgent = false;
    }

    private void Stop(PlayerMobile player)
    {
        StopMoving(player);
        SetWarMode(player, false);
        _runner.Reset();
        _currentEnemy = 0;
        _retreating = false;
        _followRequested = false;
        _healRequested = false;
        _busyUntil = 0;
        _wasEnabled = false;
    }

    private static int HealthPercent(Mobile mobile) =>
        mobile.HitsMax == 0 ? 100 : mobile.Hits * 100 / mobile.HitsMax;

    /// <summary>Picks up "follow me" / "heal me" said by the leader.</summary>
    private void OnMessageReceived(object? sender, MessageEventArgs e)
    {
        SupportAgentSave? settings = _settings;

        if (settings is not { Enabled: true } || !settings.Commands.Enabled || e.Parent == null || string.IsNullOrEmpty(e.Text))
            return;

        if (settings.LeaderSerial == 0 || e.Parent.Serial != settings.LeaderSerial)
            return;

        if (!string.IsNullOrWhiteSpace(settings.Commands.FollowCommand) && e.Text.ContainsIgnoreCase(settings.Commands.FollowCommand))
            _followRequested = true;
        else if (!string.IsNullOrWhiteSpace(settings.Commands.HealCommand) && e.Text.ContainsIgnoreCase(settings.Commands.HealCommand))
            _healRequested = true;
    }

    #endregion
}
