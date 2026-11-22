using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Protocol;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Player trades on the auditable economy ledger (step 11b): a confirmed exchange is one <see cref="LedgerTxKind.TradeSettle"/>
/// between the two bags, pieces move by instance id, and nothing is recorded when the trade does not happen.
/// </summary>
public sealed class TradeLedgerTests
{
    const int Adena = 57;

    [Fact]
    public async Task A_confirmed_exchange_is_one_settlement_between_the_two_bags()
    {
        var kit = new NetTestKit();
        var (seller, sellerEntity) = await kit.EnterAsync(classId: 0);
        var (buyer, buyerEntity) = await kit.EnterAsync(classId: 1);
        var piece = sellerEntity.Inventory!.Items.First(item => Offerable(item) && !item.Template.Stackable);
        Assert.Null(await kit.World.TryGiveItemAsync(buyer, Adena, 500, default));
        var stack = buyerEntity.Inventory!.Items.Single(item => item.ItemId == Adena);
        int stackItem = stack.ItemId;
        long sellerStackBefore = sellerEntity.Inventory.CountOf(stackItem);

        await OpenAsync(kit, seller, buyer, sellerEntity);
        Assert.Null(await kit.World.TryTradeAddItemAsync(
            seller, new TradeAddItemRequestMsg { InventoryObjectId = piece.ObjectId, Count = 1 }, default));
        Assert.Null(await kit.World.TryTradeAddItemAsync(
            buyer, new TradeAddItemRequestMsg { InventoryObjectId = stack.ObjectId, Count = 200 }, default));
        await ConfirmAsync(kit, seller, buyer);

        var sellerBag = LedgerAccount.Inventory(sellerEntity.ObjectId);
        var buyerBag = LedgerAccount.Inventory(buyerEntity.ObjectId);
        var kinds = kit.Ledger.Entries.Select(entry => entry.Transaction.Kind).ToList();
        Assert.Equal([LedgerTxKind.GmGrant, LedgerTxKind.Adjustment, LedgerTxKind.TradeSettle], kinds);
        var settle = kit.Ledger.Entries[^1].Transaction;
        Assert.Equal((sellerEntity.ObjectId, buyerEntity.ObjectId), (settle.CausedBy, settle.Reference));
        Assert.Equal(
            [
                (sellerBag, piece.ItemId, -1L, $"{piece.ObjectId}"),
                (buyerBag, piece.ItemId, 1L, $"{piece.ObjectId}"),
                (buyerBag, Adena, -200L, ""),
                (sellerBag, Adena, 200L, "")
            ],
            settle.Postings.Select(posting =>
                (posting.Account, posting.ItemId, posting.Amount, string.Join(",", posting.InstanceIds))));

        var ledger = kit.World.LedgerForTests;
        Assert.Equal(buyerBag, ledger.OwnerOf(piece.ObjectId));
        Assert.Equal(sellerStackBefore + 200, ledger.HoldingsOf(sellerBag).CountOf(stackItem));
        Assert.Contains(kit.Ledger.Entries[0].Transaction.Postings,
            posting => posting.Account == buyerBag && posting.ItemId == Adena && posting.Amount == 500);
        Assert.Equal(sellerEntity.Inventory.CountOf(stackItem), ledger.HoldingsOf(sellerBag).CountOf(stackItem));
        Assert.Equal(buyerEntity.Inventory.CountOf(stackItem), ledger.HoldingsOf(buyerBag).CountOf(stackItem));
    }

    [Fact]
    public async Task An_empty_or_cancelled_trade_records_nothing()
    {
        var kit = new NetTestKit();
        var (seller, sellerEntity) = await kit.EnterAsync(classId: 0);
        var (buyer, _) = await kit.EnterAsync(classId: 1);

        await OpenAsync(kit, seller, buyer, sellerEntity);
        await ConfirmAsync(kit, seller, buyer);
        Assert.Empty(kit.Ledger.Entries);

        var piece = sellerEntity.Inventory!.Items.First(item => Offerable(item) && !item.Template.Stackable);
        await OpenAsync(kit, seller, buyer, sellerEntity);
        Assert.Null(await kit.World.TryTradeAddItemAsync(
            seller, new TradeAddItemRequestMsg { InventoryObjectId = piece.ObjectId, Count = 1 }, default));
        Assert.Null(await kit.World.TryTradeDoneAsync(seller, new TradeDoneRequestMsg { Response = 0 }, default));
        Assert.Empty(kit.Ledger.Entries);
    }

    static bool Offerable(ItemInstance item) => !item.IsEquipped && item.Template.CanDropTradeStore && item.Count >= 1;

    static async Task OpenAsync(NetTestKit kit, ZoneSession seller, ZoneSession buyer, ZoneEntity sellerEntity)
    {
        Assert.Null(await kit.World.TryTradeRequestAsync(
            seller, new TradeRequestMsg { TargetObjectId = buyer.ObjectId }, default));
        Assert.Null(await kit.World.TryTradeAnswerAsync(
            buyer, new TradeAnswerRequestMsg { RequestorObjectId = sellerEntity.ObjectId, Response = 1 }, default));
    }

    static async Task ConfirmAsync(NetTestKit kit, ZoneSession seller, ZoneSession buyer)
    {
        Assert.Null(await kit.World.TryTradeDoneAsync(seller, new TradeDoneRequestMsg { Response = 1 }, default));
        Assert.Null(await kit.World.TryTradeDoneAsync(buyer, new TradeDoneRequestMsg { Response = 1 }, default));
    }
}
