using System.Reflection;
using Fineage.Accounts;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// #1116: a fast relog loaded the character while the previous session's logout save was still being written. The
/// enter either inserted the pre-logout body or, when a snapshot moved under it, was refused "changed while loading".
/// The enter now chains after that save and loads the durable state. When the save does not land within the budget,
/// the enter is refused and the refusal says so.
/// </summary>
public sealed class RelogChainsAfterLogoutSaveTests
{
    const int Adena = 57;

    [Fact]
    public async Task Relog_waits_for_the_previous_sessions_logout_save_and_loads_what_it_wrote()
    {
        var gate = new GatedEconomyStore();
        var bench = new WorldBench(accounts: gate.Store, store: gate.Inner);
        var player = await bench.EnterAsync(items: [new StoredItem(Adena, 50, -1)]);
        bench.World.StartEconomyPersistForTests();
        try
        {
            var leave = HoldLogoutSaveInFlight(bench, gate, player);

            var relog = Relog(player);
            var entering = Task.Run(() => bench.World.TryEnterAsync(relog, CancellationToken.None));
            await Task.Delay(300);
            Assert.False(entering.IsCompleted, "the relog entered while the logout save was still in flight");
            Assert.Equal(50, player.Saved().Single(item => item.ItemId == Adena).Count);

            gate.Release.Set();
            await leave.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.True(await entering.WaitAsync(TimeSpan.FromSeconds(30)), relog.EnterRefusal);

            Assert.True(bench.World.TryGetLivePlayer(relog, out var live));
            Assert.Equal(49, live.Inventory!.Items.Single(item => item.ItemId == Adena).Count);
            Assert.Equal(49, player.Saved().Single(item => item.ItemId == Adena).Count);
            Assert.False(bench.World.IsLogoutSavePendingForTests(player.ObjectId));
        }
        finally
        {
            gate.Release.Set();
            bench.World.StopEconomyPersistForTests();
        }
    }

    [Fact]
    public async Task Relog_is_refused_explicitly_when_the_logout_save_outlives_the_budget()
    {
        var gate = new GatedEconomyStore();
        var bench = new WorldBench(accounts: gate.Store, store: gate.Inner);
        var player = await bench.EnterAsync(items: [new StoredItem(Adena, 50, -1)]);
        bench.World.StartEconomyPersistForTests();
        bench.World.EnterChainBudget = TimeSpan.FromMilliseconds(300);
        Task? leave = null;
        try
        {
            leave = HoldLogoutSaveInFlight(bench, gate, player);

            var relog = Relog(player);
            bool entered = await Task.Run(() => bench.World.TryEnterAsync(relog, CancellationToken.None))
                .WaitAsync(TimeSpan.FromSeconds(30));

            Assert.False(entered);
            Assert.Contains("logout save still in flight", relog.EnterRefusal);
            Assert.False(bench.World.TryGetLivePlayer(relog, out _), "a refused relog must not insert a stale body");
        }
        finally
        {
            gate.Release.Set();
            if (leave != null)
                await leave.WaitAsync(TimeSpan.FromSeconds(30));
            bench.World.StopEconomyPersistForTests();
        }
    }

    /// <summary>
    /// The live bag is changed (one adena destroyed) and its in-step commit is held inside the store, then the session
    /// logs out. The logout save cannot land until the gate opens, and the store still says 50.
    /// </summary>
    static Task HoldLogoutSaveInFlight(WorldBench bench, GatedEconomyStore gate, BenchPlayer player)
    {
        gate.Armed = true;
        _ = bench.World.ExecuteAsync(player.Session, new DestroyItemCommand(player.Slot(Adena), 1), CancellationToken.None);
        bench.World.StepSimulationRawForTests();
        Assert.True(gate.Entered.Wait(TimeSpan.FromSeconds(10)), "the economy commit never reached the store");

        var leave = Task.Run(() => bench.World.LeaveAsync(player.Session, CancellationToken.None));
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!bench.World.IsLogoutSavePendingForTests(player.ObjectId) && DateTime.UtcNow < deadline)
            Thread.Sleep(5);
        Assert.True(bench.World.IsLogoutSavePendingForTests(player.ObjectId), "the logout save was never registered");
        return leave;
    }

    static ZoneSession Relog(BenchPlayer player)
    {
        var session = new ZoneSession();
        session.Bind(player.Ticket.CharacterId, player.Ticket.Account, player.Ticket.CharacterName,
            player.Ticket.ClassId, player.Ticket.Race, player.Ticket.Sex);
        return session;
    }

    /// <summary>Holds every economy commit after <see cref="Armed"/> until <see cref="Release"/>.</summary>
    sealed class GatedEconomyStore
    {
        public ManualResetEventSlim Entered { get; } = new(false);
        public ManualResetEventSlim Release { get; } = new(false);
        public IAccountStore Store { get; }
        public MemoryAccountStore Inner { get; } = MemoryAccountStore.InMemory();
        public volatile bool Armed;

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
