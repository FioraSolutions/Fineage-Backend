using Fineage.Game.Economy;
using Fineage.Zone.World;
using Xunit;
using static Fineage.Zone.Tests.NetShopTests;

namespace Fineage.Zone.Tests;

/// <summary>
/// Shop moves on the auditable economy ledger (step 11a): each buy, sale and multisell is journaled with the NPC shop
/// as counterparty, castle tax goes on to the treasuries it was credited to, and the journal agrees with the bag and
/// the treasuries after every move.
/// </summary>
public sealed class ShopLedgerTests
{
    static readonly LedgerAccount Shop = LedgerAccount.Of(SystemAccount.NpcShop);
    static readonly LedgerAccount Untracked = LedgerAccount.Of(SystemAccount.Untracked);
    const ulong ShoppingSeed = 20_261_121;

    [Fact]
    public async Task Buy_sell_and_multisell_are_journaled_against_the_npc_shop_and_match_the_bag()
    {
        var clock = new MutableClock();
        WorldBench bench = Flat(clock, Kit((Adena, 1000), (Sword, 1)));
        BenchPlayer buyer = await bench.EnterAsync("buyer", "Buyer");
        var bag = LedgerAccount.Inventory(buyer.ObjectId);
        int sword = buyer.Slot(Sword);
        long tick = clock.Now.ToUnixTimeMilliseconds() / ZoneSimulationSettings.StepMilliseconds;

        await buyer.BypassAsync(MerchantObjectId, "Buy 1");
        Assert.Null(await buyer.Buy(MerchantObjectId, 1, (Potion, 2)));

        // The bag predates the ledger: the first move opens it from the untracked account, then records the buy.
        var opening = bench.Ledger.Entries[0].Transaction;
        Assert.Equal((new LedgerTxId(tick, 0), LedgerTxKind.Adjustment), (opening.Id, opening.Kind));
        Assert.Equal(
            Plain((bag, Sword, 1, $"{sword}"), (bag, Adena, 1000, ""), (Untracked, Adena, -1000, ""), (Untracked, Sword, -1, "")),
            opening.Postings.Select(Plain));
        var buy = bench.Ledger.Entries[1].Transaction;
        Assert.Equal((new LedgerTxId(tick, 1), LedgerTxKind.ShopBuy, buyer.ObjectId, MerchantNpcId),
            (buy.Id, buy.Kind, buy.CausedBy, buy.Reference));
        Assert.Equal(
            Plain((bag, Potion, 2, ""), (Shop, Potion, -2, ""), (bag, Adena, -80, ""), (Shop, Adena, 80, "")),
            buy.Postings.Select(Plain));

        await buyer.BypassAsync(MerchantObjectId, "Sell");
        Assert.Null(await buyer.Sell(MerchantObjectId, (sword, 1)));
        var sell = bench.Ledger.Entries[2].Transaction;
        Assert.Equal(LedgerTxKind.ShopSell, sell.Kind);
        Assert.Equal(
            Plain((bag, Adena, 500, ""), (Shop, Adena, -500, ""), (bag, Sword, -1, $"{sword}"), (Shop, Sword, 1, "")),
            sell.Postings.Select(Plain));

        await buyer.BypassAsync(MerchantObjectId, "Multisell 2");
        Assert.Null(await buyer.MultiSell(MerchantObjectId, 2, LastMultiSell(bench.Rec).Entries[0].EntryId, 1));
        int blade = buyer.Slot(Blade);
        var exchange = bench.Ledger.Entries[3].Transaction;
        Assert.Equal(LedgerTxKind.ShopExchange, exchange.Kind);
        Assert.Equal(
            Plain((bag, Blade, 1, $"{blade}"), (Shop, Blade, -1, ""), (bag, Potion, -1, ""), (Shop, Potion, 1, ""),
                (bag, Adena, -100, ""), (Shop, Adena, 100, "")),
            exchange.Postings.Select(Plain));

        Assert.Equal(4, bench.Ledger.Entries.Count);
        Assert.Null(LedgerChain.FirstBroken(bench.Ledger.Entries));
        var journal = bench.World.LedgerForTests.HoldingsOf(bag);
        Assert.Equal(1320, journal.CountOf(Adena));
        Assert.Equal(1, journal.CountOf(Potion));
        Assert.Equal(bag, bench.World.LedgerForTests.OwnerOf(blade));
        Assert.Null(bench.World.LedgerForTests.OwnerOf(sword));
    }

    [Fact]
    public async Task A_refused_purchase_records_no_move()
    {
        var clock = new MutableClock();
        WorldBench bench = Flat(clock, Kit((Adena, 50)));
        BenchPlayer buyer = await bench.EnterAsync("poor", "Poor");

        await buyer.BypassAsync(MerchantObjectId, "Buy 1");
        Assert.Null(await buyer.Buy(MerchantObjectId, 1, (Potion, 2)));

        Assert.Equal(Fineage.Protocol.ShopProtocol.NotEnoughAdena, LastSystemMessage(bench.Rec).Id);
        Assert.Equal([LedgerTxKind.Adjustment], bench.Ledger.Entries.Select(entry => entry.Transaction.Kind));
    }

    [Fact]
    public async Task Castle_tax_leaves_the_shop_for_each_treasury_it_was_credited_to()
    {
        var clock = new MutableClock();
        WorldBench bench = Flat(clock, Kit((Adena, 1000)), mapId: "giran");
        BenchPlayer actor = await bench.EnterAsync("taxed", "Taxed");
        var bag = LedgerAccount.Inventory(actor.ObjectId);
        var giran = bench.World.FindCastle(3)!;
        var aden = bench.World.FindCastle(5)!;
        var giranTreasury = LedgerAccount.Treasury(3);
        var adenTreasury = LedgerAccount.Treasury(5);
        giran.OwnerClanId = 8;
        giran.TaxPercent = 10;

        await actor.BypassAsync(MerchantObjectId, "Buy 1");
        Assert.Null(await actor.Buy(MerchantObjectId, 1, (Potion, 2)));
        Assert.Equal(
            Plain((bag, Potion, 2, ""), (Shop, Potion, -2, ""), (bag, Adena, -96, ""), (Shop, Adena, 88, ""),
                (giranTreasury, Adena, 8, "")),
            bench.Ledger.Entries[^1].Transaction.Postings.Select(Plain));

        // Treasury changed outside the ledger (a lord's deposit): the next move brings it in line first.
        giran.Treasury += 1_000;
        aden.OwnerClanId = 9;
        aden.TaxPercent = 50;
        Assert.Null(await actor.Buy(MerchantObjectId, 1, (Potion, 2)));
        var deposit = bench.Ledger.Entries[^2].Transaction;
        Assert.Equal(LedgerTxKind.Adjustment, deposit.Kind);
        Assert.Equal(Plain((giranTreasury, Adena, 1_000, ""), (Untracked, Adena, -1_000, "")), deposit.Postings.Select(Plain));
        Assert.Equal(
            Plain((bag, Potion, 2, ""), (Shop, Potion, -2, ""), (bag, Adena, -96, ""), (Shop, Adena, 88, ""),
                (adenTreasury, Adena, 4, ""), (giranTreasury, Adena, 4, "")),
            bench.Ledger.Entries[^1].Transaction.Postings.Select(Plain));

        Assert.Equal(giran.Treasury, bench.World.LedgerForTests.HoldingsOf(giranTreasury).CountOf(Adena));
        Assert.Equal(aden.Treasury, bench.World.LedgerForTests.HoldingsOf(adenTreasury).CountOf(Adena));
    }

    [Fact]
    public async Task Replaying_the_command_log_reproduces_the_journal_byte_for_byte()
    {
        var (recorded, log) = await RecordShoppingAsync();
        Assert.Equal(
            [LedgerTxKind.Adjustment, LedgerTxKind.ShopBuy, LedgerTxKind.ShopBuy, LedgerTxKind.ShopSell, LedgerTxKind.ShopExchange],
            recorded.Ledger.Entries.Select(entry => entry.Transaction.Kind));

        var clock = new MutableClock();
        var replay = Flat(clock, Kit((Adena, 1000), (Sword, 1)), seed: ShoppingSeed);
        await replay.EnterAsync("buyer", "Buyer");
        await replay.World.ReplayAsync(log, ms => clock.Now = DateTimeOffset.FromUnixTimeMilliseconds(ms), CancellationToken.None);

        Assert.Equal(recorded.Ledger.Entries.Select(Encoded), replay.Ledger.Entries.Select(Encoded));
        Assert.Equal(recorded.World.LedgerForTests.Head, replay.World.LedgerForTests.Head);
    }

    static async Task<(WorldBench Bench, Fineage.Zone.Simulation.SimulationLog Log)> RecordShoppingAsync()
    {
        var clock = new MutableClock();
        var bench = Flat(clock, Kit((Adena, 1000), (Sword, 1)), seed: ShoppingSeed);
        var buyer = await bench.EnterAsync("buyer", "Buyer");
        int sword = buyer.Slot(Sword);
        var log = bench.World.RecordSimulation();
        ZoneCommand[][] script =
        [
            [new BypassCommand(MerchantObjectId, "Buy 1"), new BuyCommand(MerchantObjectId, 1, [new(Potion, 2)])],
            [new BuyCommand(MerchantObjectId, 1, [new(Potion, 1)]), new BypassCommand(MerchantObjectId, "Sell")],
            [new SellCommand(MerchantObjectId, [new(sword, 1)]), new BypassCommand(MerchantObjectId, "Multisell 2")],
            [new MultiSellCommand(MerchantObjectId, 2, 200_000, 1)]
        ];
        foreach (var step in script)
        {
            var replies = step.Select(command => bench.World.ExecuteAsync(buyer.Session, command, CancellationToken.None)).ToList();
            clock.Now = clock.Now.AddMilliseconds(250);
            bench.World.StepSimulation();
            Assert.All(await Task.WhenAll(replies), Assert.Null);
        }

        return (bench, log);
    }

    static string Encoded(LedgerEntry entry) =>
        $"{entry.Position} {entry.Hash} {Convert.ToHexString(LedgerChain.Encode(entry.Transaction))}";

    static (LedgerAccount, int, long, string) Plain(LedgerPosting posting) =>
        (posting.Account, posting.ItemId, posting.Amount, string.Join(",", posting.InstanceIds));

    static IEnumerable<(LedgerAccount, int, long, string)> Plain(params (LedgerAccount, int, long, string)[] postings) =>
        postings;
}
