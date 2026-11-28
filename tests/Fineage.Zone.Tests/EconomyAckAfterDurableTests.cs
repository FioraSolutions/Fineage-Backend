using System.Reflection;
using Fineage.Accounts;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// #1114: a logout commit steals every dirty bag. When it takes the bag a command dirtied in this step, the step finds
/// nothing of that change left to commit. It used to answer the command with success while the logout batch was still
/// being written, so the client heard "done" for a change that was not durable and that a failed batch would restore.
/// The reply now waits for the commit that really carries the change.
/// </summary>
public sealed class EconomyAckAfterDurableTests
{
    const int Adena = 57;

    [Fact]
    public async Task A_change_taken_by_another_logout_commit_is_acked_only_after_that_commit_is_durable()
    {
        var gate = new GatedEconomyStore();
        var bench = new WorldBench(accounts: gate.Store, store: gate.Inner);
        var player = await bench.EnterAsync(items: [new StoredItem(Adena, 50, -1)]);
        var leaver = await bench.EnterAsync();
        bench.World.StartEconomyPersistForTests();
        gate.Armed = true;
        bool logoutQueued = false;
        bench.World.BeforeEconomyReplyReleaseForTests = () =>
        {
            if (logoutQueued)
                return;
            logoutQueued = true;
            // Another character logs out in the window between apply and reply: its commit takes the payer's bag.
            Assert.True(bench.World.CommitEconomyAsLogoutForTests(leaver.ObjectId));
        };
        try
        {
            var destroyed = bench.World.ExecuteAsync(
                player.Session, new DestroyItemCommand(player.Slot(Adena), 1), CancellationToken.None);
            // Raw steps: StepSimulation would wait for every batch, and the logout batch is held in the store.
            bench.World.StepSimulationRawForTests();

            Assert.True(logoutQueued);
            Assert.True(gate.Entered.Wait(TimeSpan.FromSeconds(10)), "the logout commit never reached the store");
            Assert.True(bench.World.IsEconomyInFlightForTests(player.ObjectId));
            bench.World.StepSimulationRawForTests();
            Assert.False(destroyed.IsCompleted,
                $"acked before the logout batch carrying the change was durable: {(destroyed.IsCompleted ? destroyed.Result ?? "success" : "")}");

            gate.Release.Set();
            StepUntil(bench, destroyed);

            Assert.Null(await destroyed);
            Assert.False(bench.World.IsEconomyInFlightForTests(player.ObjectId));
            Assert.Equal(49, player.Saved().Single(item => item.ItemId == Adena).Count);
        }
        finally
        {
            gate.Release.Set();
            bench.World.StopEconomyPersistForTests();
        }
    }

    [Fact]
    public async Task A_change_whose_logout_batch_fails_is_committed_again_before_the_ack()
    {
        var gate = new GatedEconomyStore();
        var bench = new WorldBench(accounts: gate.Store, store: gate.Inner);
        var player = await bench.EnterAsync(items: [new StoredItem(Adena, 50, -1)]);
        var leaver = await bench.EnterAsync();
        bench.World.StartEconomyPersistForTests();
        gate.Armed = true;
        gate.FailOnce = true;
        bool logoutQueued = false;
        bench.World.BeforeEconomyReplyReleaseForTests = () =>
        {
            if (logoutQueued)
                return;
            logoutQueued = true;
            bench.World.CommitEconomyAsLogoutForTests(leaver.ObjectId);
        };
        try
        {
            var destroyed = bench.World.ExecuteAsync(
                player.Session, new DestroyItemCommand(player.Slot(Adena), 1), CancellationToken.None);
            // Raw steps: StepSimulation would wait for every batch, and the logout batch is held in the store.
            bench.World.StepSimulationRawForTests();
            Assert.True(gate.Entered.Wait(TimeSpan.FromSeconds(10)));
            Assert.False(destroyed.IsCompleted);

            // The logout batch fails: the bag is dirty again and a later step's own commit must carry it first.
            gate.Release.Set();
            StepUntil(bench, destroyed);

            Assert.Null(await destroyed);
            Assert.True(gate.Commits >= 2, $"the ack came without a second commit (commits={gate.Commits})");
            Assert.Equal(49, player.Saved().Single(item => item.ItemId == Adena).Count);
        }
        finally
        {
            gate.Release.Set();
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

        Assert.True(task.IsCompleted, "the reply never answered");
    }

    /// <summary>Holds the first economy commit after <see cref="Armed"/> until <see cref="Release"/>.</summary>
    sealed class GatedEconomyStore
    {
        public ManualResetEventSlim Entered { get; } = new(false);
        public ManualResetEventSlim Release { get; } = new(false);
        public IAccountStore Store { get; }
        public MemoryAccountStore Inner { get; } = MemoryAccountStore.InMemory();
        public volatile bool Armed;
        public volatile bool FailOnce;
        int _commits;
        public int Commits => Volatile.Read(ref _commits);

        public GatedEconomyStore()
        {
            var proxy = DispatchProxy.Create<IAccountStore, Proxy>()!;
            ((Proxy)(object)proxy).Init(Inner, this);
            Store = proxy;
        }

        class Proxy : DispatchProxy
        {
            IAccountStore _inner = null!;
            GatedEconomyStore _owner = null!;

            public void Init(IAccountStore inner, GatedEconomyStore owner)
            {
                _inner = inner;
                _owner = owner;
            }

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                ArgumentNullException.ThrowIfNull(targetMethod);
                if (targetMethod.Name == nameof(IAccountStore.CommitEconomy) && _owner.Armed)
                {
                    Interlocked.Increment(ref _owner._commits);
                    _owner.Entered.Set();
                    _owner.Release.Wait();
                    if (_owner.FailOnce)
                    {
                        _owner.FailOnce = false;
                        throw new InvalidOperationException("injected economy commit failure");
                    }
                }

                try
                {
                    return targetMethod.Invoke(_inner, args);
                }
                catch (TargetInvocationException ex) when (ex.InnerException != null)
                {
                    throw ex.InnerException;
                }
            }
        }
    }
}
