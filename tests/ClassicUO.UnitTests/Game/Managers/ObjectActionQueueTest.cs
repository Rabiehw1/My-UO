using ClassicUO.Game.Managers;
using FluentAssertions;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    public class ObjectActionQueueTest
    {
        [Fact]
        public void PromotedCorpseOpens_ShareTurnsWithLootAndPreserveUrgentActions()
        {
            var queue = new ObjectActionQueue();
            var own = new ObjectActionQueueItem(() => { });
            var manual = new ObjectActionQueueItem(() => { });
            var routine = new ObjectActionQueueItem(() => { });
            var firstOpen = new ObjectActionQueueItem(() => { });
            var nextOpen = new ObjectActionQueueItem(() => { });
            var queuedLoot = new ObjectActionQueueItem(() => { });
            var laterLoot = new ObjectActionQueueItem(() => { });
            queue.Enqueue(own, ActionPriority.Immediate);
            queue.Enqueue(manual, ActionPriority.ManualMoveItem);
            queue.Enqueue(routine, ActionPriority.OpenCorpse);
            queue.Enqueue(firstOpen, ActionPriority.OpenCorpseForSkinning);
            queue.Enqueue(nextOpen, ActionPriority.OpenCorpseForSkinning);
            queue.Enqueue(queuedLoot, ActionPriority.LootItemHigh);
            queue.Enqueue(laterLoot, ActionPriority.LootItemHigh);

            TakeTurn().Should().BeSameAs(own);
            TakeTurn().Should().BeSameAs(manual);
            TakeTurn().Should().BeSameAs(firstOpen);
            TakeTurn().Should().BeSameAs(queuedLoot);
            TakeTurn().Should().BeSameAs(nextOpen);
            TakeTurn().Should().BeSameAs(laterLoot);
            TakeTurn().Should().BeSameAs(routine);

            ObjectActionQueueItem TakeTurn()
            {
                queue.TryDequeueScheduled(out var item, out var priority, out _).Should().BeTrue();
                queue.RecordExecutedPriority(priority);
                return item;
            }
        }

        [Fact]
        public void YieldingToLoot_DoesNotStrandCorpseOpensWhenLootIsEmpty()
        {
            var queue = new ObjectActionQueue();
            var open = new ObjectActionQueueItem(() => { });
            queue.RecordExecutedPriority(ActionPriority.OpenCorpseForSkinning);
            queue.Enqueue(open, ActionPriority.OpenCorpseForSkinning);

            queue.TryDequeueScheduled(out var item, out _, out _).Should().BeTrue();
            item.Should().BeSameAs(open);
            queue.IsEmpty.Should().BeTrue();
        }

        [Fact]
        public void CancelAutomaticCorpseOpens_ReleasesBothAutomaticPrioritiesAndLeavesManualOpen()
        {
            var queue = new ObjectActionQueue();
            int automaticCompletions = 0;
            int manualCompletions = 0;
            queue.Enqueue(new ObjectActionQueueItem(() => { }, _ => automaticCompletions++), ActionPriority.OpenCorpse);
            queue.Enqueue(new ObjectActionQueueItem(() => { }, _ => automaticCompletions++), ActionPriority.OpenCorpseForSkinning);
            queue.Enqueue(new ObjectActionQueueItem(() => { }, _ => manualCompletions++), ActionPriority.ManualUseItem);

            queue.CancelAutomaticCorpseOpens();

            automaticCompletions.Should().Be(2);
            manualCompletions.Should().Be(0);
            queue.IsEmpty.Should().BeFalse();
            queue.Clear();
            manualCompletions.Should().Be(1);
            queue.IsEmpty.Should().BeTrue();
        }

        [Fact]
        public void ClearByPriority_RunsCanceledCompletionForCarvedMoves()
        {
            var queue = new ObjectActionQueue();
            ObjectActionQueueItem finished = null;
            var carve = new ObjectActionQueueItem(() => { }, action => finished = action);
            var other = new ObjectActionQueueItem(() => { });
            queue.Enqueue(carve, ActionPriority.MoveItem);
            queue.Enqueue(other, ActionPriority.LootItemMedium);

            queue.ClearByPriority(ActionPriority.MoveItem);

            finished.Should().BeSameAs(carve);
            finished.Canceled.Should().BeTrue();
            queue.TryDequeueScheduled(out var item, out _, out _).Should().BeTrue();
            item.Should().BeSameAs(other);
        }

        [Fact]
        public void CancelMovesFor_RunsCompletionCallbacksOfRemovedItems()
        {
            const uint serial = 0x0A000001;
            int completions = 0;

            ObjectActionQueue.Instance.Enqueue(
                new ObjectActionQueueItem(() => { }, _ => completions++) { Serial = serial },
                ActionPriority.LootItemMedium
            );

            ObjectActionQueue.Instance.Enqueue(
                new ObjectActionQueueItem(() => { }, _ => completions++) { Serial = serial },
                ActionPriority.LootItemMedium
            );

            ObjectActionQueue.Instance.CancelMovesFor(serial);

            completions.Should().Be(2, "removed items must still report completion so callers can clear in-flight state");
        }

        [Fact]
        public void CancelMovesFor_LeavesOtherItemsQueued()
        {
            const uint canceledSerial = 0x0A000002;
            const uint keptSerial = 0x0A000003;
            int canceledCompletions = 0;
            int keptCompletions = 0;

            ObjectActionQueue.Instance.Enqueue(
                new ObjectActionQueueItem(() => { }, _ => canceledCompletions++) { Serial = canceledSerial },
                ActionPriority.LootItemMedium
            );

            ObjectActionQueue.Instance.Enqueue(
                new ObjectActionQueueItem(() => { }, _ => keptCompletions++) { Serial = keptSerial },
                ActionPriority.LootItemHigh
            );

            ObjectActionQueue.Instance.CancelMovesFor(canceledSerial);

            canceledCompletions.Should().Be(1);
            keptCompletions.Should().Be(0);
            ObjectActionQueue.Instance.IsEmpty.Should().BeFalse();

            // Drain the remaining action so the shared queue singleton is left empty for other tests.
            ObjectActionQueue.Instance.CancelMovesFor(keptSerial);
            keptCompletions.Should().Be(1);
            ObjectActionQueue.Instance.IsEmpty.Should().BeTrue();
        }
    }
}
