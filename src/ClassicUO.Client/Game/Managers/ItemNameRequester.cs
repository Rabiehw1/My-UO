#nullable enable

using System;
using System.Collections.Generic;
using ClassicUO.Game.GameObjects;
using ClassicUO.Network;

namespace ClassicUO.Game.Managers;

/// <summary>
/// Learns items' real names by single-clicking them, for shards that send no item tooltips and only reveal a magic
/// item's name when it is clicked. Clicks are spaced out so a full corpse costs a few small packets over a second
/// or two, never a burst, and no item is clicked twice. Owned by <see cref="AutoLootManager" />; game thread only.
/// </summary>
internal sealed class ItemNameRequester
{
    #region Private members

    /// <summary>Gap between clicks, keeping requests well below anything a shard would treat as flooding.</summary>
    private const int CLICK_SPACING_MS = 150;

    /// <summary>How long to wait for the shard's name reply before giving up on an item.</summary>
    private const int NAME_TIMEOUT_MS = 3000;

    /// <summary>Remembered clicked serials before the history is reset, bounding memory over a long session.</summary>
    private const int MAX_REMEMBERED = 10_000;

    private readonly World _world;
    private readonly Action<Item> _onNameLearned;
    private readonly Queue<uint> _queue = new();
    private readonly HashSet<uint> _requested = new();
    private readonly Dictionary<uint, long> _awaiting = new();
    private readonly List<uint> _finished = new();
    private readonly List<Item> _learned = new();
    private long _nextClickTime;

    #endregion

    #region Ctor

    /// <param name="world">World the items live in.</param>
    /// <param name="onNameLearned">Called on the game thread when a requested item's name arrives.</param>
    public ItemNameRequester(World world, Action<Item> onNameLearned)
    {
        _world = world;
        _onNameLearned = onNameLearned;
    }

    #endregion

    #region Public methods

    /// <summary>Whether the client already has the shard's name for <paramref name="item" />.</summary>
    /// <param name="item">Item to test.</param>
    /// <returns>True when a tooltip name or a single-click name has been received.</returns>
    public static bool HasShardName(Item item) => !string.IsNullOrEmpty(item.OPLName) || !string.IsNullOrEmpty(item.Name);

    /// <summary>Queues a click for <paramref name="item" /> unless its name is known or it was clicked before.</summary>
    /// <param name="item">Item whose name is needed.</param>
    public void Request(Item item)
    {
        if (HasShardName(item))
            return;

        if (_requested.Count >= MAX_REMEMBERED)
            _requested.Clear();

        if (_requested.Add(item.Serial))
            _queue.Enqueue(item.Serial);
    }

    /// <summary>Sends the next queued click when due and reports names that have arrived. Call every frame.</summary>
    public void Update()
    {
        long now = Time.Ticks;
        CheckReplies(now);

        if (_queue.Count == 0 || now < _nextClickTime)
            return;

        while (_queue.Count > 0)
        {
            uint serial = _queue.Dequeue();
            Item? item = _world.Items.Get(serial);

            // Looted, decayed or named by some other means while waiting.
            if (item == null || item.IsDestroyed || HasShardName(item))
                continue;

            AsyncNetClient.Socket.Send_ClickRequest(serial);
            _awaiting[serial] = now + NAME_TIMEOUT_MS;
            _nextClickTime = now + CLICK_SPACING_MS;
            return;
        }
    }

    /// <summary>Drops all queued and outstanding requests, e.g. when auto-loot is turned off.</summary>
    public void Clear()
    {
        _queue.Clear();
        _awaiting.Clear();
    }

    #endregion

    #region Private methods

    private void CheckReplies(long now)
    {
        if (_awaiting.Count == 0)
            return;

        foreach (KeyValuePair<uint, long> request in _awaiting)
        {
            Item? item = _world.Items.Get(request.Key);

            if (item == null || item.IsDestroyed || now > request.Value)
            {
                _finished.Add(request.Key);
            }
            else if (HasShardName(item))
            {
                _finished.Add(request.Key);
                _learned.Add(item);
            }
        }

        foreach (uint serial in _finished)
            _awaiting.Remove(serial);

        _finished.Clear();

        // Callbacks run after the scan, since they may queue further requests.
        foreach (Item item in _learned)
            _onNameLearned(item);

        _learned.Clear();
    }

    #endregion
}
