using System;
using System.Collections.Generic;

namespace ClassicUO.Utility;

public class ConcurrentPriorityQueue<TElement, TPriority>
{
    public bool IsEmpty => _isEmpty;

    protected readonly PriorityQueue<TElement, (TPriority Priority, long Sequence)> _queue = new(new PrioritySequenceComparer<TPriority>());
    private readonly object _lock = new();
    private bool _isEmpty = true;
    private long _sequence;

    public void Enqueue(TElement element, TPriority priority, long? sequence = null)
    {
        if (element == null) return;

        lock (_lock)
        {
            // Enque using the lowest sequence between sequence and _sequence, or increment _sequence
            _queue.Enqueue(element, (priority, sequence < _sequence + 1 ? sequence.Value : _sequence++));
            _isEmpty = false;
        }
    }

    public bool TryDequeue(out TElement element, out TPriority priority, out long sequence)
    {
        lock (_lock)
            return TryDequeueCore(out element, out priority, out sequence);
    }

    /// <summary>
    /// Let the next priority group go first when the head group is yielding and work from
    /// the other group is available. The yielded items keep their original FIFO order.
    /// </summary>
    protected bool TryDequeueYielding(TPriority yieldPriority, Predicate<TPriority> mayTakeTurn,
        out TElement element, out TPriority priority, out long sequence)
    {
        lock (_lock)
        {
            var comparer = EqualityComparer<TPriority>.Default;
            if (!_queue.TryPeek(out _, out var head) || !comparer.Equals(head.Priority, yieldPriority))
                return TryDequeueCore(out element, out priority, out sequence);

            bool hasOtherWork = false;
            foreach (var (_, queuedPriority) in _queue.UnorderedItems)
            {
                if (mayTakeTurn(queuedPriority.Priority))
                {
                    hasOtherWork = true;
                    break;
                }
            }
            if (!hasOtherWork)
                return TryDequeueCore(out element, out priority, out sequence);

            var yielded = new List<(TElement Element, (TPriority Priority, long Sequence) Priority)>();
            while (_queue.TryPeek(out var next, out var nextPriority) && comparer.Equals(nextPriority.Priority, yieldPriority))
            {
                _queue.Dequeue();
                yielded.Add((next, nextPriority));
            }

            // The yielded group and its alternate are adjacent. If another group is ahead
            // of the alternate, retain ordinary ordering.
            bool tookAlternate = _queue.TryPeek(out _, out var alternate) && mayTakeTurn(alternate.Priority);
            if (tookAlternate)
                _queue.TryDequeue(out element, out alternate);
            else
            {
                (element, alternate) = yielded[0];
                yielded.RemoveAt(0);
            }

            foreach (var (waiting, originalPriority) in yielded)
                _queue.Enqueue(waiting, originalPriority);

            priority = alternate.Priority;
            sequence = alternate.Sequence;
            FinishDequeue();
            return true;
        }
    }

    private bool TryDequeueCore(out TElement element, out TPriority priority, out long sequence)
    {
        bool result = _queue.TryDequeue(out element, out var compositePriority);
        priority = compositePriority.Priority;
        sequence = compositePriority.Sequence;
        FinishDequeue();
        return result;
    }

    private void FinishDequeue()
    {
        _isEmpty = _queue.Count == 0;
        if (_isEmpty)
            _sequence = 0; //reset to avoid potential overrun if client is running for a long time
    }

    /// <summary>
    /// Clear all items of a specific priority
    /// </summary>
    /// <param name="priority"></param>
    public void ClearByPriority(TPriority priority) =>
        Retain((_, queuedPriority) => !EqualityComparer<TPriority>.Default.Equals(queuedPriority, priority));

    /// <summary>
    /// Remove all items matching <paramref name="predicate"/>, preserving the priority and
    /// insertion order of the remaining items.
    /// </summary>
    /// <param name="predicate">Return true to remove the item.</param>
    public void RemoveWhere(Predicate<TElement> predicate) => Retain((element, _) => !predicate(element));

    protected void RemoveWhereWithPriority(Func<TElement, TPriority, bool> predicate) =>
        Retain((element, priority) => !predicate(element, priority));

    private void Retain(Func<TElement, TPriority, bool> keep)
    {
        lock (_lock)
        {
            var itemsToKeep = new List<(TElement Element, TPriority Priority, long Sequence)>();

            while (_queue.TryDequeue(out TElement element, out (TPriority Priority, long Sequence) compositePriority))
                if (keep(element, compositePriority.Priority))
                    itemsToKeep.Add((element, compositePriority.Priority, compositePriority.Sequence));

            foreach ((TElement Element, TPriority Priority, long Sequence) item in itemsToKeep) _queue.Enqueue(item.Element, (item.Priority, item.Sequence));

            _isEmpty = _queue.Count == 0;

            if (_isEmpty)
                _sequence = 0;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _queue.Clear();
            _isEmpty = true;
        }
    }

    private class PrioritySequenceComparer<T> : IComparer<(T Priority, long Sequence)>
    {
        public int Compare((T Priority, long Sequence) x, (T Priority, long Sequence) y)
        {
            int priorityComparison = Comparer<T>.Default.Compare(x.Priority, y.Priority);
            if (priorityComparison != 0)
                return priorityComparison;

            return x.Sequence.CompareTo(y.Sequence);
        }
    }
}
