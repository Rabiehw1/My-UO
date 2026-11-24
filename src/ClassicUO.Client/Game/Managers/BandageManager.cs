using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Network;
using System;
using System.Collections.Generic;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game.Managers
{
    internal class BandageManager : IDisposable
    {
        public static BandageManager Instance
        {
            get
            {
                if (field == null)
                    field = new();
                return field;
            }
            private set;
        }

        private long _nextBandageTime = 0;
        private long _bandageInProgressUntil;
        private long _bandagingBuffSetTime = 0;
        private long _nextCheckTime = 0;
        private readonly HealingAssistantManager _healing = new();

        // When the last bandage was sent, so a "busy" reply from the shard can be matched to it. Zero when none is
        // awaiting a reply.
        private long _lastAttemptTime = 0;

        // Bandages refused in a row with a busy reply; reset when one is accepted or a pause begins.
        private int _consecutiveRefusals = 0;
        private readonly LinkedList<uint> _pendingHeals = new();
        private readonly Dictionary<uint, long> _retryDeadlines = new();

        // How often Update() checks the player's health and re-checks pending heals. The check only reads HP the
        // client already has from the server, so it sends nothing over the network.
        private const int CHECK_INTERVAL_MS = 100;

        // After the shard refuses a bandage (e.g. "I am already doing something else"), wait this long before the
        // next attempt. Keeps refused attempts from flooding the shard.
        private const int MIN_RETRY_GAP_MS = 1000;

        // After this many refusals in a row, pause for BUSY_PAUSE_MS instead of retrying every MIN_RETRY_GAP_MS,
        // so a long busy spell costs the shard a handful of attempts rather than one a second.
        private const int MAX_REFUSALS_BEFORE_PAUSE = 5;
        private const int BUSY_PAUSE_MS = 10_000;

        // A busy reply arriving later than this after a bandage was sent is assumed to be about something else.
        private const int BUSY_REPLY_WINDOW_MS = 2500;

        // How long the bandage's target cursor may take to arrive before the auto-target is dropped, so a stale
        // auto-target can never answer an unrelated cursor such as a spell.
        private const int AUTO_TARGET_TIMEOUT_MS = 2000;

        // Upper bound on how long a single mobile is kept in the retry queue while it
        // can't actually be healed (e.g. permanently out of range and HP not changing),
        // so we don't re-queue it forever. Reset whenever a heal is attempted.
        private const long MAX_RETRY_DURATION_MS = 30_000;

        // Safety net in case a buff-removed event is ever missed: treat the bandaging
        // buff as expired after this long so the agent can't get stuck never healing.
        private const long MAX_BANDAGE_BUFF_AGE_MS = 15_000;

        public int PendingHealCount => _pendingHeals.Count;

        private bool IsEnabled => ProfileManager.CurrentProfile is { EnableHealingAssistant: true, EnableBandageAgent: true };
        private bool FriendBandagingEnabled => ProfileManager.CurrentProfile?.BandageAgentBandageFriends ?? false;
        private bool AllyBandagingEnabled => ProfileManager.CurrentProfile?.BandageAgentBandageAllies ?? false;
        private bool PetBandagingEnabled => ProfileManager.CurrentProfile?.BandageAgentBandagePets ?? false;
        private int HealDelayMs => ProfileManager.CurrentProfile?.BandageAgentDelay ?? 3000;
        private bool CheckForBuff => ProfileManager.CurrentProfile?.BandageAgentCheckForBuff ?? false;
        private int MissingHpThreshold => 100 - Math.Clamp(ProfileManager.CurrentProfile?.BandageAgentHPPercentage ?? 80, 1, 99);
        private bool UseOnPoisoned => ProfileManager.CurrentProfile?.BandageAgentCheckPoisoned ?? false;
        private bool CheckHidden => ProfileManager.CurrentProfile?.BandageAgentCheckHidden ?? false;
        private bool CheckInvul => ProfileManager.CurrentProfile?.BandageAgentCheckInvul ?? false;
        private bool HasBandagingBuff { get; set; } = false;
        private bool UseDexFormula => ProfileManager.CurrentProfile?.BandageAgentUseDexFormula ?? false;
        private bool DisableSelfHeal => ProfileManager.CurrentProfile?.BandageAgentDisableSelfHeal ?? false;
        private bool UseJournalTrigger => ProfileManager.CurrentProfile?.BandageAgentUseJournalTrigger ?? false;
        private string JournalMessages => ProfileManager.CurrentProfile?.BandageAgentJournalMessages ?? "";
        private string BusyMessages => ProfileManager.CurrentProfile?.BandageAgentBusyMessages ?? "";
        private bool UseSelfCommand => ProfileManager.CurrentProfile?.BandageAgentUseSelfCommand ?? false;
        private string SelfCommand => ProfileManager.CurrentProfile?.BandageAgentSelfCommand ?? "";
        private bool SelfCommandExpectTarget => ProfileManager.CurrentProfile?.BandageAgentSelfCommandExpectTarget ?? false;
        private int BandageDistance => ProfileManager.AccountSettings?.BandageAgentDistance ?? 3;

        private BandageManager()
        {
            EventSink.OnBuffAddedInternal += OnBuffAdded;
            EventSink.OnBuffRemovedInternal += OnBuffRemoved;
            EventSink.JournalEntryAdded += OnJournalEntryAdded;
        }

        public void SetPoisoned(uint serial, bool status)
        {
            if (!IsEnabled || !status) return;

            Mobile mobile = World.Instance?.Mobiles?.Get(serial);

            if (mobile is PlayerMobile player)
            {
                TryHealPlayer(player);
                return;
            }

            if (ShouldAttemptHeal(mobile)) TryHeal(mobile);
        }

        private void OnBuffAdded(object sender, BuffEventArgs e)
        {
            if (e.Buff.Type == BuffIconType.Healing || e.Buff.Type == BuffIconType.Veterinary)
            {
                HasBandagingBuff = true;
                _bandagingBuffSetTime = Time.Ticks;
            }
        }

        /// <summary>
        /// Whether the bandaging buff is currently considered active. Includes a maximum
        /// age so a missed buff-removed event can't permanently disable healing.
        /// </summary>
        private bool IsBandagingBuffActive => HasBandagingBuff && (Time.Ticks - _bandagingBuffSetTime) < MAX_BANDAGE_BUFF_AGE_MS;

        private void OnJournalEntryAdded(object sender, JournalEntry e)
        {
            if (e == null || string.IsNullOrEmpty(e.Text)) return;

            if (IsBusyReplyToLastBandage(e.Text))
            {
                // The shard refused the bandage, so no bandage started from it: try again shortly instead of
                // waiting a full bandage cycle, but pause after several refusals in a row.
                _consecutiveRefusals++;

                if (_consecutiveRefusals >= MAX_REFUSALS_BEFORE_PAUSE)
                {
                    _consecutiveRefusals = 0;
                    _nextBandageTime = _lastAttemptTime + BUSY_PAUSE_MS;
                    GameActions.Print(TazLang.Get("bandageagent_busypause", [(BUSY_PAUSE_MS / 1000).ToString()]), Constants.HUE_WARN);
                }
                else
                {
                    _nextBandageTime = _lastAttemptTime + MIN_RETRY_GAP_MS;
                }

                _lastAttemptTime = 0;
                _bandageInProgressUntil = 0;
                return;
            }

            if (IsEnabled && UseJournalTrigger && ContainsAny(e.Text, JournalMessages))
            {
                _nextBandageTime = 0;
                HasBandagingBuff = false;
            }
        }

        private bool IsBusyReplyToLastBandage(string text) =>
            _lastAttemptTime != 0
            && Time.Ticks - _lastAttemptTime <= BUSY_REPLY_WINDOW_MS
            && ContainsAny(text, BusyMessages);

        /// <summary>Whether <paramref name="text" /> contains any of the ';'-separated <paramref name="phrases" />.</summary>
        private static bool ContainsAny(string text, string phrases)
        {
            if (string.IsNullOrWhiteSpace(phrases)) return false;

            foreach (string phrase in phrases.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private void OnBuffRemoved(object sender, BuffEventArgs e)
        {
            if (e.Buff.Type is not (BuffIconType.Healing or BuffIconType.Veterinary))
                return;

            HasBandagingBuff = false;
            _bandageInProgressUntil = 0;
            if (CheckForBuff && Time.Ticks >= _nextBandageTime)
                _nextBandageTime = Time.Ticks + AsyncNetClient.Socket.Statistics.Ping;
        }

        /// <summary>
        /// Called from packet handlers when mobile HP changes
        /// </summary>
        public void OnMobileHpChanged(Mobile mobile, int oldHp, int newHp)
        {
            if (mobile is PlayerMobile player)
                TryHealPlayer(player);
            else if (IsEnabled && ShouldAttemptHeal(mobile))
                TryHeal(mobile);
        }

        /// <summary>
        /// Queues a mobile to be re-checked for healing on a later Update().
        /// </summary>
        private void ScheduleRetry(uint mobileSerial)
        {
            if (!IsEnabled || mobileSerial == 0 || mobileSerial == World.Instance?.Player?.Serial) return;

            if (!_pendingHeals.Contains(mobileSerial))
                _pendingHeals.AddLast(mobileSerial);

            if (!_retryDeadlines.ContainsKey(mobileSerial))
                _retryDeadlines[mobileSerial] = Time.Ticks + MAX_RETRY_DURATION_MS;
        }

        /// <summary>
        /// Driven from GameScene.Update() on the main thread. Every <see cref="CHECK_INTERVAL_MS" /> it checks the
        /// player's own health, so damage is caught even when no HP packet triggered
        /// <see cref="OnMobileHpChanged" />, then re-checks one pending heal for others.
        /// </summary>
        public void Update()
        {
            bool bandagesEnabled = IsEnabled;
            if (!bandagesEnabled && (_pendingHeals.Count > 0 || _retryDeadlines.Count > 0))
                ClearAllPendingHeals();

            if (ProfileManager.CurrentProfile?.EnableHealingAssistant != true)
                return;

            if (Time.Ticks < _nextCheckTime)
                return;

            _nextCheckTime = Time.Ticks + CHECK_INTERVAL_MS;

            PlayerMobile player = World.Instance?.Player;

            if (player == null)
                return;

            if (TryHealPlayer(player))
                return;

            if (bandagesEnabled && _pendingHeals.Count > 0)
                ProcessPendingHeals();
        }

        private bool TryHealPlayer(PlayerMobile player)
        {
            Profile profile = ProfileManager.CurrentProfile;
            if (profile?.EnableHealingAssistant != true)
                return false;

            // Wait for an active bandage to finish before picking the method for the player's current health.
            if ((profile.HealingAssistantUseGreaterHeal || profile.HealingAssistantUseTamalaHeal || profile.HealingAssistantUseMagicFish)
                && (Time.Ticks < _bandageInProgressUntil
                    || HasUnexpiredBandageBuff(player, BuffIconType.Healing)
                    || HasUnexpiredBandageBuff(player, BuffIconType.Veterinary)))
                return true;

            return _healing.TryHeal(World.Instance, player, profile, profile.EnableBandageAgent && ShouldAttemptHeal(player),
                () => IsReadyToBandage() && SendBandage(player));
        }

        private bool HasUnexpiredBandageBuff(PlayerMobile player, BuffIconType type) =>
            player.BuffIcons.TryGetValue(type, out BuffIcon buff) && Time.Ticks < buff.Timer
            && (buff.Timer != uint.MaxValue || Time.Ticks - _bandagingBuffSetTime < MAX_BANDAGE_BUFF_AGE_MS);

        /// <summary>
        /// Processes a single pending heal. Always called on the main thread from Update().
        /// </summary>
        private void ProcessPendingHeals()
        {
            try
            {
                PlayerMobile player = World.Instance?.Player;
                if (player == null)
                    return; // Not in game yet (login/logout/world load); keep the queue and retry later.

                // Drop anything that has been un-healable for too long (e.g. no bandages,
                // or a stuck target) so the queue doesn't keep re-checking it forever.
                PruneExpiredRetries();

                if (_pendingHeals.Count == 0) return;

                uint serial = _pendingHeals.First.Value;

                _pendingHeals.RemoveFirst();

                Mobile mobile = World.Instance?.Mobiles?.Get(serial);
                if (ShouldAttemptHeal(mobile))
                {
                    TryHeal(mobile);
                }
                else if (IsHealCandidate(mobile) && !IsRetryExpired(serial))
                {
                    // Conditions temporarily not met (e.g., distance, hidden, invul) but
                    // mobile still needs healing - keep retrying so we don't lose track
                    ScheduleRetry(serial);
                }
                else
                {
                    // Recovered, no longer a valid target, or retry window elapsed - stop tracking.
                    _retryDeadlines.Remove(serial);
                }
            }
            catch (Exception e)
            {
                Log.Error($"BandageManager failed while processing the heal retry queue: {e}");
            }
        }

        /// <summary>
        /// Whether the retry window for a mobile has elapsed without a successful heal attempt.
        /// </summary>
        private bool IsRetryExpired(uint serial) => _retryDeadlines.TryGetValue(serial, out long deadline) && Time.Ticks >= deadline;

        /// <summary>
        /// Removes queued heals whose retry window has elapsed so we don't keep
        /// re-checking targets that can never be healed.
        /// </summary>
        private void PruneExpiredRetries()
        {
            if (_pendingHeals.Count == 0) return;

            long now = Time.Ticks;
            LinkedListNode<uint> node = _pendingHeals.First;
            while (node != null)
            {
                LinkedListNode<uint> next = node.Next;
                if (_retryDeadlines.TryGetValue(node.Value, out long deadline) && now >= deadline)
                {
                    _pendingHeals.Remove(node);
                    _retryDeadlines.Remove(node.Value);
                }
                node = next;
            }
        }

        /// <summary>
        /// Checks whether a mobile is still a valid candidate for healing, ignoring
        /// temp conditions like distance/hidden/invul. Used to decide whether to
        /// keep retrying when ShouldAttemptHeal returns false.
        /// </summary>
        private bool IsHealCandidate(Mobile mobile)
        {
            PlayerMobile player = World.Instance?.Player;

            if (player == null || mobile == null || mobile.IsDead)
                return false;

            bool isPlayer = mobile == player;
            bool isFriend = !isPlayer && FriendBandagingEnabled && FriendsListManager.Instance.IsFriend(mobile);
            bool isAlly = !isPlayer && AllyBandagingEnabled && mobile.NotorietyFlag == NotorietyFlag.Ally;
            bool isPet = !isPlayer && PetBandagingEnabled && mobile.IsRenamable;

            if (!isPlayer && !isFriend && !isAlly && !isPet)
                return false;

            if (isPlayer && DisableSelfHeal)
                return false;

            if (mobile.HitsMax <= 0)
                return false;

            return NeedsBandage(mobile);
        }

        private bool ShouldAttemptHeal(Mobile mobile)
        {
            PlayerMobile player = World.Instance.Player;
            if (player == null || mobile == null)
                return false;

            if (mobile.IsDead)
                return false;

            // Check if this is the player or a friend/ally
            bool isPlayer = mobile == player;
            bool isFriend = !isPlayer && FriendBandagingEnabled && FriendsListManager.Instance.IsFriend(mobile.Serial);
            bool isAlly = !isPlayer && AllyBandagingEnabled && mobile.NotorietyFlag == NotorietyFlag.Ally;
            bool isPet = !isPlayer && PetBandagingEnabled && mobile.IsRenamable;

            if (!isPlayer && !isFriend && !isAlly && !isPet)
                return false;

            // Check if self-healing is disabled
            if (isPlayer && DisableSelfHeal)
                return false;

            // No healing beyond the configured bandage distance
            if (mobile.Distance > BandageDistance)
                return false;

            // Guard against divide-by-zero and invul
            if (mobile.HitsMax <= 0)
                return false;

            // Check for invul if enabled
            if (CheckInvul && mobile.IsYellowHits)
                return false;

            // Check for hidden status if enabled
            if (CheckHidden && mobile.IsHidden)
                return false;

            return NeedsBandage(mobile);
        }

        private bool NeedsBandage(Mobile mobile) =>
            (UseOnPoisoned && mobile.IsPoisoned)
            || HealingAssistantManager.HasMissingHealth(mobile.Hits, mobile.HitsMax, MissingHpThreshold);

        /// <summary>
        /// Whether a bandage may be sent right now: the previous one has run its course, no bandaging buff is
        /// showing (buff mode), and the player isn't dragging an item, which a double-click would disturb.
        /// </summary>
        private bool IsReadyToBandage() =>
            Time.Ticks >= _nextBandageTime
            && Time.Ticks >= _healing.NextActionTime
            && !(CheckForBuff && IsBandagingBuffActive)
            && !Client.Game.UO.GameCursor.ItemHold.Enabled;

        /// <summary>
        /// Bandages <paramref name="mobile" /> now if ready, otherwise keeps it queued for the next check. Sends
        /// directly rather than through the shared action queue, so a heal never waits behind looting or moves.
        /// </summary>
        private void TryHeal(Mobile mobile)
        {
            if (mobile == null) return;

            if (IsReadyToBandage())
            {
                // A heal is being attempted, so refresh the retry deadline for this mobile.
                _retryDeadlines[mobile.Serial] = Time.Ticks + MAX_RETRY_DURATION_MS;
                SendBandage(mobile);
            }

            // Keep the mobile queued so we re-check until IsHealCandidate is false, even if no HP-change packet arrives.
            ScheduleRetry(mobile.Serial);
        }

        /// <summary>Uses a bandage (or the self command) on <paramref name="mobile" /> and starts the bandage timer.</summary>
        /// <returns>True if something was sent; false when no bandage was found.</returns>
        private bool SendBandage(Mobile mobile)
        {
            bool isPlayer = mobile == World.Instance.Player;

            // Some servers support commands (e.g. .bandage / .bandageself) that heal you directly.
            if (isPlayer && UseSelfCommand && !string.IsNullOrWhiteSpace(SelfCommand))
            {
                // If the command opens a target cursor, auto-target self before it arrives
                if (SelfCommandExpectTarget)
                    TargetManager.SetAutoTarget(mobile.Serial, TargetType.Beneficial, matchAnyTargetType: true, AUTO_TARGET_TIMEOUT_MS);

                GameActions.Say(SelfCommand);
            }
            else if (!GameActions.UseBandageOnTarget(World.Instance, mobile.Serial, AUTO_TARGET_TIMEOUT_MS))
            {
                return false;
            }

            // The previous attempt drew no busy reply (a refusal clears _lastAttemptTime), so it was accepted.
            if (_lastAttemptTime != 0)
                _consecutiveRefusals = 0;

            long now = Time.Ticks;
            _lastAttemptTime = now;
            long interval = GetBandageInterval(isPlayer);
            _nextBandageTime = now + interval;

            // Even in buff mode, prevent consumables from starting while the buff or cursor is in transit.
            long estimatedDuration = CheckForBuff && !UseDexFormula ? HealDelayMs : interval;
            bool expectsTarget = isPlayer && UseSelfCommand && !string.IsNullOrWhiteSpace(SelfCommand)
                ? SelfCommandExpectTarget
                : GameActions.UsesBandageTargetCursor(ProfileManager.CurrentProfile, Client.Game.UO.Version);
            _bandageInProgressUntil = now + Math.Max(0, estimatedDuration) + (expectsTarget ? AUTO_TARGET_TIMEOUT_MS : 0);

            // Bandaging is an item use too: give queued loot and moves their usual gap after it.
            GlobalActionCooldown.BeginCooldown();

            Log.Debug($"Bandage agent sent a healing request for target 0x{mobile.Serial:X8}.");
            return true;
        }

        /// <summary>Time from sending a bandage until the next may be sent, unless the shard refuses it.</summary>
        private long GetBandageInterval(bool self)
        {
            if (UseDexFormula)
                return GetDexHealingTime(self);

            return CheckForBuff ? AsyncNetClient.Socket.Statistics.Ping + 10 : HealDelayMs;
        }

        /// <summary>
        /// This includes your last ping to be on the safe side
        /// </summary>
        /// <returns></returns>
        private int GetDexHealingTime(bool self)
        {
            if (!IsEnabled) return 0;

            int diff = self ? World.Instance.Player.Dexterity / 20 : World.Instance.Player.Dexterity / 60;
            int init = self ? 11 : 4;

            return (int)(((init - diff) * 1000) + AsyncNetClient.Socket.Statistics.Ping + 10);
        }

        /// <summary>
        /// Clears all pending healing requests
        /// </summary>
        private void ClearAllPendingHeals()
        {
            _pendingHeals.Clear();
            _retryDeadlines.Clear();
            _consecutiveRefusals = 0;
        }

        public void Dispose()
        {
            ClearAllPendingHeals();
            EventSink.OnBuffAddedInternal -= OnBuffAdded;
            EventSink.OnBuffRemovedInternal -= OnBuffRemoved;
            EventSink.JournalEntryAdded -= OnJournalEntryAdded;
            Instance = null;
        }
    }
}
