using System.Reflection;
using Fineage.Accounts;
using Fineage.Game;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class EconomyPersistWorkerTests
{
    [Fact]
    public async Task Commit_runs_on_persist_worker_not_simulation_thread()
    {
        var gate = new BlockingAccountStore();
        var bench = new WorldBench(accounts: gate.Store);
        bench.World.StartEconomyPersistForTests();
        Assert.Equal(AddAccountStatus.Created, gate.Store.TryAddAccount("loadbot", "hash", "salt", 1, null));
        Assert.Equal(CreateCharacterStatus.Ok, gate.Store.TryAddCharacter("loadbot", "Bot", 0, 0, 0, out var created));
        var session = new ZoneSession();
        session.Bind(created!.Id, "loadbot", "Bot", 0);
        bench.World.StageDeferredInventoryForTest(session, new PlayerInventory(bench.Game.Items));

        int simThread = Environment.CurrentManagedThreadId;
        var flush = Task.Run(() =>
        {
            Assert.True(bench.World.FlushDirtyInventoriesForTests());
            return bench.World.DrainEconomyPersistForTests();
        });
        Assert.True(gate.Entered.Wait(TimeSpan.FromSeconds(10)));
        Assert.NotEqual(simThread, gate.WorkerThreadId);

        gate.Release.Set();
        Assert.True(await flush);
        Assert.NotEqual(simThread, bench.World.EconomyPersistWriterThreadForTests);
        bench.World.StopEconomyPersistForTests();
    }

    /// <summary>
    /// #990 reconnect storm: a logout commit off the step used to take the world turn and then wait for every batch
    /// in flight, so the simulation froze for the whole Postgres write (up to 30 s while new logouts kept queueing).
    /// The leaver now waits for its own batch and the step keeps running meanwhile.
    /// </summary>
    [Fact]
    public async Task Off_step_logout_commit_does_not_hold_the_world_turn_while_its_batch_is_in_flight()
    {
        var gate = new BlockingAccountStore();
        var bench = new WorldBench(accounts: gate.Store);
        bench.World.StartEconomyPersistForTests();
        Assert.Equal(AddAccountStatus.Created, gate.Store.TryAddAccount("loadbot", "hash", "salt", 1, null));
        Assert.Equal(CreateCharacterStatus.Ok, gate.Store.TryAddCharacter("loadbot", "Bot", 0, 0, 0, out var created));
        var session = new ZoneSession();
        session.Bind(created!.Id, "loadbot", "Bot", 0);
        bench.World.StageDeferredInventoryForTest(session, new PlayerInventory(bench.Game.Items));

        var commit = Task.Run(() => bench.World.CommitEconomyOffStepForTests(session.ObjectId));
        Assert.True(gate.Entered.Wait(TimeSpan.FromSeconds(10)));

        var step = Task.Run(() => bench.World.RunStepForTest(() => { }));
        bool stepRan = step.Wait(TimeSpan.FromSeconds(5));
        bool commitWaiting = !commit.IsCompleted;
        gate.Release.Set();
        Assert.True(stepRan, "a world step could not take the turn while a logout commit was in Postgres");
        Assert.True(commitWaiting, "the logout commit returned before its batch was durable");
        Assert.True(await commit.WaitAsync(TimeSpan.FromSeconds(30)));
        await step.WaitAsync(TimeSpan.FromSeconds(30));
        bench.World.StopEconomyPersistForTests();
    }

    /// <summary>
    /// #990 reconnect storm: a step whose commands dirtied no economy left their replies to "some pending batch"
    /// whenever any batch was in flight (a logout's), and no batch ever answered them: the enter never completed.
    /// </summary>
    [Fact]
    public async Task Step_answers_its_own_replies_while_an_unrelated_logout_batch_is_in_flight()
    {
        var gate = new BlockingAccountStore { Armed = false };
        var bench = new WorldBench(accounts: gate.Store);
        bench.World.StartEconomyPersistForTests();
        var player = await bench.EnterAsync();
        Assert.Equal(AddAccountStatus.Created, gate.Store.TryAddAccount("loadbot", "hash", "salt", 1, null));
        Assert.Equal(CreateCharacterStatus.Ok, gate.Store.TryAddCharacter("loadbot", "Bot", 0, 0, 0, out var created));
        var leaver = new ZoneSession();
        leaver.Bind(created!.Id, "loadbot", "Bot", 0);
        bench.World.StageDeferredInventoryForTest(leaver, new PlayerInventory(bench.Game.Items));

        gate.Armed = true;
        var logout = Task.Run(() => bench.World.CommitEconomyOffStepForTests(leaver.ObjectId));
        Assert.True(gate.Entered.Wait(TimeSpan.FromSeconds(10)));

        var reply = bench.World.ExecuteAsync(player.Session, new CancelTargetCommand(), CancellationToken.None);
        bench.World.StepSimulationRawForTests();
        bool answered = reply.Wait(TimeSpan.FromSeconds(5));
        gate.Release.Set();
        Assert.True(answered, "a command with no economy write waited on someone else's logout batch");
        Assert.Null(await reply);
        Assert.True(await logout.WaitAsync(TimeSpan.FromSeconds(30)));
        bench.World.StopEconomyPersistForTests();
    }

    [Fact]
    public void Blocking_wait_on_simulation_thread_throws()
    {
        var bench = new WorldBench();
        bench.World.StartEconomyPersistForTests();
        bench.World.DriveForTest();
        bench.World.RunStepForTest(() =>
        {
            Assert.Throws<InvalidOperationException>(() =>
                bench.World.EconomyPersistBlockingWaitForTests());
        });
        bench.World.StopEconomyPersistForTests();
    }

    sealed class BlockingAccountStore
    {
        public ManualResetEventSlim Entered { get; } = new(false);
        public ManualResetEventSlim Release { get; } = new(false);
        public int WorkerThreadId { get; private set; }
        public IAccountStore Store { get; }

        /// <summary>Only an armed gate holds a commit; a test arms it after its own setup commits.</summary>
        public volatile bool Armed = true;

        public BlockingAccountStore()
        {
            var proxy = DispatchProxy.Create<IAccountStore, Proxy>()!;
            ((Proxy)(object)proxy).Init(MemoryAccountStore.InMemory(), this);
            Store = proxy;
        }

        class Proxy : DispatchProxy
        {
            BlockingAccountStore _owner = null!;
            IAccountStore _inner = null!;

            public void Init(IAccountStore inner, BlockingAccountStore owner)
            {
                _inner = inner;
                _owner = owner;
            }

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                ArgumentNullException.ThrowIfNull(targetMethod);
                if (targetMethod.Name == nameof(IAccountStore.CommitEconomy) && _owner.Armed)
                {
                    _owner.WorkerThreadId = Environment.CurrentManagedThreadId;
                    _owner.Entered.Set();
                    _owner.Release.Wait();
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