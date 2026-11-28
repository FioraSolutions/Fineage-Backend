using Fineage.Accounts;
using Fineage.Game.Economy;
using Fineage.Game.Services;
using Fineage.Net.Contract;
using Fineage.Protocol.Telemetry;
using Fineage.Zone.Interest;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Regression tests for zone-consistency-review P0/P1 stability items (2026-11-22).</summary>
public sealed class ZoneConsistencyRegressionTests
{
    const int Adena = WarehouseRules.AdenaItemId;

    [Fact]
    public async Task Same_session_reenter_flushes_deferred_inventory_and_matches_store()
    {
        var bench = new WorldBench();
        var player = await bench.EnterAsync(items: [new StoredItem(Adena, 10, -1)]);
        Assert.Null(await player.World.TryGiveItemAsync(player.Session, Adena, 90, CancellationToken.None));
        Assert.Equal(100, player.Inventory.CountOf(Adena));
        Assert.Equal(100, player.Saved().Sum(row => row.ItemId == Adena ? row.Count : 0));

        bench.World.StageDeferredInventoryForTest(player.Session, player.Inventory);
        Assert.True(bench.World.HasDeferredInventoryForTests(player.ObjectId));

        Assert.True(await bench.World.TryEnterAsync(player.Session, CancellationToken.None));
        Assert.True(bench.World.TryGetLivePlayer(player.Session, out var relogged));
        Assert.Equal(100, relogged.Inventory!.CountOf(Adena));
        Assert.Equal(100, player.Saved().Sum(row => row.ItemId == Adena ? row.Count : 0));
        Assert.False(bench.World.HasDeferredInventoryForTests(player.ObjectId));
    }

    [Fact]
    public async Task Ledger_flush_failure_blocks_deferred_inventory_replace()
    {
        var journal = new FlakyLedgerJournal(failuresBeforeSuccess: 0);
        var bench = new WorldBench(ledgerJournal: journal);
        var player = await bench.EnterAsync(items: [new StoredItem(Adena, 5, -1)]);
        Assert.True(bench.World.DrainEconomyPersistForTests());
        journal.ArmFailures(1);

        var bag = LedgerAccount.Inventory(player.ObjectId);
        var shop = LedgerAccount.Of(SystemAccount.NpcShop);
        long tick = bench.Ledger.Entries[^1].Transaction.Id.Tick + 1;
        Assert.True(bench.World.LedgerForTests.Submit(new LedgerTransaction(
            new LedgerTxId(tick, 0),
            LedgerTxKind.Adjustment,
            0,
            0,
            0,
            [new LedgerPosting(shop, Adena, -1), new LedgerPosting(bag, Adena, 1)])).Ok);
        Assert.True(journal.PendingFlushCount > 0);
        bench.World.StageDeferredInventoryForTest(player.Session, player.Inventory);
        Assert.True(bench.World.HasDeferredInventoryForTests(player.ObjectId));

        using var metrics = new MetricsCollector();
        // Off the step the flush reports its own commit: a failed ledger flush is a failed flush, not a queued one.
        Assert.False(bench.World.FlushDirtyInventoriesForTests());
        Assert.True(bench.World.DrainEconomyPersistForTests());

        Assert.True(metrics.Snapshot().LedgerFlushFailures >= 1);
        Assert.True(bench.World.HasDeferredInventoryForTests(player.ObjectId));
        Assert.Equal(5, player.Saved().Sum(row => row.ItemId == Adena ? row.Count : 0));
    }

    [Fact]
    public async Task Party_adena_pickup_commit_failure_keeps_ground_item()
    {
        var kit = new NetTestKit();
        var (session, picker) = await kit.EnterAsync();
        var (_, second) = await kit.EnterAsync();
        kit.World.JoinPartyForTest(picker, second);
        Assert.Null(await kit.World.TryGiveItemAsync(session, Adena, 50, default));
        var adena = picker.Inventory!.Items.Single(item => item.ItemId == Adena);

        Assert.Null(await kit.World.TryDropItemAsync(session, adena.ObjectId, 40, 0, 0, 0, default));
        var drop = kit.Ledger.Entries.Last(tx => tx.Transaction.Kind == LedgerTxKind.Drop);
        int groundId = drop.Transaction.Postings.Single(p => p.Account.Container == LedgerContainer.Ground).Account.OwnerId;
        Assert.True(kit.World.TryGetEntityById(groundId, out _));

        int before = picker.Inventory!.CountOf(Adena);
        Assert.Null(await kit.World.TryPickupAsync(session, groundId, default));
        Assert.False(kit.World.TryGetEntityById(groundId, out _));
        Assert.True(picker.Inventory.CountOf(Adena) > before);
        Assert.NotNull(kit.Ledger.Entries.LastOrDefault(e => e.Transaction.Kind == LedgerTxKind.Pickup));
    }

    [Fact]
    public async Task Same_tick_knownlist_add_then_despawn_sends_no_entity_spawn()
    {
        var kit = new NetTestKit(aoi: new GridAoi());
        var (_, player) = await kit.EnterAsync();
        int mobId = kit.World.SpawnMonsterForTests(20001, player.X + 80, player.Y, player.Z).ObjectId;

        var hub = new NetPresenceHub();
        var ids = new EntityIdMap();
        var publisher = new NetWorldEventPublisher(kit.World, ids, hub, _ => 300);
        var drain = ((IWorldEventPublisher)publisher).PrepareKnownlistDiff(player.ObjectId, [mobId], []);
        kit.World.DespawnNpcForTests(mobId);
        drain();

        Assert.Equal(0, publisher.KnownlistSpawnCountAtDrain);
        Assert.False(kit.World.TryGetEntityById(mobId, out _));
    }

    [Fact]
    public async Task Aoi_refresh_mid_run_emits_move_state()
    {
        var kit = new NetTestKit();
        var (_, mover) = await kit.EnterAsync();
        var (_, watcher) = await kit.EnterAsync();
        mover.MoveDestX = mover.X + 800;
        mover.MoveDestY = mover.Y;
        mover.MoveDestZ = mover.Z;
        kit.World.SetMoveActive(mover, true);

        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        kit.World.FanoutMoveSyncToNewWatchersForTests(mover, [watcher.ObjectId], legAckAlreadySent: false);

        Assert.Contains(rec.Of(nameof(IWorldEventPublisher.FanoutMoveState)),
            args => (int)args[1]! == mover.ObjectId && (int)args[5]! == mover.MoveDestX);
    }

    sealed class FlakyLedgerJournal(int failuresBeforeSuccess) : MemoryLedgerJournal
    {
        int _failuresLeft = failuresBeforeSuccess;

        internal void ArmFailures(int count) => _failuresLeft = count;
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
}