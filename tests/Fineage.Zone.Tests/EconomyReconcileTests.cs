using Fineage.Accounts;
using Fineage.Game.Economy;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;
using static Fineage.Zone.Tests.StoredStateAuditTests;

namespace Fineage.Zone.Tests;

/// <summary>
/// #955: the auditable economy ledger reconciled against the store and the live world, before and after a restart,
/// after a crash inside the commit and after a crash between the commit and its publish. A restart is a new world over
/// the same store and the same durable journal; a crash drops the old world without a leave or a shutdown flush.
/// </summary>
public sealed class EconomyReconcileTests
{
    const int KeeperObjectId = 61001;
    const int Adena = 57;
    const int Scroll = 736;
    const int Harvester = 5125;

    internal sealed class Rig
    {
        public List<LedgerEntry> Durable { get; } = [];
        public DurableStore Store { get; } = new();

        public WorldBench Boot()
        {
            var map = new SpawnMapDefinition(SpawnCatalog.DefaultMapId, true,
            [
                new SpawnNpcDefinition(KeeperObjectId, 30005, "Wilford", 0, 0, 0, 0, false, 100, 80f, 40f, 60, 0,
                    "Warehouse Keeper", "Warehouse")
            ]);
            return new WorldBench(
                accounts: Store.Proxy, store: Store.Inner, ledgerJournal: new TxJournal(Durable),
                spawns: SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition> { [SpawnCatalog.DefaultMapId] = map }));
        }
    }

    /// <summary>A grant, a warehouse deposit, a piece dropped by one player and picked up by another, adena left on the ground.</summary>
    static async Task<(BenchPlayer A, BenchPlayer B, int Ground)> PlayAsync(WorldBench bench)
    {
        var a = await bench.EnterAsync("reca", "RecA",
            items: [new StoredItem(Adena, 1000, -1), new StoredItem(Scroll, 10, -1), new StoredItem(Harvester, 1, -1)], x: 0, y: 0, z: 0);
        var b = await bench.EnterAsync("recb", "RecB", items: [new StoredItem(Adena, 10, -1)], x: 20, y: 0, z: 0);
        Assert.Null(await bench.World.TryGiveItemAsync(a.Session, Adena, 250, CancellationToken.None));
        Assert.Null(await a.BypassAsync(KeeperObjectId, "DepositP"));
        var scrolls = a.Player.Inventory!.Items.Single(item => item.ItemId == Scroll);
        Assert.Null(await a.Deposit(KeeperObjectId, (scrolls.ObjectId, 4)));
        int piece = a.Player.Inventory.Items.Single(item => item.ItemId == Harvester).ObjectId;
        Assert.Null(await bench.World.TryDropItemAsync(a.Session, piece, 1, 10, 0, 0, CancellationToken.None));
        Assert.Null(await bench.World.TryPickupAsync(b.Session, piece, CancellationToken.None));
        var adena = a.Player.Inventory.Items.Single(item => item.ItemId == Adena);
        Assert.Null(await bench.World.TryDropItemAsync(a.Session, adena.ObjectId, 33, 10, 0, 0, CancellationToken.None));
        int ground = bench.World.LedgerForTests.NonEmptyAccounts().Single(x => x.Account.Container == LedgerContainer.Ground).Account.OwnerId;
        return (a, b, ground);
    }

    internal static EconomyReconcileReport Reconciled(WorldBench bench, string phase)
    {
        Assert.True(bench.World.FlushEconomyForReconcile(TimeSpan.FromSeconds(10)));
        var report = bench.World.ReconcileEconomy(phase);
        report.ThrowIfViolations();
        return report;
    }

    [Fact]
    public async Task Ledger_store_and_world_agree_before_and_after_a_restart()
    {
        var rig = new Rig();
        var first = rig.Boot();
        first.World.StartEconomyPersistForTests();
        var (a, b, ground) = await PlayAsync(first);
        var before = Reconciled(first, "before-restart");
        Assert.True(before.GroundStacks == 1 && before.LiveContainers >= 5, before.Summary);
        Assert.True(before.StoredOwners >= 2, before.Summary);
        long aAdena = first.World.LedgerForTests.HoldingsOf(LedgerAccount.Inventory(a.ObjectId)).CountOf(Adena);
        var bBag = first.World.LedgerForTests.HoldingsOf(LedgerAccount.Inventory(b.ObjectId));

        // Graceful restart: both players leave (their bags commit), the world stops, a new one boots on the same store.
        await first.World.LeaveAsync(a.Session, CancellationToken.None);
        await first.World.LeaveAsync(b.Session, CancellationToken.None);
        Reconciled(first, "after-leave");
        first.World.StopEconomyPersistForTests();

        var second = rig.Boot();
        var after = Reconciled(second, "after-restart");
        Assert.Equal(aAdena, second.World.LedgerForTests.HoldingsOf(LedgerAccount.Inventory(a.ObjectId)).CountOf(Adena));
        Assert.True(second.World.LedgerForTests.HoldingsOf(LedgerAccount.Inventory(b.ObjectId)).SameAs(bBag));
        Assert.Equal(4, second.World.LedgerForTests.HoldingsOf(LedgerAccount.Warehouse(a.ObjectId)).CountOf(Scroll));
        // The ground does not survive a restart: its stack leaves through an explicit Expire, not a silent drop.
        Assert.True(second.World.LedgerForTests.HoldingsOf(LedgerAccount.Ground(ground)).IsEmpty);
        Assert.Equal(0, after.GroundStacks);

        // And the players come back with exactly what the ledger says.
        var a2 = await second.EnterAsync("reca", "RecA", x: 0, y: 0, z: 0);
        Assert.Equal(aAdena, a2.Player.Inventory!.Items.Where(i => i.ItemId == Adena).Sum(i => (long)i.Count));
        Reconciled(second, "after-reenter");
    }

    [Fact]
    public async Task Crash_inside_the_commit_loses_nothing_and_duplicates_nothing_after_restart()
    {
        var rig = new Rig();
        var first = rig.Boot();
        first.World.StartEconomyPersistForTests();
        var (a, _, _) = await PlayAsync(first);
        Reconciled(first, "before-crash");
        long committed = rig.Store.Inner.FindByCharacterId(a.ObjectId)!.Items!.Where(r => r.ItemId == Adena).Sum(r => r.Count);
        int journal = rig.Durable.Count;

        // The next give dies inside the database transaction: neither the journal rows nor the bag land. Then the
        // process dies (the world is dropped with no leave and no shutdown flush).
        rig.Store.FailCommits = true;
        Assert.Null(await first.World.TryGiveItemAsync(a.Session, Adena, 77, CancellationToken.None));
        Assert.False(first.World.FlushEconomyForReconcile(TimeSpan.FromSeconds(10)));
        var dirty = first.World.ReconcileEconomy("crashed-world");
        Assert.Contains(dirty.Violations, v => v.Code == EconomyReconcileViolation.NotQuiescent);
        Assert.Equal(journal, rig.Durable.Count);

        rig.Store.FailCommits = false;
        var second = rig.Boot();
        Reconciled(second, "after-crash");
        Assert.Equal(committed, second.World.LedgerForTests.HoldingsOf(LedgerAccount.Inventory(a.ObjectId)).CountOf(Adena));
    }

    [Fact]
    public async Task Crash_after_the_commit_before_its_publish_keeps_the_committed_state()
    {
        var rig = new Rig();
        var first = rig.Boot();
        first.World.StartEconomyPersistForTests();
        var (a, _, _) = await PlayAsync(first);
        Reconciled(first, "before");
        Assert.Null(await first.World.TryGiveItemAsync(a.Session, Adena, 5, CancellationToken.None));
        Assert.True(first.World.FlushDirtyInventoriesForTests());
        long expected = first.World.LedgerForTests.HoldingsOf(LedgerAccount.Inventory(a.ObjectId)).CountOf(Adena);
        // The worker committed (journal + bag in one transaction); the world dies before draining the completion.
        first.World.StopEconomyPersistForTests();

        var second = rig.Boot();
        Reconciled(second, "after-crash");
        Assert.Equal(expected, second.World.LedgerForTests.HoldingsOf(LedgerAccount.Inventory(a.ObjectId)).CountOf(Adena));
    }

    /// <summary>
    /// #955 root cause: the persist worker used to hand a batch every journal row buffered when it ran, not the rows
    /// that were buffered when the batch's bags were taken. A batch queued behind a slow commit then wrote a later
    /// move's row without that move's bag; dying before the later bag's own batch left the store and the journal
    /// apart (seen on the local server as two death drops in the journal with the items still in the saved bag).
    /// </summary>
    [Fact]
    public async Task A_commit_writes_only_the_journal_rows_of_the_bags_it_carries()
    {
        var rig = new Rig();
        var first = rig.Boot();
        var a = await first.EnterAsync("bnda", "BndA", items: [new StoredItem(Adena, 500, -1)], x: 0, y: 0, z: 0);
        var b = await first.EnterAsync("bndb", "BndB", items: [new StoredItem(Adena, 500, -1)], x: 20, y: 0, z: 0);
        var c = await first.EnterAsync("bndc", "BndC", items: [new StoredItem(Adena, 500, -1)], x: 40, y: 0, z: 0);
        first.World.StartEconomyPersistForTests();
        Reconciled(first, "start");
        long bSaved = rig.Store.Inner.FindByCharacterId(b.ObjectId)!.Items!.Where(r => r.ItemId == Adena).Sum(r => (long)r.Count);

        // W (C's drop) holds the worker inside its commit; X (A's drop) and then Y (B's drop) queue behind it.
        rig.Store.Gate = new ManualResetEventSlim(false);
        _ = first.World.ExecuteAsync(c.Session, new DropItemCommand(c.Slot(Adena), 5, 40, 0, 0), CancellationToken.None);
        first.World.StepSimulationRawForTests();
        Assert.True(rig.Store.Entered.Wait(TimeSpan.FromSeconds(10)));
        _ = first.World.ExecuteAsync(a.Session, new DropItemCommand(a.Slot(Adena), 10, 0, 0, 0), CancellationToken.None);
        first.World.StepSimulationRawForTests();
        _ = first.World.ExecuteAsync(b.Session, new DropItemCommand(b.Slot(Adena), 20, 20, 0, 0), CancellationToken.None);
        first.World.StepSimulationRawForTests();

        // W and X commit, Y (B's bag) dies inside the database, then the process dies.
        rig.Store.FailWhen = bags => bags.Any(bag => bag.CharacterId == b.ObjectId);
        var gate = rig.Store.Gate;
        rig.Store.Gate = null;
        gate.Set();
        Assert.True(SpinWait.SpinUntil(() => rig.Store.Failed > 0, TimeSpan.FromSeconds(10)), "B's batch never ran");
        Assert.True(SpinWait.SpinUntil(() => first.World.EconomyPersistPendingForTests == 0, TimeSpan.FromSeconds(10)));
        rig.Store.FailWhen = null;

        var drops = rig.Durable.Where(e => e.Transaction.Kind == LedgerTxKind.Drop).Select(e => e.Transaction.CausedBy).ToList();
        Assert.Contains(a.ObjectId, drops);
        Assert.DoesNotContain(b.ObjectId, drops);
        var second = rig.Boot();
        Reconciled(second, "after-crash");
        // B's drop never happened durably: its bag and the journal both still show the 500.
        Assert.Equal(bSaved, rig.Store.Inner.FindByCharacterId(b.ObjectId)!.Items!.Where(r => r.ItemId == Adena).Sum(r => (long)r.Count));
    }

    /// <summary>
    /// The other half of the same rule: a failed batch leaves its rows first in the buffer, so a batch queued behind it
    /// would write them without their bags. It is held back until the world has restored the failed bags.
    /// </summary>
    [Fact]
    public async Task A_batch_behind_a_failed_one_does_not_write_the_failed_rows()
    {
        var rig = new Rig();
        var first = rig.Boot();
        var a = await first.EnterAsync("hbka", "HbkA", items: [new StoredItem(Adena, 500, -1)], x: 0, y: 0, z: 0);
        var b = await first.EnterAsync("hbkb", "HbkB", items: [new StoredItem(Adena, 500, -1)], x: 20, y: 0, z: 0);
        var c = await first.EnterAsync("hbkc", "HbkC", items: [new StoredItem(Adena, 500, -1)], x: 40, y: 0, z: 0);
        first.World.StartEconomyPersistForTests();
        Reconciled(first, "start");

        rig.Store.Gate = new ManualResetEventSlim(false);
        _ = first.World.ExecuteAsync(c.Session, new DropItemCommand(c.Slot(Adena), 5, 40, 0, 0), CancellationToken.None);
        first.World.StepSimulationRawForTests();
        Assert.True(rig.Store.Entered.Wait(TimeSpan.FromSeconds(10)));
        _ = first.World.ExecuteAsync(a.Session, new DropItemCommand(a.Slot(Adena), 10, 0, 0, 0), CancellationToken.None);
        first.World.StepSimulationRawForTests();
        _ = first.World.ExecuteAsync(b.Session, new DropItemCommand(b.Slot(Adena), 20, 20, 0, 0), CancellationToken.None);
        first.World.StepSimulationRawForTests();

        // A's batch dies inside the database; B's runs right behind it, before the world restores A's bag.
        rig.Store.FailWhen = bags => bags.Any(bag => bag.CharacterId == a.ObjectId);
        var gate = rig.Store.Gate;
        rig.Store.Gate = null;
        gate.Set();
        Assert.True(SpinWait.SpinUntil(() => first.World.EconomyPersistPendingForTests == 0, TimeSpan.FromSeconds(10)));
        rig.Store.FailWhen = null;

        var drops = rig.Durable.Where(e => e.Transaction.Kind == LedgerTxKind.Drop).Select(e => e.Transaction.CausedBy).ToList();
        Assert.Equal([c.ObjectId], drops);
        var second = rig.Boot();
        Reconciled(second, "after-crash");

        // A world that lives on retries both once the failed bag is restored, rows and bags together.
        first.World.StepSimulationRawForTests();
        Reconciled(first, "retried");
        drops = rig.Durable.Where(e => e.Transaction.Kind == LedgerTxKind.Drop).Select(e => e.Transaction.CausedBy).ToList();
        Assert.Equal([c.ObjectId, a.ObjectId, b.ObjectId], drops);
    }

    [Fact]
    public async Task Divergences_fail_loudly_with_the_container_and_entity_id()
    {
        var rig = new Rig();
        var first = rig.Boot();
        first.World.StartEconomyPersistForTests();
        var (a, b, ground) = await PlayAsync(first);
        Reconciled(first, "clean");

        // A stack that vanished from the world without a ledger move.
        first.World.RemoveGroundForTest(ground);
        // A store row edited behind the ledger.
        var rows = rig.Store.Inner.FindByCharacterId(b.ObjectId)!.Items!
            .Select(row => row.ItemId == Adena ? row with { Count = row.Count + 500 } : row).ToList();
        Assert.True(rig.Store.Inner.TryReplaceItems("recb", b.ObjectId, rows));
        // An item written into a bag behind the ledger (a dupe through the store).
        var aRows = rig.Store.Inner.FindByCharacterId(a.ObjectId)!.Items!.Append(new StoredItem(Harvester, 1, -1)).ToList();
        Assert.True(rig.Store.Inner.TryReplaceItems("reca", a.ObjectId, aRows));

        var report = first.World.ReconcileEconomy("tampered");
        Assert.False(report.Ok);
        Assert.Contains(report.Violations, v => v.Code == EconomyReconcileViolation.GroundWithoutEntity && v.OwnerId == ground);
        Assert.Contains(report.Violations, v => v.Code == EconomyReconcileViolation.StoreDivergence && v.OwnerId == b.ObjectId && v.Container == "inventory");
        Assert.Contains(report.Violations, v => v.Code == EconomyReconcileViolation.StoreDivergence && v.OwnerId == a.ObjectId && v.Detail.Contains("5125", StringComparison.Ordinal));
        var thrown = Assert.Throws<EconomyReconcileException>(report.ThrowIfViolations);
        Assert.Contains($"ground:{ground}", thrown.Message, StringComparison.Ordinal);
        Assert.Contains($"inventory:{b.ObjectId}", thrown.Message, StringComparison.Ordinal);
        Assert.Contains($"inventory:{a.ObjectId}", thrown.Message, StringComparison.Ordinal);
    }
}
