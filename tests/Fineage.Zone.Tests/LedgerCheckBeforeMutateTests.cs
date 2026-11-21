using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Protocol;
using Fineage.Zone.World;
using Xunit;
using static Fineage.Zone.Tests.NetShopTests;

namespace Fineage.Zone.Tests;

/// <summary>P0-4: ledger <see cref="LedgerMove.CheckCommit"/> runs before inventories and ground stacks change.</summary>
public sealed class LedgerCheckBeforeMutateTests
{
    static readonly LedgerAccount Shop = LedgerAccount.Of(SystemAccount.NpcShop);


    [Fact]
    public void Trade_settlement_calls_check_before_transferring_items()
    {
        var journal = new MemoryLedgerJournal();
        var ledger = new EconomyLedger(journal, 100);
        const int Alice = 1001;
        const int Bob = 1002;
        const int Sword = 100;
        const int Inst = 55_001;
        var alice = LedgerAccount.Inventory(Alice);
        var bob = LedgerAccount.Inventory(Bob);
        Assert.True(ledger.Submit(new LedgerTransaction(
            new LedgerTxId(1, 0),
            LedgerTxKind.Adjustment,
            0,
            0,
            0,
            [new LedgerPosting(Shop, Sword, -1), new LedgerPosting(alice, Sword, 1, [Inst])])).Ok);

        var move = new LedgerMove(ledger, () => new LedgerTxId(2, 0), LedgerTxKind.TradeSettle, 0, Alice, Bob);
        move.Observe(alice, new LedgerHoldings().AddPiece(Sword, Inst));
        move.Observe(bob, new LedgerHoldings());
        move.Post(alice, Sword, -1, [Inst]);
        move.Post(bob, Sword, 1, [Inst]);
        move.Post(bob, Sword, 1, [Inst]);
        Assert.NotEqual(LedgerRejection.None, move.CheckCommit());
    }

    [Fact]
    public async Task Shop_buy_restores_the_bag_when_the_ledger_rejects()
    {
        var clock = new MutableClock();
        WorldBench bench = Flat(clock, Kit((Adena, 1000)));
        BenchPlayer buyer = await bench.EnterAsync("buyer", "Buyer");
        int mintId = bench.World.NextItemObjectIdForTests;
        ClaimInstance(bench.World.LedgerForTests, bench.Ledger, mintId, Blade, LedgerAccount.Inventory(999_999));

        var before = CharacterInventory.Rows(buyer.Player.Inventory!);

        await buyer.BypassAsync(MerchantObjectId, "Buy 1");
        Assert.Null(await buyer.Buy(MerchantObjectId, 1, (Blade, 1)));

        Assert.Equal(before, CharacterInventory.Rows(buyer.Player.Inventory!));
        Assert.DoesNotContain(bench.Ledger.Entries, e => e.Transaction.Kind == LedgerTxKind.ShopBuy);
        Assert.Equal(ShopProtocol.NotTradable, LastSystemMessage(bench.Rec).Id);
    }


    static void ClaimInstance(
        EconomyLedger ledger,
        MemoryLedgerJournal journal,
        int instanceId,
        int itemId,
        LedgerAccount holder)
    {
        long tick = journal.Entries.Count > 0 ? journal.Entries[^1].Transaction.Id.Tick + 1 : 1;
        Assert.True(ledger.Submit(new LedgerTransaction(
            new LedgerTxId(tick, 0),
            LedgerTxKind.Adjustment,
            0,
            0,
            0,
            [new LedgerPosting(Shop, itemId, -1), new LedgerPosting(holder, itemId, 1, [instanceId])])).Ok);
    }
}