using System;
using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers.Structs;
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

    // Bounded history of corpses we've already tried to skin so we don't spam the same corpse
    // when it re-fires open/creation events.
    private const int MaxSkinnedHistory = 200;
    private readonly HashSet<uint> _skinnedCorpses = new();
    private readonly Queue<uint> _skinnedOrder = new();

    private string _cachedRaw;
    private readonly List<ushort> _cachedGraphics = new();
    // How long after the latest carve new items in the backpack are treated as carved. Each carve restarts it.
    private const uint CARVE_WATCH_MS = 3000;
    private const ushort GOLD_GRAPHIC = 0x0EED;
    private uint _watchCarvedUntil;

    // Backpack piles and their amounts at the latest carve: a pile counts as carved only if it grew since.
    private readonly Dictionary<uint, ushort> _backpackAmounts = new();
    private readonly HashSet<uint> _queuedCarved = new();

    private AutoSkinningManager() { }

    public bool IsEnabled => ProfileManager.CurrentProfile?.EnableAutoSkinning ?? false;

    public void OnSceneLoad()
    {
        EventSink.OnOpenContainer += OnOpenContainer;
        EventSink.OnItemCreatedInternal += OnItemCreated;
        EventSink.OnItemUpdatedInternal += OnItemUpdated;
    }

    public void OnSceneUnload()
    {
        EventSink.OnOpenContainer -= OnOpenContainer;
        EventSink.OnItemCreatedInternal -= OnItemCreated;
        EventSink.OnItemUpdatedInternal -= OnItemUpdated;
        _skinnedCorpses.Clear();
        _skinnedOrder.Clear();
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
    /// Attempts to skin the given corpse if a configured knife is available. Safe to call multiple
    /// times for the same corpse - it will only be attempted once.
    /// </summary>
    public void TrySkin(Item corpse)
    {
        if (!IsEnabled || corpse is not { IsCorpse: true })
            return;

        if (corpse.IsHumanCorpse && !(ProfileManager.CurrentProfile?.AutoSkinningHumanCorpses ?? false))
            return;

        if (!MarkSkinned(corpse.Serial))
            return;

        Item knife = FindKnife();
        if (knife == null)
            return;

        EnqueueSkin(knife.Serial, corpse.Serial);
    }

    private Item FindKnife()
    {
        PlayerMobile player = World.Instance?.Player;
        if (player == null)
            return null;

        foreach (ushort graphic in GetKnifeGraphics())
        {
            Item knife = player.FindItemByGraphic(graphic);
            if (knife != null)
                return knife;
        }

        return null;
    }

    private void EnqueueSkin(uint knifeSerial, uint corpseSerial) => ObjectActionQueue.Instance.Enqueue(new ObjectActionQueueItem(() =>
                                                                          {
                                                                              World world = World.Instance;
                                                                              if (world == null)
                                                                                  return;

                                                                              // Queue the corpse serial to answer the server target request produced by using the knife.
                                                                              // Match any target cursor type since some servers send a non-neutral cursor for skinning.
                                                                              TargetManager.SetAutoTarget(corpseSerial, TargetType.Neutral, true);

                                                                              // ignoreQueue: this is already running inside the action queue, don't re-enqueue.
                                                                              GameActions.DoubleClick(world, knifeSerial, false, true);

                                                                              StartCarveWatch(world);
                                                                          }), ActionPriority.UseItem);

    /// <summary>
    /// Opens (or, for back-to-back carves, extends) the window in which carved items are moved, and records the
    /// backpack as it is now so later growth can be told apart from things that were already there.
    /// </summary>
    private void StartCarveWatch(World world)
    {
        if ((ProfileManager.CurrentProfile?.AutoSkinningBagSerial ?? 0) == 0 || world.Player?.Backpack is not { } backpack)
            return;

        // A fresh window starts clean; an open one keeps what it already queued.
        if (Time.Ticks > _watchCarvedUntil)
        {
            _backpackAmounts.Clear();
            _queuedCarved.Clear();
        }

        for (LinkedObject node = backpack.Items; node != null; node = node.Next)
        {
            if (node is Item item && !_queuedCarved.Contains(item.Serial))
                _backpackAmounts[item.Serial] = item.Amount;
        }

        _watchCarvedUntil = Time.Ticks + CARVE_WATCH_MS;
    }

    private void OnItemCreated(object sender, EventArgs e) => ConsiderCarved(sender as Item, created: true);

    private void OnItemUpdated(object sender, EventArgs e) => ConsiderCarved(sender as Item, created: false);

    /// <summary>
    /// Queues a move to the carved items bag for a backpack item that a carve produced: one the shard just created
    /// there, or a pile that grew. Items that moved in from elsewhere (loot), piles that shrank, and gold stay put.
    /// </summary>
    private void ConsiderCarved(Item item, bool created)
    {
        if (item == null || Time.Ticks > _watchCarvedUntil || item.Graphic == GOLD_GRAPHIC)
            return;

        uint bag = ProfileManager.CurrentProfile?.AutoSkinningBagSerial ?? 0;
        World world = World.Instance;
        if (bag == 0 || world?.Player?.Backpack is not { } backpack || item.Container != backpack.Serial || world.Items.Get(bag) == null)
            return;

        bool grew = _backpackAmounts.TryGetValue(item.Serial, out ushort before) && item.Amount > before;
        if (!(created && !_backpackAmounts.ContainsKey(item.Serial) || grew) || !_queuedCarved.Add(item.Serial))
            return;

        uint serial = item.Serial;
        ObjectActionQueue.Instance.Enqueue(new ObjectActionQueueItem(() =>
        {
            // Read the pile when the move runs, so hides added by a later carve go along too.
            if (World.Instance?.Items.Get(serial) is { } current && current.Container == backpack.Serial)
                new MoveRequest(serial, bag, current.Amount).Execute();
        })
        {
            Serial = serial,
            TriggersGlobalCooldown = () => ObjectActionQueue.Instance.HasPendingMove
        }, ActionPriority.MoveItem);
    }

    private bool MarkSkinned(uint serial)
    {
        if (serial == 0 || !_skinnedCorpses.Add(serial))
            return false;

        _skinnedOrder.Enqueue(serial);

        while (_skinnedOrder.Count > MaxSkinnedHistory && _skinnedOrder.TryDequeue(out uint oldest))
            _skinnedCorpses.Remove(oldest);

        return true;
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
