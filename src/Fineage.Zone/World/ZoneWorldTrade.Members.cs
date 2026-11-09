using Fineage.Game;
using Fineage.Game.Services;
using Fineage.Protocol;
using Fineage.Zone.Trade;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    static bool ValidateOffers(PlayerInventory inventory, List<TradeOfferRow> offers)
    {
        foreach (var row in offers)
        {
            var item = inventory.Find(row.InventoryObjectId);
            if (!TradeRules.OfferStillValid(
                    found: item != null,
                    equipped: item?.IsEquipped == true,
                    stackable: item?.Template.Stackable == true,
                    offeredCount: row.Count,
                    availableCount: item?.Count ?? 0))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Mobius <c>TradeList.doExchange</c>: <c>validateWeight(partnerList.calcItemsWeight())</c> then
    /// <c>validateCapacity(partnerList.countItemsSlots(owner))</c> against the receiving side.
    /// </summary>
    TradeRules.IncomingDeny IncomingDenyOf(ZoneEntity receiver, List<TradeOfferRow> offers)
    {
        if (receiver.Inventory == null || offers.Count == 0)
            return TradeRules.IncomingDeny.None;

        long incoming = 0;
        int slots = 0;
        foreach (var row in offers)
        {
            incoming = checked(incoming + (long)_weights.Of(row.ItemId) * row.Count);
            bool alreadyCarried = receiver.Inventory.FindByItemId(row.ItemId) != null;
            bool stackable = _game.Items.TryGet(row.ItemId, out var template) && template != null && template.Stackable;
            slots += WarehouseRules.SlotsFor(stackable, alreadyCarried, (int)Math.Min(int.MaxValue, row.Count));
        }

        return TradeRules.IncomingDenyOf(
            receiver.Inventory.TotalWeight,
            incoming,
            MaxLoadOf(receiver),
            receiver.Inventory.Items.Count,
            slots,
            WarehouseRules.InventorySlots(receiver.Race));
    }

    /// <summary>Mobius sends the same reply to both trade partners, whichever side actually overflowed.</summary>
    void DenyIncomingTrade(ZoneEntity requestor, ZoneEntity partner, TradeRules.IncomingDeny reason)
    {
        int id = reason == TradeRules.IncomingDeny.Slots ? SysMsgInventoryFull : SysMsgWeightLimit;
        string text = reason == TradeRules.IncomingDeny.Slots ? "Your inventory is full." : "You have exceeded the weight limit.";
        NotifyLine(requestor.ObjectId, id, text);
        NotifyLine(partner.ObjectId, id, text);
    }

    static void TransferOfferRow(
        PlayerInventory from,
        PlayerInventory to,
        TradeOfferRow row,
        List<InventoryUpdateEntryMsg> fromChanges,
        List<InventoryUpdateEntryMsg> toChanges)
    {
        var item = from.Find(row.InventoryObjectId)!;
        int itemId = item.ItemId;
        int enchant = item.EnchantLevel;
        int before = to.CountOf(itemId);

        bool stackable = item.Template.Stackable;

        if (!from.TryRemoveFromStack(item, (int)row.Count, out bool removedEntirely))
            return;

        if (removedEntirely)
            fromChanges.Add(InventoryMessages.RemoveRow(item, -1));
        else
            fromChanges.Add(InventoryMessages.ChangeRow(InventoryChange.Modify, item));

        var received = to.Add(itemId, (int)row.Count, enchant);
        int change = !stackable || before == 0 ? InventoryChange.Add : InventoryChange.Modify;
        toChanges.Add(InventoryMessages.ChangeRow(change, received));
    }

    void SendTradeStart(ZoneEntity viewer, int partnerId)
    {
        if (viewer.Session == null)
            return;
        if (!_byId.TryGetValue(partnerId, out var partner))
            return;
        NetBridge?.FanoutTradeStart([viewer.ObjectId], new TradeStartMsg
        {
            PartnerObjectId = partnerId,
            PartnerName = partner.Name
        });
    }

    void FanoutTradeOffer(ZoneTradeSession session, int actorId, TradeOfferUpdateMsg update)
    {
        foreach (int playerId in new[] { session.RequestorObjectId, session.PartnerObjectId })
        {
            if (!_byId.TryGetValue(playerId, out var entity) || entity.Session == null)
                continue;
            NetBridge?.FanoutTradeOfferUpdate([entity.ObjectId],
                new TradeOfferUpdateMsg
                {
                    IsOwn = playerId == actorId ? 1 : 0,
                    InventoryObjectId = update.InventoryObjectId,
                    ItemId = update.ItemId,
                    Count = update.Count,
                    EnchantLevel = update.EnchantLevel
                });
        }
    }

    void FanoutTradeConfirm(ZoneTradeSession session)
    {
        SendTradeConfirm(session.RequestorObjectId, session.RequestorConfirmed, session.PartnerConfirmed);
        SendTradeConfirm(session.PartnerObjectId, session.PartnerConfirmed, session.RequestorConfirmed);
    }

    void SendTradeConfirm(int viewerId, bool selfConfirmed, bool partnerConfirmed)
    {
        if (!_byId.TryGetValue(viewerId, out var entity) || entity.Session == null)
            return;
        NetBridge?.FanoutTradeConfirmState([viewerId], new TradeConfirmStateMsg
        {
            SelfConfirmed = selfConfirmed ? 1 : 0,
            PartnerConfirmed = partnerConfirmed ? 1 : 0
        });
    }

    void CancelTradeSession(ZoneTradeSession session) =>
        FinishTradeSession(session, exchanged: false);

    void FinishTradeSession(ZoneTradeSession session, bool exchanged)
    {
        int response = exchanged ? 1 : 0;
        foreach (int playerId in new[] { session.RequestorObjectId, session.PartnerObjectId })
        {
            if (_byId.TryGetValue(playerId, out var entity) && entity.Session != null)
                NetBridge?.FanoutTradeFinished([playerId], new TradeFinishedMsg
                {
                    Response = response
                });
        }

        UnbindTradeSession(session);
    }

    void QueueTradeAsk(ZoneEntity target, TradeAskMsg ask)
    {
        NetBridge?.FanoutTradeAsk([target.ObjectId], ask);
    }

    void QueueTradeRequestResult(ZoneEntity requestor, int response)
    {
        NetBridge?.FanoutTradeRequestResult([requestor.ObjectId],
            new TradeRequestResultMsg { Response = response });
    }

    void BindTradeSession(ZoneTradeSession session)
    {
        _tradeByPlayer[session.RequestorObjectId] = session;
        _tradeByPlayer[session.PartnerObjectId] = session;
    }

    void UnbindTradeSession(ZoneTradeSession session)
    {
        _tradeByPlayer.Remove(session.RequestorObjectId);
        _tradeByPlayer.Remove(session.PartnerObjectId);
    }

    bool TradeInRange(int objectIdA, int objectIdB)
    {
        if (!_byId.TryGetValue(objectIdA, out var a) || !_byId.TryGetValue(objectIdB, out var b))
            return false;
        return NpcHtmlProtocol.InRange(a.X, a.Y, a.Z, b.X, b.Y, b.Z, TradeProtocol.Distance);
    }
}
