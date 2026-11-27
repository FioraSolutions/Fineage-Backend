using Fineage.Accounts;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// #1112: a step whose economy batch cannot be queued (another commit holds one of its characters, or the worker queue
/// is full) restores the batch and commits it on a later step. The reply waits for that commit instead of hearing
/// <c>economy_commit_failed</c> for a change that is about to land: gatekeeper fees under reconnect churn were refused
/// while the teleport had already happened.
/// </summary>
public sealed class EconomyReplyCarryTests
{
    const int Adena = 57;

    [Fact]
    public async Task A_reply_held_by_another_commit_waits_and_answers_after_its_own_commit()
    {
        var bench = new WorldBench();
        var player = await bench.EnterAsync(items: [new StoredItem(Adena, 50, -1)]);
        bench.World.StartEconomyPersistForTests();
        try
        {
            var reservations = bench.World.EconomyReservationsForTests!;
            // Someone else's logout commit stole this character's dirty bag and is still writing it.
            Assert.True(reservations.TryReserve([player.ObjectId]));

            var destroyed = bench.World.ExecuteAsync(
                player.Session, new DestroyItemCommand(player.Slot(Adena), 1), CancellationToken.None);
            bench.World.StepSimulation();

            Assert.False(destroyed.IsCompleted, $"the reply must wait for the commit, got {(destroyed.IsCompleted ? destroyed.Result : "")}");
            Assert.Equal(1, bench.World.EconomyCarriedReplyCount);

            reservations.Release([player.ObjectId]);
            StepUntil(bench, destroyed);

            Assert.Null(await destroyed);
            Assert.Equal(49, player.Saved().Single(item => item.ItemId == Adena).Count);
            Assert.Equal(0, bench.World.EconomyCarriedReplyCount);
        }
        finally
        {
            bench.World.StopEconomyPersistForTests();
        }
    }

    [Fact]
    public async Task A_reply_that_never_gets_a_commit_is_refused_after_the_budget()
    {
        var bench = new WorldBench();
        var player = await bench.EnterAsync(items: [new StoredItem(Adena, 50, -1)]);
        bench.World.StartEconomyPersistForTests();
        bench.World.EconomyReplyCarryBudget = TimeSpan.FromMilliseconds(200);
        var reservations = bench.World.EconomyReservationsForTests!;
        try
        {
            Assert.True(reservations.TryReserve([player.ObjectId]));
            var destroyed = bench.World.ExecuteAsync(
                player.Session, new DestroyItemCommand(player.Slot(Adena), 1), CancellationToken.None);
            bench.World.StepSimulation();
            Assert.False(destroyed.IsCompleted);

            Thread.Sleep(300);
            bench.World.StepSimulation();

            Assert.True(destroyed.IsCompleted, "a carried reply must be answered once its budget is spent");
            Assert.Equal(ZoneWorld.EconomyCommitFailed, await destroyed);
            Assert.Equal(0, bench.World.EconomyCarriedReplyCount);
        }
        finally
        {
            reservations.Release([player.ObjectId]);
            bench.World.StopEconomyPersistForTests();
        }
    }

    [Fact]
    public async Task A_command_with_nothing_to_commit_still_answers_in_its_own_step()
    {
        var bench = new WorldBench();
        var player = await bench.EnterAsync(items: [new StoredItem(Adena, 50, -1)]);
        var other = await bench.EnterAsync();
        bench.World.StartEconomyPersistForTests();
        var reservations = bench.World.EconomyReservationsForTests!;
        try
        {
            Assert.True(reservations.TryReserve([player.ObjectId]));
            var destroyed = bench.World.ExecuteAsync(
                player.Session, new DestroyItemCommand(player.Slot(Adena), 1), CancellationToken.None);
            var selected = bench.World.ExecuteAsync(
                other.Session, new SelectTargetCommand(player.ObjectId), CancellationToken.None);
            bench.World.StepSimulation();

            // Only the character whose economy is waiting waits; the bystander who shared the step answers now.
            Assert.True(selected.IsCompleted);
            Assert.Null(await selected);
            Assert.False(destroyed.IsCompleted);

            reservations.Release([player.ObjectId]);
            StepUntil(bench, destroyed);
            Assert.Null(await destroyed);
        }
        finally
        {
            bench.World.StopEconomyPersistForTests();
        }
    }

    static void StepUntil(WorldBench bench, Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            bench.World.StepSimulation();
            Thread.Sleep(5);
        }

        Assert.True(task.IsCompleted, "the carried reply never answered");
    }
}
