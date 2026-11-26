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
    private string _cachedCarvedRaw;
    private readonly List<ushort> _cachedCarvedGraphics = new();

    // How long after using the knife new carved items in the backpack are moved to the carved items bag.
    private const uint CARVE_WATCH_MS = 4000;
    private uint _watchCarvedUntil;
    private readonly HashSet<uint> _movedCarved = new();

    private AutoSkinningManager() { }

    public bool IsEnabled => ProfileManager.CurrentProfile?.EnableAutoSkinning ?? false;

    public void OnSceneLoad()
    {
        EventSink.OnOpenContainer += OnOpenContainer;
        EventSink.OnItemCreatedInternal += OnItemChanged;
        EventSink.OnItemUpdatedInternal += OnItemChanged;
    }

    public void OnSceneUnload()
    {
        EventSink.OnOpenContainer -= OnOpenContainer;
        EventSink.OnItemCreatedInternal -= OnItemChanged;
        EventSink.OnItemUpdatedInternal -= OnItemChanged;
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

                                                                              // Carved items that land in the backpack shortly after this go to the carved items bag.
                                                                              _watchCarvedUntil = Time.Ticks + CARVE_WATCH_MS;
                                                                              _movedCarved.Clear();
                                                                          }), ActionPriority.UseItem);

    /// <summary>
    /// Moves a carved item that just appeared in (or stacked onto a pile in) the backpack into the carved items bag.
    /// Only inside the short window after a carve, and only for the carved graphic list, so loot and anything else
    /// arriving in the backpack stays put.
    /// </summary>
    private void OnItemChanged(object sender, EventArgs e)
    {
        if (sender is not Item item || Time.Ticks > _watchCarvedUntil)
            return;

        uint bag = ProfileManager.CurrentProfile?.AutoSkinningBagSerial ?? 0;
        World world = World.Instance;
        Item backpack = world?.Player?.Backpack;
        if (bag == 0 || backpack == null || item.Container != backpack.Serial || world.Items.Get(bag) == null)
            return;

        if (!GetCarvedGraphics().Contains(item.Graphic) || !_movedCarved.Add(item.Serial))
            return;

        ObjectActionQueue.Instance.Enqueue(new MoveRequest(item.Serial, bag, item.Amount).ToObjectActionQueueItem(), ActionPriority.MoveItem);
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

    private List<ushort> GetCarvedGraphics() =>
        ParseGraphics(ProfileManager.CurrentProfile?.AutoSkinningCarvedGraphics, ref _cachedCarvedRaw, _cachedCarvedGraphics);

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
