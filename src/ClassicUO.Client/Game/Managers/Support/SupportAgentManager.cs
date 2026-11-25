#nullable enable

using System;
using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers.ShardSpells;
using ClassicUO.Utility;

namespace ClassicUO.Game.Managers.Support;

/// <summary>
/// Plays the logged-in character as a helper: keeps the leader and friends alive with its class's abilities, saves
/// itself when badly hurt, follows the leader, answers spoken commands and fights nearby enemies.
/// </summary>
/// <remarks>
/// Each tick takes the first matching step, in priority order:
/// emergency → recovery → spoken commands → cure → heal → buff → catch up with leader → fight → follow.
/// While an ability is being cast the helper stands still and only an emergency can interrupt. During the
/// ability's recovery no other ability starts, but the helper keeps following, fighting and listening.
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
    public SupportAgentSave Settings => _settings ??= LoadSettings();

    #endregion

    #region Private members

    /// <summary>Decision cadence. Decisions only read state the client already holds, so this costs no traffic.</summary>
    private const int TICK_MS = 100;

    /// <summary>When the leader moves this far from where the helper is walking to, the walk is redirected.</summary>
    private const int FOLLOW_REPATH_TILES = 2;

    /// <summary>Bandages only reach patients this close.</summary>
    private const int BANDAGE_RANGE = 2;

    private readonly World _world;
    private readonly SupportAbilityRunner _runner;
    private readonly SupportStatus _status;
    private readonly List<Mobile> _group = new();

    private SupportAgentSave? _settings;
    private long _nextTick;
    // Stand still and start nothing (except an emergency) until _castUntil; start no ability until _abilityReadyAt.
    private long _castUntil;
    private long _abilityReadyAt;

    // Where the current follow walk is heading, to notice when the leader has moved on.
    private int _followTargetX;
    private int _followTargetY;
    private long _lastAttack;
    private long _followOverrideUntil;
    private uint _currentEnemy;
    private bool _retreating;
    private bool _wasEnabled;
    private bool _warModeByAgent;

    // Set by spoken commands from the leader, consumed on the next tick.
    private bool _followRequested;
    private bool _healRequested;
    private bool _blessRequested;

    #endregion

    #region Ctor

    private SupportAgentManager()
    {
        _world = World.Instance;
        _status = new SupportStatus(_world, () => Settings.ShowStatusOverhead);
        _runner = new SupportAbilityRunner(_world, name => _status.Problem("nogump:" + name, TazLang.Get("supportagent_status_nogump", [name])));
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

        // Mid-cast: hold still so the song or spell isn't interrupted. Commands wait for the next tick.
        if (now < _castUntil)
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
        {
            _status.Problem("leader", TazLang.Get("supportagent_status_noleader"));
            return;
        }

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
            if (TryUseRole(classSettings, SupportRole.Emergency, player, leader, null, now))
                return true;

            _status.Show(TazLang.Get("supportagent_status_emergency"), SupportStatus.HUE_DANGER);
        }

        if (!_retreating)
            return false;

        if (health >= emergency.RecoveryHealth)
        {
            _retreating = false;
            _status.Show(TazLang.Get("supportagent_status_recovered"), SupportStatus.HUE_GOOD);
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
            _status.Show(TazLang.Get("supportagent_status_following"), SupportStatus.HUE_INFO);
        }

        if (_blessRequested && now >= _abilityReadyAt)
        {
            _blessRequested = false;

            long remaining = BlessCooldownRemaining(classSettings, now);

            if (remaining > 0)
                _status.Show(TazLang.Get("supportagent_status_blesscooldown", [FormatDuration(remaining)]), SupportStatus.HUE_INFO);
            else if (TryUseRole(classSettings, SupportRole.Bless, player, leader, leader ?? player, now))
                return true;
        }

        if (!_healRequested)
            return false;

        // Still recovering from the last ability: keep the request and heal as soon as possible.
        if (now < _abilityReadyAt)
            return false;

        _healRequested = false;
        _currentEnemy = 0;
        _lastAttack = 0;
        SetWarMode(player, false);
        _status.Show(TazLang.Get("supportagent_status_healingyou"), SupportStatus.HUE_INFO);

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
        patient ??= FindMostHurt(settings.HealThreshold, settings.LeaderSerial);

        if (patient == null)
            return false;

        return TryUseRole(classSettings, SupportRole.Heal, player, leader, patient, now);
    }

    private bool TryBuff(PlayerMobile player, SupportClassSettings classSettings, Mobile? leader, long now) =>
        TryUseRole(classSettings, SupportRole.Buff, player, leader, null, now);

    /// <summary>Shortest cooldown left among enabled bless abilities; 0 when one is ready.</summary>
    private long BlessCooldownRemaining(SupportClassSettings classSettings, long now)
    {
        long shortest = long.MaxValue;

        foreach (SupportAbility ability in classSettings.Abilities)
        {
            if (ability.Enabled && ability.Role == SupportRole.Bless)
                shortest = Math.Min(shortest, _runner.RemainingCooldownMs(ability, now));
        }

        return shortest == long.MaxValue ? 0 : shortest;
    }

    private static string FormatDuration(long ms)
    {
        var time = TimeSpan.FromMilliseconds(ms);
        return time.TotalMinutes >= 1 ? $"{(int)time.TotalMinutes}m {time.Seconds}s" : $"{Math.Max(1, time.Seconds)}s";
    }

    /// <summary>
    /// Uses the first enabled, ready ability of <paramref name="role" /> that has a valid target. Waits for the
    /// previous ability's recovery, except in an emergency.
    /// </summary>
    private bool TryUseRole(
        SupportClassSettings classSettings,
        SupportRole role,
        PlayerMobile player,
        Mobile? leader,
        Mobile? patient,
        long now
    )
    {
        if (role != SupportRole.Emergency && now < _abilityReadyAt)
            return false;

        foreach (SupportAbility ability in classSettings.Abilities)
        {
            if (!ability.Enabled || ability.Role != role)
                continue;

            if (!_runner.IsOffCooldown(ability, now))
                continue;

            // Still active according to the shard's buff icon: casting again would waste the turn.
            if (ability.ActiveBuff is { } buff && player.IsBuffIconExists(buff))
                continue;

            if (!_runner.HasRequirements(ability))
            {
                ReportMissing(ability);
                continue;
            }

            if (!TryResolveTarget(ability, player, leader, patient, out uint target))
                continue;

            // Casting while walking gets interrupted, so stand still first.
            if (ability.CastTimeMs > 0)
                StopMoving(player);

            if (_runner.TryUse(ability, target, now))
            {
                _castUntil = now + ability.CastTimeMs;
                _abilityReadyAt = _castUntil + ability.RecoveryMs;
                ShowAbilityStatus(ability, patient);
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

    /// <summary>
    /// The most hurt member below <paramref name="threshold" />. The helper and the leader come first; friends
    /// are only considered when both of them are above the threshold.
    /// </summary>
    private Mobile? FindMostHurt(int threshold, uint leaderSerial)
    {
        Mobile? priority = FindMostHurt(threshold, member => member == _world.Player || member.Serial == leaderSerial);
        return priority ?? FindMostHurt(threshold, _ => true);
    }

    private Mobile? FindMostHurt(int threshold, Func<Mobile, bool> include)
    {
        Mobile? mostHurt = null;
        int lowest = threshold;

        foreach (Mobile member in _group)
        {
            if (!include(member))
                continue;

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

        uint previousEnemy = _currentEnemy;
        _currentEnemy = enemy?.Serial ?? 0;

        if (enemy == null)
            return false;

        if (enemy.Serial != previousEnemy)
            _status.Show(TazLang.Get("supportagent_status_attacking", [MobileName(enemy)]), SupportStatus.HUE_COMBAT);

        if (combat.KeepDistance && enemy.Distance <= combat.MeleeDangerRange)
        {
            _status.Show(TazLang.Get("supportagent_status_backingoff"), SupportStatus.HUE_COMBAT);
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

    /// <summary>
    /// Walks toward the leader. A walk already under way is redirected once the leader has moved on from where it
    /// was heading, instead of finishing at the leader's old spot first.
    /// </summary>
    private void FollowLeader(PlayerMobile player, Mobile leader, SupportAgentSave settings)
    {
        if (!settings.Follow.Enabled)
            return;

        if (player.Pathfinder.AutoWalking)
        {
            int moved = Math.Max(Math.Abs(leader.X - _followTargetX), Math.Abs(leader.Y - _followTargetY));

            if (moved <= FOLLOW_REPATH_TILES)
                return;

            player.Pathfinder.StopAutoWalk();
        }

        if (leader.Distance > settings.Follow.FollowDistance
            && player.Pathfinder.WalkTo(leader.X, leader.Y, leader.Z, settings.Follow.FollowDistance))
        {
            _followTargetX = leader.X;
            _followTargetY = leader.Y;
        }
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
        _blessRequested = false;
        _castUntil = 0;
        _abilityReadyAt = 0;
        _wasEnabled = false;
    }

    /// <summary>Says above the helper's head what an ability it just started is for.</summary>
    private void ShowAbilityStatus(SupportAbility ability, Mobile? patient)
    {
        PlayerMobile? player = _world.Player;
        Mobile? subject = patient ?? player;
        string name = subject == null ? "" : MobileName(subject);
        int health = subject == null ? 100 : HealthPercent(subject);

        switch (ability.Role)
        {
            case SupportRole.Heal when ability.Target == SupportTargetMode.None:
                _status.Show(TazLang.Get("supportagent_status_areaheal", [ability.Name, name, health.ToString()]), SupportStatus.HUE_GOOD);
                break;
            case SupportRole.Heal:
                _status.Show(TazLang.Get("supportagent_status_healing", [name, health.ToString()]), SupportStatus.HUE_GOOD);
                break;
            case SupportRole.Cure:
                _status.Show(TazLang.Get("supportagent_status_curing", [name]), SupportStatus.HUE_GOOD);
                break;
            case SupportRole.Buff:
                _status.Show(ability.Name, SupportStatus.HUE_BUFF);
                break;
            case SupportRole.Emergency:
                _status.Show(TazLang.Get("supportagent_status_emergencyability", [ability.Name]), SupportStatus.HUE_DANGER);
                break;
            case SupportRole.Bless:
                _status.Show(TazLang.Get("supportagent_status_blessing", [name]), SupportStatus.HUE_BUFF);
                break;
        }
    }

    /// <summary>Explains why a needed ability can't be used: no item picked, or out of bandages.</summary>
    private void ReportMissing(SupportAbility ability)
    {
        switch (ability.Action)
        {
            case SupportActionType.UseItem:
            case SupportActionType.UseItemAndGumpButton:
                _status.Problem("item:" + ability.Id, TazLang.Get("supportagent_status_noitem", [ability.Name]));
                break;
            case SupportActionType.Bandage:
                _status.Problem("bandages", TazLang.Get("supportagent_status_nobandages"));
                break;
            case SupportActionType.CastShardSpell when ShardSpellCaster.Instance.GetBookSerial(ability.ShardBook) == 0:
                _status.Problem("book:" + ability.ShardBook, TazLang.Get("supportagent_status_nobook", [ShardSpellCatalog.Get(ability.ShardBook).Name]));
                break;
        }
    }

    private static SupportAgentSave LoadSettings()
    {
        SupportAgentSave settings = SupportAgentSave.Load();
        settings.AddNewPresetAbilities();
        return settings;
    }

    private static string MobileName(Mobile mobile) => string.IsNullOrEmpty(mobile.Name) ? "?" : mobile.Name;

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
        else if (!string.IsNullOrWhiteSpace(settings.Commands.BlessCommand) && e.Text.ContainsIgnoreCase(settings.Commands.BlessCommand))
            _blessRequested = true;
        else if (!string.IsNullOrWhiteSpace(settings.Commands.HealCommand) && e.Text.ContainsIgnoreCase(settings.Commands.HealCommand))
            _healRequested = true;
    }

    #endregion
}
