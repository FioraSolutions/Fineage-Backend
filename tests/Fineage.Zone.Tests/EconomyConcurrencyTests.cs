using Fineage.Accounts;
using Fineage.Game.Economy;
using Fineage.Game.Services;
using Fineage.Protocol;
using Fineage.Zone.World;
using Xunit;
using Rig = Fineage.Zone.Tests.EconomyReconcileTests.Rig;

namespace Fineage.Zone.Tests;

/// <summary>
/// #955: two or more clients racing for the same goods, each from its own thread, through the command ingress of a
/// running simulation loop with the persist worker committing behind it. Racing commands land in the same step or in
/// consecutive ones; these prove the loser is refused on the state the winner left (no dupe, no loss) and that the
/// ledger, the store and the live world reconcile after the race, after the real shutdown path and after a restart.
/// </summary>
public sealed class EconomyConcurrencyTests
{
    const int KeeperObjectId = 61001;
    const int Adena = 57;
    const int Scroll = 736;
    const int Harvester = 5125;

    /// <summary>Starts every call on its own thread at once.</summary>
    static async Task<string?[]> Race(params Func<Task<string?>>[] calls)
    {
        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int ready = 0;
        var running = calls.Select(call => Task.Run(async () =>
        {
            Interlocked.Increment(ref ready);
            await go.Task;
            return await call();
        })).ToArray();
        SpinWait.SpinUntil(() => Volatile.Read(ref ready) == calls.Length, TimeSpan.FromSeconds(5));
        go.SetResult();
        return await Task.WhenAll(running).WaitAsync(TimeSpan.FromSeconds(60));
    }

    static WorldBench Boot(Rig rig) => rig.Boot();

    /// <summary>
    /// The production shape: the simulation loop and the persist worker run, handlers submit commands from their own
    /// threads. Disposing stops the loop through its real shutdown path (save online players, stop the worker, flush
    /// the journal) and then reconciles.
    /// </summary>
    sealed class Live : IAsyncDisposable
    {
        readonly WorldBench _bench;
        readonly CancellationTokenSource _stop = new();

        public Live(WorldBench bench)
        {
            _bench = bench;
            bench.World.StartSimulation(_stop.Token);
        }

        public Task<string?> Run(BenchPlayer player, ZoneCommand command) =>
            _bench.World.ExecuteAsync(player.Session, command, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            await _bench.World.WaitSimulationAsync().WaitAsync(TimeSpan.FromSeconds(60));
            var report = _bench.World.ReconcileEconomy("after-shutdown");
            report.ThrowIfViolations();
        }
    }

    static LedgerHoldings Ledger(WorldBench bench, LedgerAccount account) => bench.World.LedgerForTests.HoldingsOf(account);

    static Func<Task<string?>>[] Each<T>(IEnumerable<T> items, Func<T, Task<string?>> call) =>
        items.Select(item => (Func<Task<string?>>)(() => call(item))).ToArray();

    static Func<Task<string?>>[] Times(int n, Func<Task<string?>> call) => Enumerable.Repeat(call, n).ToArray();

    static int[] GroundStacks(WorldBench bench) => bench.World.LedgerForTests.NonEmptyAccounts()
        .Where(x => x.Account.Container == LedgerContainer.Ground).Select(x => x.Account.OwnerId).ToArray();

    /// <summary>A reconnect through the running loop (the bench's enter reads the entity off the turn).</summary>
    static async Task<BenchPlayer> ReenterAsync(WorldBench bench, BenchPlayer left)
    {
        var ticket = left.Ticket;
        var session = new ZoneSession();
        session.Bind(ticket.CharacterId, ticket.Account, ticket.CharacterName, ticket.ClassId, ticket.Race, ticket.Sex);
        Assert.True(await bench.World.TryEnterAsync(session, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30)));
        ZoneEntity? player = null;
        bench.World.RunStepForTest(() => Assert.True(bench.World.TryGetLivePlayer(session, out player)));
        return new BenchPlayer(bench.World, left.Store, session, player!, ticket);
    }

    [Fact]
    public async Task Pickup_races_have_one_winner_per_stack()
    {
        var bench = Boot(new Rig());
        var a = await bench.EnterAsync("pka", "PkA",
            items: [new StoredItem(Adena, 500, -1), new StoredItem(Harvester, 1, -1)], x: 0, y: 0, z: 0);
        var pickers = new List<BenchPlayer>();
        for (int i = 0; i < 4; i++)
            pickers.Add(await bench.EnterAsync($"pk{i}", $"Pk{i}", items: [new StoredItem(Adena, 1, -1)], x: 10 + i, y: 0, z: 0));
        int piece = a.Slot(Harvester);
        int adena = a.Slot(Adena);
        var winners = new List<int>();

        await using (var live = new Live(bench))
        {
            for (int round = 0; round < 8; round++)
            {
                bool dropPiece = round % 2 == 0;
                Assert.Null(dropPiece
                    ? await live.Run(round == 0 ? a : pickers[winners[^1]], new DropItemCommand(piece, 1, 10, 0, 0))
                    : await live.Run(a, new DropItemCommand(adena, 20, 10, 0, 0)));
                int ground = dropPiece ? piece : GroundStacks(bench).Single(id => id != piece);
                var results = await Race(Each(pickers, p => live.Run(p, new PickupCommand(ground))));
                Assert.Single(results, result => result == null);
                if (dropPiece)
                    winners.Add(Array.FindIndex(results, result => result == null));
                Assert.Empty(GroundStacks(bench));
            }
        }

        Assert.Single(pickers, p => p.Inventory.Find(piece) != null);
        Assert.Equal(4 + 4 * 20, pickers.Sum(p => p.Inventory.CountOf(Adena)));
        Assert.Equal(500 - 4 * 20, a.Inventory.CountOf(Adena));
    }

    [Fact]
    public async Task Warehouse_deposit_and_withdraw_races_move_each_unit_once()
    {
        var bench = Boot(new Rig());
        var a = await bench.EnterAsync("wha", "WhA",
            items: [new StoredItem(Adena, 5000, -1), new StoredItem(Scroll, 10, -1)], x: 0, y: 0, z: 0);
        var store = LedgerAccount.Warehouse(a.ObjectId);
        long adena = a.Inventory.CountOf(Adena);
        int scrolls = a.Slot(Scroll);
        int deposited, withdrawn;

        await using (var live = new Live(bench))
        {
            Assert.Null(await live.Run(a, new BypassCommand(KeeperObjectId, "DepositP")));
            var deposits = await Race(Times(8, () => live.Run(a, new WarehouseDepositCommand(KeeperObjectId, [new WarehouseLine(scrolls, 3)]))));
            deposited = deposits.Count(result => result == null);

            Assert.Null(await live.Run(a, new BypassCommand(KeeperObjectId, "WithdrawP")));
            int stored = a.Player.Warehouse!.Items.Single(stack => stack.ItemId == Scroll).ObjectId;
            var withdrawals = await Race(Times(6, () => live.Run(a, new WarehouseWithdrawCommand(KeeperObjectId, [new WarehouseLine(stored, 4)]))));
            withdrawn = withdrawals.Count(result => result == null);
        }

        // Mobius answers an over-count deposit or withdrawal with a system message, not an error: count what moved.
        var ledger = bench.World.LedgerForTests;
        long fees = ledger.HoldingsOf(LedgerAccount.Of(SystemAccount.Fees)).CountOf(Adena);
        long kept = ledger.HoldingsOf(store).CountOf(Scroll);
        Assert.Equal(10, a.Inventory.CountOf(Scroll) + kept);
        Assert.Equal(adena, a.Inventory.CountOf(Adena) + fees);
        Assert.Equal(a.Player.Warehouse!.Items.Where(stack => stack.ItemId == Scroll).Sum(stack => stack.Count), kept);
        Assert.True(fees % WarehouseRules.DepositFee(1) == 0 && fees / WarehouseRules.DepositFee(1) <= 3,
            $"{fees} adena of fees for at most three 3-scroll deposits of a 10-scroll stack ({deposited} answered ok)");
        Assert.True(kept >= 1 && kept <= 9, $"warehouse keeps {kept} scrolls after the withdraw race ({withdrawn} answered ok)");
    }

    [Fact]
    public async Task A_piece_offered_in_a_trade_and_dropped_at_once_ends_in_exactly_one_place()
    {
        var bench = Boot(new Rig());
        var a = await bench.EnterAsync("tda", "TdA", items: [new StoredItem(Adena, 100, -1)], x: 0, y: 0, z: 0);
        var b = await bench.EnterAsync("tdb", "TdB", items: [new StoredItem(Adena, 100, -1)], x: 20, y: 0, z: 0);
        var outcomes = new List<string>();

        await using (var live = new Live(bench))
        {
            for (int round = 0; round < 12; round++)
            {
                Assert.Null(await bench.World.TryGiveItemAsync(a.Session, Harvester, 1, CancellationToken.None));
                int piece = a.Inventory.Items.Single(item => item.ItemId == Harvester).ObjectId;
                Assert.Null(await live.Run(a, new TradeRequestCommand(new TradeRequestMsg { TargetObjectId = b.ObjectId })));
                Assert.Null(await live.Run(b, new TradeAnswerCommand(
                    new TradeAnswerRequestMsg { RequestorObjectId = a.ObjectId, Response = 1 })));
                Assert.Null(await live.Run(a, new TradeAddItemCommand(
                    new TradeAddItemRequestMsg { InventoryObjectId = piece, Count = 1 })));

                Func<Task<string?>> drop = () => live.Run(a, new DropItemCommand(piece, 1, 10, 0, 0));
                Func<Task<string?>> settle = async () =>
                    await live.Run(a, new TradeDoneCommand(new TradeDoneRequestMsg { Response = 1 }))
                    ?? await live.Run(b, new TradeDoneCommand(new TradeDoneRequestMsg { Response = 1 }));
                var results = round % 2 == 0 ? await Race(drop, settle) : await Race(settle, drop);

                // Whatever is left of the trade is cancelled before the next round.
                await live.Run(a, new TradeDoneCommand(new TradeDoneRequestMsg { Response = 0 }));
                var report = bench.World.ReconcileEconomyFlushed($"trade-drop-{round}");
                report.ThrowIfViolations();
                bool onGround = GroundStacks(bench).Contains(piece);
                var owner = bench.World.LedgerForTests.OwnerOf(piece);
                Assert.True(owner == (onGround ? LedgerAccount.Ground(piece) : LedgerAccount.Inventory(b.ObjectId)),
                    $"round {round}: piece {piece} owned by {owner}, on ground={onGround} ({results[0]}, {results[1]})");
                outcomes.Add(onGround ? "drop" : "trade");
                if (onGround)
                    Assert.Null(await live.Run(b, new PickupCommand(piece)));
            }
        }

        Assert.Equal(12, b.Inventory.Items.Count(item => item.ItemId == Harvester));
        Assert.DoesNotContain(a.Inventory.Items, item => item.ItemId == Harvester);
    }

    [Fact]
    public async Task Buyers_racing_for_one_private_store_listing_pay_once()
    {
        var rig = new Rig();
        var bench = Boot(rig);
        var seller = await bench.EnterAsync("pss", "PsS",
            items: [new StoredItem(Adena, 10, -1), new StoredItem(Harvester, 1, -1), new StoredItem(Scroll, 5, -1)],
            x: 0, y: 0, z: 0);
        var buyers = new List<BenchPlayer>();
        for (int i = 0; i < 3; i++)
            buyers.Add(await bench.EnterAsync($"psb{i}", $"PsB{i}", items: [new StoredItem(Adena, 1000, -1)], x: 15 + i, y: 0, z: 0));
        int piece = seller.Slot(Harvester);
        int scrolls = seller.Slot(Scroll);
        int pieceSales, scrollSales;

        await using (var live = new Live(bench))
        {
            Assert.Null(await live.Run(seller, new SetPrivateStoreSellCommand(new SetPrivateStoreSellMsg
            {
                Message = "race",
                Items =
                [
                    new PrivateStoreSellRowMsg { InventoryObjectId = piece, Count = 1, Price = 300 },
                    new PrivateStoreSellRowMsg { InventoryObjectId = scrolls, Count = 5, Price = 10 }
                ]
            })));
            foreach (var buyer in buyers)
                Assert.Null(await live.Run(buyer, new PrivateStoreListCommand(
                    new PrivateStoreListRequestMsg { StoreObjectId = seller.ObjectId })));

            var pieceRace = await Race(Each(buyers, buyer => live.Run(buyer, new PrivateStoreBuyCommand(new PrivateStoreBuyMsg
            {
                StoreObjectId = seller.ObjectId,
                Items = [new PrivateStoreSellRowMsg { InventoryObjectId = piece, Count = 1, Price = 300 }]
            }))));
            pieceSales = pieceRace.Count(result => result == null);
            var scrollRace = await Race(Each(buyers, buyer => live.Run(buyer, new PrivateStoreBuyCommand(new PrivateStoreBuyMsg
            {
                StoreObjectId = seller.ObjectId,
                Items = [new PrivateStoreSellRowMsg { InventoryObjectId = scrolls, Count = 2, Price = 10 }]
            }))));
            scrollSales = scrollRace.Count(result => result == null);
        }

        Assert.Equal(1, pieceSales);
        Assert.Equal(2, scrollSales);
        Assert.Single(buyers, buyer => buyer.Inventory.Find(piece) != null);
        Assert.Equal(1, seller.Inventory.CountOf(Scroll));
        Assert.Equal(4, buyers.Sum(buyer => buyer.Inventory.CountOf(Scroll)));
        Assert.Equal(10 + 300 + 2 * 2 * 10, seller.Inventory.CountOf(Adena));
        Assert.Equal(3000 - 300 - 2 * 2 * 10, buyers.Sum(buyer => buyer.Inventory.CountOf(Adena)));
        Assert.Single(rig.Durable, entry =>
            entry.Transaction.Kind == LedgerTxKind.StoreSale && entry.Transaction.Postings.Any(p => p.InstanceIds.Contains(piece)));
    }

    [Fact]
    public async Task Disconnecting_while_its_economy_commit_is_in_flight_loses_and_duplicates_nothing()
    {
        var rig = new Rig();
        var bench = Boot(rig);
        var other = await bench.EnterAsync("dco", "DcO", items: [new StoredItem(Adena, 10, -1)], x: 20, y: 0, z: 0);
        var a = await bench.EnterAsync("dca", "DcA",
            items: [new StoredItem(Adena, 5000, -1), new StoredItem(Scroll, 40, -1)], x: 0, y: 0, z: 0);
        int characterId = a.ObjectId;

        await using (var live = new Live(bench))
        {
            for (int round = 0; round < 10; round++)
            {
                Assert.Null(await live.Run(a, new BypassCommand(KeeperObjectId, "DepositP")));
                int scrolls = a.Slot(Scroll);
                int adena = a.Slot(Adena);
                var player = a;
                var results = await Race(
                    () => live.Run(player, new WarehouseDepositCommand(KeeperObjectId, [new WarehouseLine(scrolls, 1)])),
                    () => live.Run(player, new DropItemCommand(adena, 7, 10, 0, 0)),
                    () => bench.World.TryGiveItemAsync(player.Session, Adena, 11, CancellationToken.None),
                    async () =>
                    {
                        await bench.World.LeaveAsync(player.Session, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));
                        return null;
                    });
                Assert.False(a.Session.InWorld);
                foreach (int ground in GroundStacks(bench))
                    Assert.Null(await live.Run(other, new PickupCommand(ground)));
                bench.World.ReconcileEconomyFlushed($"disconnect-{round} ({string.Join(",", results)})").ThrowIfViolations();
                a = await ReenterAsync(bench, a);
            }

            await bench.World.LeaveAsync(a.Session, CancellationToken.None);
        }

        var bag = bench.World.LedgerForTests.HoldingsOf(LedgerAccount.Inventory(characterId));
        var warehouse = bench.World.LedgerForTests.HoldingsOf(LedgerAccount.Warehouse(characterId));
        Assert.Equal(40, bag.CountOf(Scroll) + warehouse.CountOf(Scroll));

        var second = rig.Boot();
        EconomyReconcileTests.Reconciled(second, "after-restart");
        Assert.True(second.World.LedgerForTests.HoldingsOf(LedgerAccount.Inventory(characterId)).SameAs(bag));
        Assert.True(second.World.LedgerForTests.HoldingsOf(LedgerAccount.Warehouse(characterId)).SameAs(warehouse));
    }
}
