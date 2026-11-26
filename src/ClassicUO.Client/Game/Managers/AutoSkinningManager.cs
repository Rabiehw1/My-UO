using System;
using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers.Structs;
using ClassicUO.Game.Managers.ShardSpells;
using ClassicUO.Game.Managers.Support;
using ClassicUO.Utility;

namespace ClassicUO.Game.Managers;

/// <summary>
/// Auto skinning support. When enabled, opening a corpse automatically uses a knife/dagger
/// (from the configured graphic list) on that corpse through the object action queue -
/// i.e. it double clicks the knife and targets the corpse for you.
/// </summary>
public sealed class AutoSkinningManager
{
    public static AutoSkinningManager Instance
    {
        get
        {
            if (field == null)
                field = new();
            return field;
        }
        private set => field = value;
    }

    // Keep a short history of corpses we have already targeted, independent of pending attempts.
    private const int MaxTargetHistory = 200;
    private readonly HashSet<uint> _targetedCorpses = new();
    private readonly Queue<uint> _targetedOrder = new();
    private const int MaxPendingCorpses = 200;
    private const int PendingLifetimeMs = 20_000;
    private const int SkinTargetTimeoutMs = 3_000;
    private const int RetryDelayMs = 1_000;
    private const int CheckIntervalMs = 150;
    private const int MaxAttempts = 2;
    private readonly Dictionary<uint, PendingCarve> _pending = new();
    private PendingCarve _awaitingCursor;
    private long _cursorDeadline;
    private long _nextCheck;
    private bool _knifeQueued;

    private sealed class PendingCarve(uint serial, long expires)
    {
        internal uint Serial { get; } = serial;
        internal long Expires { get; } = expires;
        internal long RetryAfter { get; set; }
        internal int Attempts { get; set; }
    }

    private string _cachedRaw;
    private readonly List<ushort> _cachedGraphics = new();
    private const int CarveWatchMs = 3000;

    private long _watchCarvedUntil;
    private uint _carveBag;
    private readonly Dictionary<uint, CarvedMove> _carvedMoves = new();
    private readonly List<MaterialDrop> _recentMaterialDrops = new();

    private AutoSkinningManager() { }

    public bool IsEnabled => ProfileManager.CurrentProfile?.EnableAutoSkinning ?? false;

    /// <summary>Whether automated actions should leave the next target cursor to the knife.</summary>
    internal bool IsWaitingForCursor => _awaitingCursor != null && Time.Ticks < _cursorDeadline;

    public void OnSceneLoad()
    {
        EventSink.OnOpenContainer += OnOpenContainer;
    }

    public void OnSceneUnload()
    {
        EventSink.OnOpenContainer -= OnOpenContainer;
        _carvedMoves.Clear();
        _recentMaterialDrops.Clear();
        _watchCarvedUntil = 0;
        _targetedCorpses.Clear();
        _targetedOrder.Clear();
        _pending.Clear();
        _awaitingCursor = null;
        Instance = null;
    }

    private void OnOpenContainer(object sender, uint serial)
    {
        if (!IsEnabled)
            return;

        if (sender is Item corpse && corpse.IsCorpse)
            TrySkin(corpse);
    }

    /// <summary>
    /// Records an opened corpse for skinning when combat and item actions leave a free slot.
    /// </summary>
    public void TrySkin(Item corpse)
    {
        if (!IsEligible(corpse) || _pending.Count >= MaxPendingCorpses)
            return;

        _pending.TryAdd(corpse.Serial, new PendingCarve(corpse.Serial, (long)Time.Ticks + PendingLifetimeMs));
    }

    /// <summary>Whether opening this corpse can lead to a carve with the player's current knife.</summary>
    internal bool CanPrioritizeOpen(Item corpse) => IsEligible(corpse) && FindKnife() != null;

    private bool IsEligible(Item corpse) =>
        IsEnabled && corpse is { IsCorpse: true, IsDestroyed: false }
        && (ProfileManager.CurrentProfile.AutoSkinningHumanCorpses || !corpse.IsHumanCorpse)
        && !_targetedCorpses.Contains(corpse.Serial);

    private bool IsWithinReach(Item corpse) =>
        IsEligible(corpse) && corpse.Distance <= ProfileManager.CurrentProfile.AutoOpenCorpseRange;

    private Item FindKnife()
    {
        PlayerMobile player = World.Instance?.Player;
        if (player == null)
            return null;

        foreach (ushort graphic in GetKnifeGraphics())
        {
            Item knife = player.FindItemByGraphic(graphic);
            if (knife is { IsDestroyed: false })
                return knife;
        }

        return null;
    }

    /// <summary>Queues at most one ready carve. Waiting for a cursor or move stays outside the action queue.</summary>
    internal void Update(World world)
    {
        if (!IsEnabled)
        {
            _awaitingCursor = null;
            _pending.Clear();
            return;
        }

        long now = Time.Ticks;
        if (_awaitingCursor != null)
        {
            if (IsWaitingForCursor)
                return;
            ReleaseCursorForRetry(now);
        }

        if (_pending.Count == 0 || _knifeQueued || now < _nextCheck)
            return;

        _nextCheck = now + CheckIntervalMs;
        foreach (PendingCarve carve in _pending.Values)
        {
            Item corpse = world.Items.Get(carve.Serial);
            if (now >= carve.Expires || !IsEligible(corpse))
            {
                _pending.Remove(carve.Serial);
                continue;
            }

            if (now < carve.RetryAfter || !IsWithinReach(corpse))
                continue;

            if (CanStart(world) && FindKnife() != null)
                EnqueueCarve(world, carve);
            return;
        }
    }

    private static bool CanStart(World world)
    {
        var hold = Client.Game.UO.GameCursor.ItemHold;
        return world.Player is { IsDead: false, IsCasting: false } && !hold.Enabled && !hold.Dropped
            && !ObjectActionQueue.Instance.HasPendingMove && !world.TargetManager.IsTargeting
            && !TargetManager.NextAutoTarget.IsSet && !world.Party.HasPendingHealCursor
            && !BandageManager.Instance.IsBandaging
            && !ShardSpellCaster.Instance.IsCasting && !BandageManager.Instance.IsWaitingForHealingSpellCursor
            && !SelfHealManager.IsWaitingForCursor && !SupportAgentManager.Instance.IsUsingAbility
            && !SupportAgentManager.Instance.IsWaitingForPetCursor;
    }

    private void EnqueueCarve(World world, PendingCarve carve)
    {
        _knifeQueued = true;
        bool sent = false;
        ObjectActionQueue.Instance.Enqueue(new ObjectActionQueueItem(() =>
        {
            if (!_pending.TryGetValue(carve.Serial, out PendingCarve current) || current != carve
                || Time.Ticks >= carve.Expires || !CanStart(world) || !IsWithinReach(world.Items.Get(carve.Serial))
                || FindKnife() is not { } knife)
                return;

            carve.Attempts++;
            _awaitingCursor = carve;
            _cursorDeadline = (long)Time.Ticks + SkinTargetTimeoutMs;
            GameActions.DoubleClick(world, knife.Serial, ignoreQueue: true);
            sent = true;
        }, _ => _knifeQueued = false)
        {
            TriggersGlobalCooldown = () => sent
        }, ActionPriority.AutoSkinning);
    }

    /// <summary>Answers the knife cursor after existing party and explicit targets have had priority.</summary>
    internal bool TryHandleCursor(World world, CursorTarget cursor, TargetType type, bool claimed)
    {
        if (_awaitingCursor is not { } carve)
            return false;

        if (!IsWaitingForCursor || claimed || type >= TargetType.Cancel
            || cursor is not (CursorTarget.Object or CursorTarget.Position) || !world.TargetManager.IsTargeting)
        {
            ReleaseCursorForRetry(Time.Ticks);
            return false;
        }

        if (!IsWithinReach(world.Items.Get(carve.Serial)))
        {
            ReleaseCursorForRetry(Time.Ticks);
            world.TargetManager.CancelTarget();
            return true;
        }

        // Start recording materials before replying, so the shard's first inventory update is observed.
        StartCarveWatch(world);
        world.TargetManager.Target(carve.Serial);
        _awaitingCursor = null;
        _pending.Remove(carve.Serial);
        RememberTargetSent(carve.Serial);
        return true;
    }

    private void ReleaseCursorForRetry(long now)
    {
        PendingCarve carve = _awaitingCursor;
        _awaitingCursor = null;
        _nextCheck = now + RetryDelayMs;

        if (carve.Attempts >= MaxAttempts || now >= carve.Expires)
            _pending.Remove(carve.Serial);
        else
            carve.RetryAfter = _nextCheck;
    }

    /// <summary>
    /// Opens or extends the window in which new carving materials received in the backpack can be routed.
    /// </summary>
    private void StartCarveWatch(World world)
    {
        uint bag = ProfileManager.CurrentProfile?.AutoSkinningBagSerial ?? 0;
        if (world.Player?.Backpack is not { } backpack || bag == backpack.Serial || world.Items.Get(bag) is not { IsDestroyed: false })
            return;

        _carveBag = bag;
        _watchCarvedUntil = Environment.TickCount64 + CarveWatchMs;
    }

    internal void RememberBackpackDrop(World world, uint serial, uint destination, ItemHold hold)
    {
        if (!IsEnabled || ProfileManager.CurrentProfile?.AutoSkinningBagSerial == 0
            || world?.Player?.Backpack is not { } backpack
            || destination != backpack.Serial && world.Items.Get(destination)?.Container != backpack.Serial)
            return;

        Item source = world.Items.Get(serial);
        bool held = hold.Dropped && hold.Serial == serial;
        ushort graphic = source?.OriginalGraphic ?? (held ? hold.OriginalGraphic : (ushort)0);
        if (!IsCarvingMaterial(graphic))
            return;

        // Keep the drop after the shard's acknowledgment, which can arrive before its inventory update.
        long now = Environment.TickCount64;
        _recentMaterialDrops.RemoveAll(drop => drop.Expires <= now);
        ushort hue = source?.OriginalHue ?? source?.Hue ?? hold.Hue;
        _recentMaterialDrops.Add(new(serial, destination, graphic, hue, now + CarveWatchMs));
    }

    internal void ObserveBackpackUpdate(World world, Item item, ItemMoveSource? previous)
    {
        int change = item.Amount - (previous?.Amount ?? 0);
        long now = Environment.TickCount64;
        ushort hue = item.OriginalHue ?? item.Hue;
        bool knownMove = IsKnownDrop(item, hue, change, now)
            || previous is { } source && (source.Container != item.Container || source.Graphic != item.OriginalGraphic || source.Hue != hue);
        bool canAdd = IsEnabled
            && now < _watchCarvedUntil
            && _carveBag == ProfileManager.CurrentProfile?.AutoSkinningBagSerial
            && IsCarvingMaterial(item.OriginalGraphic)
            && !knownMove;

        if (_carvedMoves.TryGetValue(item.Serial, out CarvedMove queued))
        {
            if (queued.Matches(item) && queued.Bag == _carveBag)
                queued.AddChange(change, canAdd);
            return;
        }

        if (!canAdd || change <= 0)
            return;

        CarvedMove move = new(item, _carveBag, change);
        uint backpack = item.Container;
        _carvedMoves.Add(item.Serial, move);
        ObjectActionQueue.Instance.Enqueue(new ObjectActionQueueItem(
            () =>
            {
                if (IsEnabled
                    && world == World.Instance
                    && world.Player?.Backpack?.Serial == backpack
                    && ProfileManager.CurrentProfile?.AutoSkinningBagSerial == move.Bag
                    && world.Items.Get(move.Bag) is { IsDestroyed: false }
                    && world.Items.Get(item.Serial) is { IsDestroyed: false } current
                    && current.Container == backpack
                    && move.Matches(current)
                    && move.Amount > 0
                    && move.Amount <= current.Amount)
                    new MoveRequest(item.Serial, move.Bag, (ushort)move.Amount).Execute();
            },
            _ => _carvedMoves.Remove(item.Serial))
        {
            Serial = item.Serial,
            TriggersGlobalCooldown = () => ObjectActionQueue.Instance.HasPendingMove
        }, ActionPriority.MoveItem);
    }

    private bool IsKnownDrop(Item item, ushort hue, int change, long now)
    {
        bool possibleMerge = false;
        for (int i = _recentMaterialDrops.Count - 1; i >= 0; i--)
        {
            MaterialDrop drop = _recentMaterialDrops[i];
            if (drop.Expires <= now)
            {
                _recentMaterialDrops.RemoveAt(i);
                continue;
            }

            if (drop.Serial == item.Serial || change > 0 && drop.Destination == item.Serial)
            {
                _recentMaterialDrops.RemoveAt(i);
                return true;
            }

            // A backpack drop can merge into an existing pile with a different serial. Skip ambiguous growth.
            possibleMerge |= change > 0 && drop.Destination == item.Container && drop.Graphic == item.OriginalGraphic && drop.Hue == hue;
        }

        return possibleMerge;
    }

    // Standard RunUO carving resources. Resource quality variants share graphics and use different hues.
    internal static bool IsCarvingMaterial(ushort graphic) => graphic is
        0x1078 or 0x1079 or 0x1081 or 0x1082 // Hides and cut leather
        or 0x09F1 or 0x09B9 or 0x1609 // Raw ribs, raw bird and raw lamb leg
        or 0x1BD1 or 0x0DF8 or 0x101F or 0x26B4; // Feathers, wool, tainted wool and scales

    private readonly record struct MaterialDrop(uint Serial, uint Destination, ushort Graphic, ushort Hue, long Expires);

    private sealed class CarvedMove(Item item, uint bag, int amount)
    {
        private readonly ushort _graphic = item.OriginalGraphic;
        private readonly ushort _hue = item.OriginalHue ?? item.Hue;

        public uint Bag { get; } = bag;
        public int Amount { get; private set; } = amount;

        public bool Matches(Item current) => ReferenceEquals(item, current) && current.OriginalGraphic == _graphic
            && (current.OriginalHue ?? current.Hue) == _hue;

        public void AddChange(int change, bool canAdd) => Amount = UpdateCarvedAmount(Amount, change, canAdd);
    }

    // For a decrease, assume the removed units came from this carve first.
    internal static int UpdateCarvedAmount(int amount, int change, bool canAdd) =>
        Math.Max(0, amount + (canAdd || change < 0 ? change : 0));

    private void RememberTargetSent(uint serial)
    {
        if (!_targetedCorpses.Add(serial))
            return;

        _targetedOrder.Enqueue(serial);
        if (_targetedOrder.Count > MaxTargetHistory && _targetedOrder.TryDequeue(out uint oldest))
            _targetedCorpses.Remove(oldest);
    }

    private List<ushort> GetKnifeGraphics() =>
        ParseGraphics(ProfileManager.CurrentProfile?.AutoSkinningKnifeGraphics, ref _cachedRaw, _cachedGraphics);

    /// <summary>Parses a ';'-separated graphic list, reusing the last result while the text is unchanged.</summary>
    private static List<ushort> ParseGraphics(string raw, ref string cachedRaw, List<ushort> cached)
    {
        raw ??= string.Empty;
        if (raw == cachedRaw)
            return cached;

        cachedRaw = raw;
        cached.Clear();

        foreach (string part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (StringHelper.TryParseInt(part, out int graphic) && graphic is > 0 and <= ushort.MaxValue)
                cached.Add((ushort)graphic);
        }

        return cached;
    }
}
