using System;
using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers.Structs;
using ClassicUO.Utility;

namespace ClassicUO.Game.Managers;

public class ObjectActionQueue : ConcurrentPriorityQueue<ObjectActionQueueItem, ActionPriority>
{
    public static ObjectActionQueue Instance { get; } = new();

    public int GetCurrentQueuedCount => _queue.Count;

    private sealed class PendingMove(ItemMoveSource source, MoveRequest request, ushort graphic, ushort hue, Action retryWhenFree)
    {
        public readonly ItemMoveSource Source = source;
        public readonly MoveRequest Request = request;
        public readonly ushort Graphic = graphic;
        public readonly ushort Hue = hue;
        public readonly Action RetryWhenFree = retryWhenFree;
        public readonly long Started = Environment.TickCount64;
        public int Quantity => Math.Min(Source.Amount, Request.Amount);
        public bool SourceChanged;
        public bool DestinationChanged;
    }

    private PendingMove _pendingMove;
    private int _unmatchedDropConfirmations;
    private bool _waitingForFreeCursor;
    private bool _reportedTimeout;
    private bool _yieldToLoot;

    public bool HasPendingMove => _pendingMove != null || _waitingForFreeCursor;

    internal ObjectActionQueue() { }

    public new void Clear()
    {
        CancelActions(static _ => true);
        _yieldToLoot = false;
    }

    public new void ClearByPriority(ActionPriority priority) => CancelActions((_, queuedPriority) => queuedPriority == priority);

    internal void CancelAutomaticCorpseOpens() => CancelActions(static (_, priority) =>
        priority is ActionPriority.OpenCorpse or ActionPriority.OpenCorpseForSkinning);

    /// <summary>
    /// Cancel every queued action that moves <paramref name="serial"/>.
    /// </summary>
    /// <remarks>
    /// Called when the server rejects a move (packet 0x27) so the rest of the queue does not keep
    /// trying to move an item the server refused to hand over.
    /// </remarks>
    public void CancelMovesFor(uint serial)
    {
        if (serial == 0) return;

        CancelActions(item => item.Serial == serial);
    }

    internal void CancelActions(Predicate<ObjectActionQueueItem> matches) => CancelActions((item, _) => matches(item));

    private void CancelActions(Func<ObjectActionQueueItem, ActionPriority, bool> matches)
    {
        List<ObjectActionQueueItem> canceled = null;

        RemoveWhereWithPriority((item, priority) =>
        {
            if (!matches(item, priority)) return false;

            item.SetCanceled();
            if (item.AfterInvoked != null)
                (canceled ??= new List<ObjectActionQueueItem>()).Add(item);
            return true;
        });

        if (canceled == null) return;

        // Removed items never reach Update(), so run their completion callbacks now.
        foreach (ObjectActionQueueItem item in canceled)
            item.AfterInvoked?.Invoke(item);
    }

    /// <summary>
    /// The location of the most recent item move sent to the server that has not been confirmed yet.
    /// </summary>
    /// <remarks>
    /// Queued moves send the pickup/drop packets without populating <see cref="ItemHold"/> (unlike a
    /// manual drag), so this snapshot is what lets the deny handler put a rejected item back.
    /// </remarks>
    public bool TryGetPendingMove(out ItemMoveSource source)
    {
        if (_pendingMove != null)
        {
            source = _pendingMove.Source;
            return true;
        }

        source = default;
        return false;
    }

    /// <summary>
    /// Forget the pending move. When <paramref name="serial"/> is non-zero the pending move is only
    /// cleared if it belongs to that item.
    /// </summary>
    public void ClearPendingMove(uint serial = 0)
    {
        if (serial == 0)
        {
            _waitingForFreeCursor = false;
            _unmatchedDropConfirmations = 0;
        }

        if (serial == 0 || _pendingMove?.Source.Serial == serial)
            _pendingMove = null;
    }

    internal bool TryStartMove(MoveRequest moveRequest, Action retryWhenFree)
    {
        var hold = Client.Game.UO.GameCursor.ItemHold;
        if (HasPendingMove || hold.Enabled || hold.Dropped
            || World.Instance.Items.Get(moveRequest.Serial) is not { IsDestroyed: false } item)
            return false;

        BeginMove(ItemMoveSource.Capture(item), moveRequest, item.OriginalGraphic, item.OriginalHue ?? item.Hue, retryWhenFree);
        return true;
    }

    internal void BeginMove(ItemMoveSource source, MoveRequest request, ushort graphic, ushort hue, Action retryWhenFree)
    {
        _pendingMove = new PendingMove(source, request, graphic, hue, retryWhenFree);
        _reportedTimeout = false;
    }

    /// <summary>
    /// Uses the original serial or both sides of a stack transfer to identify the move.
    /// </summary>
    internal void ObserveContainerUpdate(Item item, int? previousAmount)
    {
        PendingMove move = _pendingMove;
        if (move == null || move.Request.IsEquip)
            return;

        uint serial = item.Serial;
        if (serial == move.Source.Serial && item.Container == move.Request.Destination
            && (item.Container != move.Source.Container
                || item.X == move.Request.X && item.Y == move.Request.Y))
        {
            ConfirmDropFromInventory();
            return;
        }

        if (serial == move.Source.Serial && item.Container == move.Source.Container
            && move.Quantity > 0 && move.Source.Amount - item.Amount >= move.Quantity)
            move.SourceChanged = true;

        if (previousAmount is int before && move.Quantity > 0 && item.Amount - before == move.Quantity
            && (item.Container == move.Request.Destination || serial == move.Request.Destination)
            && item.OriginalGraphic == move.Graphic && (item.OriginalHue ?? item.Hue) == move.Hue)
            move.DestinationChanged = true;

        if (move.SourceChanged && move.DestinationChanged)
            ConfirmDropFromInventory();
    }

    /// <summary>Records the disappearance of the original stack while tracking a move.</summary>
    internal void ObserveSourceRemoval(uint serial)
    {
        if (_pendingMove is not { } move || move.Request.IsEquip || move.Source.Serial != serial)
            return;

        move.SourceChanged = true;
        if (move.DestinationChanged)
            ConfirmDropFromInventory();
    }

    /// <summary>Limits snapshot comparison to a container involved in the active move.</summary>
    internal bool TracksContainer(uint container) =>
        _pendingMove is { } move && !move.Request.IsEquip
        && (move.Source.Container == container || move.Request.Destination == container);

    /// <summary>Records a source stack absent from an authoritative container refresh.</summary>
    internal void ObserveContainerSnapshot(World world, uint container)
    {
        if (_pendingMove is { } move && move.Source.Container == container
            && world.Items.Get(move.Source.Serial)?.Container != container)
            ObserveSourceRemoval(move.Source.Serial);
    }

    /// <summary>Confirms a queued drop at the requested ground coordinates.</summary>
    internal void ConfirmGroundDrop(Item item)
    {
        if (_pendingMove is { } move && !move.Request.IsEquip && move.Source.Serial == item.Serial
            && !SerialHelper.IsValid(move.Request.Destination)
            && item.X == move.Request.X && item.Y == move.Request.Y
            && (item.Container != move.Source.Container || item.X != move.Source.X || item.Y != move.Source.Y))
            ConfirmDropFromInventory();
    }

    private void ConfirmDropFromInventory()
    {
        _pendingMove = null;
        // 0x29 has no item serial. If the shard sends it after the inventory result,
        // keep it from releasing the next move.
        _unmatchedDropConfirmations++;
    }

    /// <summary>Consumes a generic drop acknowledgment in send order.</summary>
    internal bool ConfirmDropAccepted()
    {
        if (_unmatchedDropConfirmations != 0)
        {
            _unmatchedDropConfirmations--;
            return false;
        }

        if (_pendingMove is { } move && !move.Request.IsEquip)
            _pendingMove = null;

        ConfirmCursorFree();
        return true;
    }

    /// <summary>Lets paused pickups proceed when the shard reports the held item was released.</summary>
    internal void ConfirmCursorFree() => _waitingForFreeCursor = false;

    /// <summary>Resolves an overdue move when the shard explicitly ends dragging.</summary>
    internal void ConfirmDragEnded()
    {
        ConfirmCursorFree();
        if (!_reportedTimeout || _pendingMove is not { } move)
            return;

        _pendingMove = null;
        if (!move.Request.IsEquip)
            _unmatchedDropConfirmations++;

        // A source that is still present can be retried. If either side changed, wait for
        // normal inventory updates instead of trying to move the same stack twice.
        if (!move.SourceChanged && !move.DestinationChanged)
            move.RetryWhenFree?.Invoke();
    }

    /// <summary>Completes a rejection and keeps a busy pickup eligible once the cursor is free.</summary>
    internal void RejectPendingMove(uint serial, bool alreadyHolding)
    {
        if (_pendingMove is not { } move || move.Source.Serial != serial)
            return;

        _pendingMove = null;
        if (!alreadyHolding)
            return;

        _waitingForFreeCursor = true;
        move.RetryWhenFree?.Invoke();
    }

    internal void ConfirmEquip(uint serial, uint destination, Layer layer)
    {
        if (_pendingMove is { } move && move.Request.IsEquip && move.Source.Serial == serial
            && move.Request.Destination == destination && layer != Layer.Invalid)
            ClearPendingMove(serial);

        var hold = Client.Game.UO.GameCursor.ItemHold;
        if (hold.Dropped && hold.Serial == serial)
        {
            hold.Clear();
            ConfirmCursorFree();
        }
    }

    public void Update()
    {
        if (_pendingMove != null && !_reportedTimeout && Environment.TickCount64 - _pendingMove.Started >= 3000)
        {
            _reportedTimeout = true;
            GameActions.Print(TazLang.Get("itemmove_waiting_for_confirmation",
                "Item moves paused while waiting for the shard to confirm the last move."));
        }

        if (IsEmpty || GlobalActionCooldown.IsOnCooldown) return; //Quick bool return if empty to avoid checking the queue when unnecessary

        while (TryDequeueScheduled(out ObjectActionQueueItem item, out ActionPriority priority, out long sequence))
        {
            if (item.Canceled)
            {
                item.AfterInvoked?.Invoke(item);
                continue;
            }

            if (priority >= ActionPriority.UnequipItem && (HasPendingMove || Client.Game.UO.GameCursor.ItemHold.Enabled || Client.Game.UO.GameCursor.ItemHold.Dropped)
                || priority >= ActionPriority.MoveItem && AutoSkinningManager.Instance.IsWaitingForCursor)
            {
                Enqueue(item,  priority, sequence); //Return to queue to retry again when not holding an item
                return;
            }

            item.Action?.Invoke();
            item.AfterInvoked?.Invoke(item);

            // Evaluated after the action runs, so items can report whether they actually did
            // work (e.g. a bandage heal that self-throttles only counts on rounds where a heal
            // was really sent). Items that did no work don't reset the shared cooldown and
            // don't stall the queue - we drain to the next item this same tick.
            if (!(item.TriggersGlobalCooldown?.Invoke() ?? true))
                continue;

            RecordExecutedPriority(priority);
            GlobalActionCooldown.BeginCooldown();
            break;
        }
    }

    internal bool TryDequeueScheduled(out ObjectActionQueueItem item, out ActionPriority priority, out long sequence) =>
        _yieldToLoot
            ? TryDequeueYielding(ActionPriority.OpenCorpseForSkinning, IsLoot, out item, out priority, out sequence)
            : TryDequeue(out item, out priority, out sequence);

    internal void RecordExecutedPriority(ActionPriority priority)
    {
        // A real promoted open lets the next pending loot move run before more opens.
        // Canceled actions and no-ops do not take anyone's turn.
        if (priority == ActionPriority.OpenCorpseForSkinning)
            _yieldToLoot = true;
        else if (IsLoot(priority))
            _yieldToLoot = false;
    }

    private static bool IsLoot(ActionPriority priority) =>
        priority is >= ActionPriority.LootItemHigh and <= ActionPriority.LootItem;
}

/// <summary>
///
/// </summary>
/// <param name="action">The action to perform</param>
/// <param name="afterInvoked">Called after the action was performed, will be called weather it was canceled or not.</param>
public class ObjectActionQueueItem(Action action, Action<ObjectActionQueueItem> afterInvoked = null)
{
    public Action Action { get; } = action;
    public Action<ObjectActionQueueItem> AfterInvoked { get; } = afterInvoked;
    public bool Canceled { get; private set; }

    /// <summary>
    /// Item serial this action moves, or zero when the action does not target a specific item.
    /// Used to cancel queued moves for an item the server refused.
    /// </summary>
    public uint Serial { get; init; }

    /// <summary>
    /// Evaluated by the queue after <see cref="Action"/> runs to decide whether this item
    /// starts the shared <see cref="GlobalActionCooldown"/>. Returning false runs the item
    /// but skips the cooldown, so self-throttled actions (e.g. a bandage heal that didn't
    /// actually fire this round) don't block the player's other queued item actions.
    /// Defaults to always true.
    /// </summary>
    public Func<bool> TriggersGlobalCooldown { get; init; } = static () => true;

    public void SetCanceled(bool canceled = true) => Canceled = canceled;

    public static ObjectActionQueueItem QuickLoot(uint serial) => World.Instance.Items.TryGetValue(serial, out Item item) ? QuickLoot(item) : null;

    public static ObjectActionQueueItem QuickLoot(Item item)
    {
        if (item == null) return null;
        MoveRequest? moveRequest = item.ToLootBag();

        if(moveRequest.HasValue)
            return moveRequest.Value.ToObjectActionQueueItem();

        return null;
    }

    public static ObjectActionQueueItem EquipItem(uint serial, Layer layer)
    {
        MoveRequest? moveRequest = MoveRequest.EquipItem(serial, layer);

        if(moveRequest.HasValue)
            return moveRequest.Value.ToObjectActionQueueItem();

        return null;
    }

    public static ObjectActionQueueItem DoubleClick(uint serial, bool ignoreWarMode = false)
    {
        if(serial == 0) return null;

        return new ObjectActionQueueItem(() => GameActions.DoubleClick(World.Instance, serial, ignoreWarMode, true));
    }
}

/// <summary>
/// Warning: Values may be rearranged, don't use int values for saving as they may loose their values
/// </summary>
public enum ActionPriority
{
    Immediate,
    ManualUseItem, //Higher priority than regular useitem which may occur in scripts
    UseItem,
    UnequipItem, //Unequip item to make room for equipping - must run before EquipItem
    EquipItem,
    ManualMoveItem, //A move the player initiated - must run before queued/automated moves
    AutoSkinning,  //A knife use that is ready to reserve the target cursor.
    MoveItem,
    OpenCorpseForSkinning, //Shares turns with automatic loot moves.
    LootItemHigh,   //Auto-loot: High priority items (still lower than manual moves)
    LootItemMedium, //Auto-loot: Normal priority items
    LootItem,       //Auto-loot: Low priority items - lowest overall priority
    OpenCorpse,
}
