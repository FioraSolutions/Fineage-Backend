using Fineage.Game;
using Fineage.Protocol;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Trade lifecycle over Net: ask, accept, offer, double confirm, exchange. Replaces the V2 wire fixture
/// <c>ZoneTradeSharedTests</c>; every step asserts the bridge fanout the Net client receives.
/// </summary>
public sealed class NetTradeTests
{
    static (NetTestKit Kit, NetBridgeRecorder Rec) World()
    {
        var kit = new NetTestKit();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return (kit, rec);
    }

    static object? Last(NetBridgeRecorder rec, string method) =>
        rec.Of(method).Count == 0 ? null : rec.Of(method).Last()[1];

    /// <summary>First carried, unequipped, tradable item — the only thing a trade may offer.</summary>
    static ItemInstance Tradable(ZoneEntity player) =>
        player.Inventory!.Items.First(item => !item.IsEquipped && item.Template.CanDropTradeStore && item.Count >= 1);

    [Fact]
    public async Task Two_players_request_trade_and_exchange_an_item()
    {
        var (kit, rec) = World();
        var (seller, sellerEntity) = await kit.EnterAsync(classId: 0);
        var (buyer, buyerEntity) = await kit.EnterAsync(classId: 1);

        var offered = Tradable(sellerEntity);
        Assert.Null(await kit.World.TryTradeRequestAsync(
            seller, new TradeRequestMsg { TargetObjectId = buyerEntity.ObjectId }, default));

        var ask = Assert.IsType<Fineage.Net.Contract.TradeAskNetMsg>(
            Last(rec, nameof(INetPresenceBridge.FanoutTradeAsk)));
        Assert.Equal(sellerEntity.ObjectId, ask.RequestorObjectId);
        Assert.Equal(sellerEntity.Name, ask.RequestorName);

        Assert.Null(await kit.World.TryTradeAnswerAsync(
            buyer,
            new TradeAnswerRequestMsg { RequestorObjectId = sellerEntity.ObjectId, Response = 1 },
            default));

        Assert.Equal(1, Assert.IsType<Fineage.Net.Contract.TradeRequestResultNetMsg>(
            Last(rec, nameof(INetPresenceBridge.FanoutTradeRequestResult))).Response);

        // Each side gets its own TradeStart naming the other partner, plus the initial confirm state.
        var starts = rec.Of(nameof(INetPresenceBridge.FanoutTradeStart));
        Assert.Equal(2, starts.Count);
        Assert.Equal(buyerEntity.ObjectId, Assert.IsType<Fineage.Net.Contract.TradeStartNetMsg>(starts[0][1]).PartnerObjectId);
        Assert.Equal(sellerEntity.ObjectId, Assert.IsType<Fineage.Net.Contract.TradeStartNetMsg>(starts[1][1]).PartnerObjectId);
        Assert.Equal(2, rec.Of(nameof(INetPresenceBridge.FanoutTradeConfirmState)).Count);

        Assert.Null(await kit.World.TryTradeAddItemAsync(
            seller, new TradeAddItemRequestMsg { InventoryObjectId = offered.ObjectId, Count = 1 }, default));

        var offer = Assert.IsType<Fineage.Net.Contract.TradeOfferUpdateNetMsg>(
            Last(rec, nameof(INetPresenceBridge.FanoutTradeOfferUpdate)));
        Assert.Equal(offered.ObjectId, offer.InventoryObjectId);
        Assert.Equal(offered.ItemId, offer.ItemId);

        Assert.Null(await kit.World.TryTradeDoneAsync(seller, new TradeDoneRequestMsg { Response = 1 }, default));
        var confirms = rec.Of(nameof(INetPresenceBridge.FanoutTradeConfirmState))
            .Select(call => Assert.IsType<Fineage.Net.Contract.TradeConfirmStateNetMsg>(call[1])).ToList();
        Assert.Contains(confirms, state => state.SelfConfirmed == 1);
        Assert.Contains(confirms, state => state.PartnerConfirmed == 1);
        Assert.Empty(rec.Of(nameof(INetPresenceBridge.FanoutTradeFinished)));

        Assert.Null(await kit.World.TryTradeDoneAsync(buyer, new TradeDoneRequestMsg { Response = 1 }, default));
        Assert.Equal(1, Assert.IsType<Fineage.Net.Contract.TradeFinishedNetMsg>(
            Last(rec, nameof(INetPresenceBridge.FanoutTradeFinished))).Response);
        Assert.DoesNotContain(buyerEntity.Inventory!.Items, item => item.ObjectId == offered.ObjectId);
        Assert.Contains(buyerEntity.Inventory.Items, item => item.ItemId == offered.ItemId);
    }

    [Fact]
    public async Task A_refused_ask_answers_zero_and_opens_no_window()
    {
        var (kit, rec) = World();
        var (seller, sellerEntity) = await kit.EnterAsync(classId: 0);
        var (buyer, buyerEntity) = await kit.EnterAsync(classId: 1);

        Assert.Null(await kit.World.TryTradeRequestAsync(
            seller, new TradeRequestMsg { TargetObjectId = buyerEntity.ObjectId }, default));
        Assert.Null(await kit.World.TryTradeAnswerAsync(
            buyer, new TradeAnswerRequestMsg { RequestorObjectId = sellerEntity.ObjectId, Response = 0 }, default));

        Assert.Equal(0, Assert.IsType<Fineage.Net.Contract.TradeRequestResultNetMsg>(
            Last(rec, nameof(INetPresenceBridge.FanoutTradeRequestResult))).Response);
        Assert.Empty(rec.Of(nameof(INetPresenceBridge.FanoutTradeStart)));
        Assert.Empty(rec.Of(nameof(INetPresenceBridge.FanoutTradeConfirmState)));
    }

    [Fact]
    public async Task An_offer_wider_than_eight_rows_is_refused()
    {
        var (kit, rec) = World();
        var (seller, sellerEntity) = await kit.EnterAsync(classId: 0);
        var (buyer, buyerEntity) = await kit.EnterAsync(classId: 1);
        Assert.Null(await kit.World.TryTradeRequestAsync(
            seller, new TradeRequestMsg { TargetObjectId = buyerEntity.ObjectId }, default));
        Assert.Null(await kit.World.TryTradeAnswerAsync(
            buyer, new TradeAnswerRequestMsg { RequestorObjectId = sellerEntity.ObjectId, Response = 1 }, default));

        var spare = sellerEntity.Inventory!.Items.Where(item => !item.IsEquipped).ToArray();
        int accepted = 0;
        foreach (var item in spare)
        {
            string? error = await kit.World.TryTradeAddItemAsync(
                seller, new TradeAddItemRequestMsg { InventoryObjectId = item.ObjectId, Count = 1 }, default);
            if (error == null)
                accepted++;
            else
                Assert.Equal(TradeProtocol.MaxOfferRows, accepted);
        }

        Assert.True(accepted <= TradeProtocol.MaxOfferRows);
    }

    [Fact]
    public async Task An_offer_count_above_owned_stack_is_refused()
    {
        var (kit, _) = World();
        var (seller, sellerEntity) = await kit.EnterAsync(classId: 0);
        var (buyer, buyerEntity) = await kit.EnterAsync(classId: 1);
        Assert.Null(await kit.World.TryTradeRequestAsync(
            seller, new TradeRequestMsg { TargetObjectId = buyerEntity.ObjectId }, default));
        Assert.Null(await kit.World.TryTradeAnswerAsync(
            buyer, new TradeAnswerRequestMsg { RequestorObjectId = sellerEntity.ObjectId, Response = 1 }, default));

        var offered = Tradable(sellerEntity);
        string? error = await kit.World.TryTradeAddItemAsync(
            seller,
            new TradeAddItemRequestMsg { InventoryObjectId = offered.ObjectId, Count = offered.Count + 1 },
            default);
        Assert.Equal(TradeProtocol.Item, error);
        Assert.Equal(offered.Count, sellerEntity.Inventory!.Find(offered.ObjectId)!.Count);
    }

    [Fact]
    public async Task A_finished_trade_cannot_confirm_again_without_a_session()
    {
        var (kit, _) = World();
        var (seller, sellerEntity) = await kit.EnterAsync(classId: 0);
        var (buyer, buyerEntity) = await kit.EnterAsync(classId: 1);
        var offered = Tradable(sellerEntity);
        int offeredObjectId = offered.ObjectId;
        int offeredItemId = offered.ItemId;

        Assert.Null(await kit.World.TryTradeRequestAsync(
            seller, new TradeRequestMsg { TargetObjectId = buyerEntity.ObjectId }, default));
        Assert.Null(await kit.World.TryTradeAnswerAsync(
            buyer, new TradeAnswerRequestMsg { RequestorObjectId = sellerEntity.ObjectId, Response = 1 }, default));
        Assert.Null(await kit.World.TryTradeAddItemAsync(
            seller, new TradeAddItemRequestMsg { InventoryObjectId = offeredObjectId, Count = 1 }, default));
        Assert.Null(await kit.World.TryTradeDoneAsync(seller, new TradeDoneRequestMsg { Response = 1 }, default));
        Assert.Null(await kit.World.TryTradeDoneAsync(buyer, new TradeDoneRequestMsg { Response = 1 }, default));

        Assert.DoesNotContain(sellerEntity.Inventory!.Items, item => item.ObjectId == offeredObjectId);
        Assert.Contains(buyerEntity.Inventory!.Items, item => item.ItemId == offeredItemId);

        // Second confirm after FinishTradeSession must not invent a new transfer.
        Assert.Equal(
            TradeProtocol.Session,
            await kit.World.TryTradeDoneAsync(seller, new TradeDoneRequestMsg { Response = 1 }, default));
        Assert.Equal(
            TradeProtocol.Session,
            await kit.World.TryTradeDoneAsync(buyer, new TradeDoneRequestMsg { Response = 1 }, default));
        Assert.Single(buyerEntity.Inventory.Items, item => item.ItemId == offeredItemId && !item.IsEquipped);
    }
}
