using ClassicUO.Game;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Managers.Structs;
using FluentAssertions;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    public class ObjectActionQueueTest
    {
        [Fact]
        public void MergedStackRequiresBothInventoryChangesAndLateAckDoesNotReleaseNextMove()
        {
            Client.UnitTestingActive = true;
            const uint sourceSerial = 0x40000001;
            const uint destination = 0x40000003;
            const ushort gold = 0x0EED;
            var queue = new ObjectActionQueue();
            var source = new ItemMoveSource { Serial = sourceSerial, Container = 0x40000002, Amount = 4 };
            queue.BeginMove(source, new MoveRequest(sourceSerial, destination, 4), gold, 0, null);

            var merged = new Item(new World()) { Serial = 0x40000004, Graphic = gold, Amount = 9, Container = destination };
            queue.ObserveContainerUpdate(merged, previousAmount: 5);
            queue.HasPendingMove.Should().BeTrue();
            queue.ObserveSourceRemoval(sourceSerial);
            queue.HasPendingMove.Should().BeFalse();

            const uint nextSerial = 0x40000005;
            queue.BeginMove(new ItemMoveSource { Serial = nextSerial, Amount = 1 },
                new MoveRequest(nextSerial, destination, 1), gold, 0, null);
            queue.ConfirmDropAccepted().Should().BeFalse();
            queue.HasPendingMove.Should().BeTrue();
            queue.ConfirmDropAccepted().Should().BeTrue();
            queue.HasPendingMove.Should().BeFalse();
        }

        [Fact]
        public void AlreadyHoldingRequeuesOnceAndKeepsQueueBlockedUntilCursorIsFree()
        {
            const uint serial = 0x40000001;
            var queue = new ObjectActionQueue();
            int retries = 0;
            queue.BeginMove(new ItemMoveSource { Serial = serial, Amount = 1 },
                new MoveRequest(serial, 0x40000002, 1), 0x0EED, 0, () => retries++);

            queue.RejectPendingMove(serial, alreadyHolding: true);
            queue.RejectPendingMove(serial, alreadyHolding: true);

            retries.Should().Be(1);
            queue.TryGetPendingMove(out _).Should().BeFalse();
            queue.HasPendingMove.Should().BeTrue();
            queue.ConfirmCursorFree();
            queue.HasPendingMove.Should().BeFalse();
        }

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
            queue.Enqueue(firstOpen, ActionPriority.OpenCorpseForAgents);
            queue.Enqueue(nextOpen, ActionPriority.OpenCorpseForAgents);
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
        public void FirstLootMoveRunsBeforeKnifeAndNextCorpseOpen()
        {
            var queue = new ObjectActionQueue();
            var knife = new ObjectActionQueueItem(() => { });
            var nextOpen = new ObjectActionQueueItem(() => { });
            var firstLoot = new ObjectActionQueueItem(() => { });
            var secondLoot = new ObjectActionQueueItem(() => { });

            queue.RecordExecutedPriority(ActionPriority.OpenCorpseForAgents);
            queue.Enqueue(knife, ActionPriority.AutoSkinning);
            queue.Enqueue(nextOpen, ActionPriority.OpenCorpseForAgents);
            queue.Enqueue(firstLoot, ActionPriority.LootItemMedium);
            queue.Enqueue(secondLoot, ActionPriority.LootItemMedium);

            foreach (var expected in new[] { firstLoot, knife, secondLoot, nextOpen })
            {
                queue.TryDequeueScheduled(out var item, out var priority, out _).Should().BeTrue();
                item.Should().BeSameAs(expected);
                queue.RecordExecutedPriority(priority);
            }
        }

        [Fact]
        public void YieldingToLoot_DoesNotStrandCorpseOpensWhenLootIsEmpty()
        {
            var queue = new ObjectActionQueue();
            var open = new ObjectActionQueueItem(() => { });
            queue.RecordExecutedPriority(ActionPriority.OpenCorpseForAgents);
            queue.Enqueue(open, ActionPriority.OpenCorpseForAgents);

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
            queue.Enqueue(new ObjectActionQueueItem(() => { }, _ => automaticCompletions++), ActionPriority.OpenCorpseForAgents);
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
