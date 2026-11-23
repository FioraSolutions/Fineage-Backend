using System.Reflection;
using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Game.Services;
using Fineage.Protocol.Telemetry;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Fault-injection harness for economy durability (architecture review §7).</summary>
public sealed class EconomyFaultInjectionTests
{
    const int Adena = WarehouseRules.AdenaItemId;

    [Fact]
    public async Task Commit_failure_restores_dirty_and_preserves_store()
    {
        var journal = new FlakyLedgerJournal(failuresBeforeSuccess: 1);
        var bench = new WorldBench(ledgerJournal: journal);
        var player = await bench.EnterAsync(items: [new StoredItem(Adena, 5, -1)]);
        var bag = LedgerAccount.Inventory(player.ObjectId);
        var shop = LedgerAccount.Of(SystemAccount.NpcShop);
        Assert.True(bench.World.LedgerForTests.Submit(new LedgerTransaction(
            new LedgerTxId(1, 0),
            LedgerTxKind.Adjustment,
            0,
            0,
            0,
            [new LedgerPosting(shop, Adena, -1), new LedgerPosting(bag, Adena, 1)])).Ok);
        bench.World.StageDeferredInventoryForTest(player.Session, player.Inventory!);
        using var metrics = new MetricsCollector();
        Assert.True(bench.World.FlushDirtyInventoriesForTests());
        Assert.True(bench.World.DrainEconomyPersistForTests());
        Assert.True(metrics.Snapshot().LedgerFlushFailures >= 1);
        Assert.True(bench.World.HasDeferredInventoryForTests(player.ObjectId));
        Assert.Equal(5, player.Saved().Sum(row => row.ItemId == Adena ? row.Count : 0));
        EconomyInvariantChecker.AssertHold(bench.World.LedgerForTests);
    }

    [Fact]
    public async Task After_successful_commit_world_reconciler_is_clean()
    {
        var bench = new WorldBench();
        var player = await bench.EnterAsync(items: [new StoredItem(Adena, 100, -1)]);
        Assert.Null(await bench.World.TryGiveItemAsync(player.Session, Adena, 50, CancellationToken.None));
        Assert.True(bench.World.FlushDirtyInventoriesForTests());
        Assert.True(bench.World.DrainEconomyPersistForTests());
        EconomyInvariantChecker.AssertHold(bench.World.LedgerForTests);
    }

    [Fact]
    public async Task In_flight_batch_blocks_second_enqueue_with_reservation_conflict()
    {
        var gate = new BlockingAccountStore();
        var bench = new WorldBench(accounts: gate.Store);
        Assert.Equal(AddAccountStatus.Created, gate.Store.TryAddAccount("busy", "hash", "salt", 1, null));
        Assert.Equal(CreateCharacterStatus.Ok,
            gate.Store.TryAddCharacter("busy", "Busy", 0, 0, 0, out var created, [new StoredItem(Adena, 1, -1)]));
        var session = new ZoneSession();
        session.Bind(created!.Id, "busy", "Busy", 0);
        bench.World.StageDeferredInventoryForTest(session, new PlayerInventory(bench.Game.Items));
        var flush = Task.Run(() => Assert.True(bench.World.FlushDirtyInventoriesForTests()));
        Assert.True(gate.Entered.Wait(TimeSpan.FromSeconds(10)), "first batch did not reach CommitEconomy");
        bench.World.StageDeferredInventoryForTest(session, new PlayerInventory(bench.Game.Items));
        Assert.False(bench.World.FlushDirtyInventoriesForTests());
        gate.Release.Set();
        await flush;
        Assert.True(bench.World.DrainEconomyPersistForTests());
    }

    [Fact]
    public async Task Postgres_failure_restores_dirty_without_partial_store_write()
    {
        var store = new FailingAccountStore();
        var bench = new WorldBench(accounts: store.Store);
        var player = await bench.EnterAsync(items: [new StoredItem(Adena, 7, -1)]);
        Assert.Null(await bench.World.TryGiveItemAsync(player.Session, Adena, 3, CancellationToken.None));
        Assert.True(bench.World.HasDeferredInventoryForTests(player.ObjectId));
        using var metrics = new MetricsCollector();
        Assert.True(bench.World.FlushDirtyInventoriesForTests());
        Assert.True(bench.World.DrainEconomyPersistForTests());
        Assert.True(metrics.Snapshot().LedgerFlushFailures >= 1);
        Assert.Equal(7, player.Saved().Sum(row => row.ItemId == Adena ? row.Count : 0));
        EconomyInvariantChecker.AssertHold(bench.World.LedgerForTests);
    }

    [Fact]
    public void Duplicate_ledger_tx_id_is_idempotent_not_double_applied()
    {
        var ledger = new EconomyLedger(new MemoryLedgerJournal(), 3_600_000);
        var bag = LedgerAccount.Inventory(42);
        var untracked = LedgerAccount.Of(SystemAccount.Untracked);
        var tx = new LedgerTransaction(
            new LedgerTxId(10, 0),
            LedgerTxKind.Adjustment,
            0,
            0,
            0,
            [
                new LedgerPosting(untracked, Adena, -5),
                new LedgerPosting(bag, Adena, 5),
            ]);
        Assert.Equal(LedgerOutcome.Committed, ledger.Submit(tx).Outcome);
        Assert.Equal(5, ledger.HoldingsOf(bag).CountOf(Adena));
        var again = ledger.Submit(tx);
        Assert.True(again.Ok);
        Assert.Equal(LedgerOutcome.Duplicate, again.Outcome);
        Assert.Equal(5, ledger.HoldingsOf(bag).CountOf(Adena));
    }

    sealed class FlakyLedgerJournal(int failuresBeforeSuccess) : MemoryLedgerJournal
    {
        int _failuresLeft = failuresBeforeSuccess;
        int _unflushed;

        public override void Append(LedgerEntry entry)
        {
            base.Append(entry);
            _unflushed++;
        }

        public override int PendingFlushCount => _unflushed;

        public override void Flush()
        {
            if (_failuresLeft > 0)
            {
                _failuresLeft--;
                throw new InvalidOperationException("simulated auditable economy ledger flush failure");
            }

            _unflushed = 0;
        }
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

    sealed class FailingAccountStore
    {
        public IAccountStore Store { get; }

        public FailingAccountStore()
        {
            var proxy = DispatchProxy.Create<IAccountStore, Proxy>()!;
            ((Proxy)(object)proxy).Init(MemoryAccountStore.InMemory());
            Store = proxy;
        }

        class Proxy : DispatchProxy
        {
            IAccountStore _inner = null!;

            public void Init(IAccountStore inner) => _inner = inner;

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                ArgumentNullException.ThrowIfNull(targetMethod);
                if (targetMethod.Name == nameof(IAccountStore.CommitEconomy))
                    throw new InvalidOperationException("simulated postgres unavailable");

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