using System.Reflection;
using Fineage.Accounts;
using Fineage.Game;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Logout used to commit the economy while holding the same lock the simulation step's flush takes.
/// A mass leave then pinned the step for as long as those writes were queued, with no region body in flight.
/// </summary>
public sealed class EconomyCommitLockTests
{
    [Fact]
    public async Task Logout_sql_does_not_hold_the_lock_the_step_flush_needs()
    {
        var gate = new BlockingAccountStore();
        var bench = new WorldBench(accounts: gate.Store);
        Assert.Equal(AddAccountStatus.Created, gate.Store.TryAddAccount("loadbot", "hash", "salt", 1, null));
        Assert.Equal(CreateCharacterStatus.Ok, gate.Store.TryAddCharacter("loadbot", "Bot", 0, 0, 0, out var created));
        var session = new ZoneSession();
        session.Bind(created!.Id, "loadbot", "Bot", 0);
        bench.World.StageDeferredInventoryForTest(session, new PlayerInventory(bench.Game.Items));

        var logout = Task.Run(() => bench.World.FlushDirtyInventoriesForTests());
        var entered = Task.Run(() => gate.Entered.Wait(TimeSpan.FromSeconds(10)));
        Assert.True(await entered, "logout commit did not reach the store");

        // The step's end-of-tick flush. It must return while logout is still inside CommitEconomy.
        var stepFlush = Task.Run(() => bench.World.FlushDirtyInventoriesForTests());
        var finished = await Task.WhenAny(stepFlush, Task.Delay(500));
        Assert.True(
            finished == stepFlush,
            "the step flush waited on the economy lock while logout SQL was still inside CommitEconomy");

        gate.Release.Set();
        Assert.True(await logout);
        Assert.True(await stepFlush);
    }

    sealed class BlockingAccountStore
    {
        public ManualResetEventSlim Entered { get; } = new(false);
        public ManualResetEventSlim Release { get; } = new(false);
        public IAccountStore Store { get; }

        public BlockingAccountStore()
        {
            var proxy = DispatchProxy.Create<IAccountStore, Proxy>()!;
            ((Proxy)(object)proxy).Init(MemoryAccountStore.InMemory(), this);
            Store = proxy;
        }

        class Proxy : DispatchProxy
        {
            IAccountStore _inner = null!;
            BlockingAccountStore _owner = null!;

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
