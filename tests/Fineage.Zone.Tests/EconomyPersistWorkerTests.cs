using System.Reflection;
using Fineage.Accounts;
using Fineage.Game;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class EconomyPersistWorkerTests
{
    [Fact]
    public void Commit_runs_on_persist_worker_not_simulation_thread()
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
        var flush = Task.Run(() => bench.World.FlushDirtyInventoriesForTests());
        Assert.True(gate.Entered.Wait(TimeSpan.FromSeconds(10)));
        Assert.NotEqual(simThread, gate.WorkerThreadId);
        Assert.NotEqual(simThread, bench.World.EconomyPersistWriterThreadForTests);

        gate.Release.Set();
        Assert.True(flush.GetAwaiter().GetResult());
        bench.World.StopEconomyPersistForTests();
    }

    [Fact]
    public void Async_disabled_uses_inline_commit()
    {
        var prior = Environment.GetEnvironmentVariable("FINEAGE_ECONOMY_ASYNC_PERSIST");
        try
        {
            Environment.SetEnvironmentVariable("FINEAGE_ECONOMY_ASYNC_PERSIST", "0");
            var bench = new WorldBench();
            Assert.Equal(AddAccountStatus.Created, bench.Store.TryAddAccount("a", "h", "s", 1, null));
            Assert.Equal(CreateCharacterStatus.Ok, bench.Store.TryAddCharacter("a", "A", 0, 0, 0, out var created));
            var session = new ZoneSession();
            session.Bind(created!.Id, "a", "A", 0);
            bench.World.StageDeferredInventoryForTest(session, new PlayerInventory(bench.Game.Items));
            Assert.True(bench.World.FlushDirtyInventoriesForTests());
        }
        finally
        {
            Environment.SetEnvironmentVariable("FINEAGE_ECONOMY_ASYNC_PERSIST", prior);
        }
    }

    sealed class BlockingAccountStore
    {
        public ManualResetEventSlim Entered { get; } = new(false);
        public ManualResetEventSlim Release { get; } = new(false);
        public int WorkerThreadId { get; private set; }
        public IAccountStore Store { get; }

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
                if (targetMethod.Name == nameof(IAccountStore.CommitEconomy))
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