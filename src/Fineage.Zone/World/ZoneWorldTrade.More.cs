using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Game.Services;
using Fineage.Protocol;
using Fineage.Zone.Augmentation;
using Fineage.Zone.Trade;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    string? TryTradeAddItem(ZoneSession actor, TradeAddItemRequestMsg add)
    {
        if (!IsLive(actor))
            return "world";
        if (!_tradeByPlayer.TryGetValue(actor.ObjectId, out var session))
            return TradeProtocol.Session;

        if (!TradeInRange(session.RequestorObjectId, session.PartnerObjectId))
        {
            CancelTradeSession(session);
            return TradeProtocol.Range;
        }

        if (!_byId.TryGetValue(actor.ObjectId, out var entity) || entity.Inventory == null)
            return TradeProtocol.Item;

        long count = add.Count <= 0 ? 1 : add.Count;
        var item = entity.Inventory.Find(add.InventoryObjectId);
        string? offerError = TradeRules.OfferItemError(
            found: item != null,
            equipped: item?.IsEquipped == true,
            canDropTradeStore: item?.Template.CanDropTradeStore == true,
            stackable: item?.Template.Stackable == true,
            requestedCount: count,
            availableCount: item?.Count ?? 0,
            augmented: item != null && AugmentationRules.IsAugmented(item));
        if (offerError != null || item == null)
            return offerError ?? TradeProtocol.Item;

        bool requestorSide = actor.ObjectId == session.RequestorObjectId;
        var offers = requestorSide ? session.RequestorOffers : session.PartnerOffers;
        var row = offers.Find(r => r.InventoryObjectId == add.InventoryObjectId);
        if (row == null)
        {
            string? rowError = TradeRules.NewOfferRowError(offers.Count);
            if (rowError != null)
                return rowError;
            row = new TradeOfferRow();
            offers.Add(row);
        }

        row.InventoryObjectId = item.ObjectId;
        row.ItemId = item.ItemId;
        row.Count = count;
        row.EnchantLevel = item.EnchantLevel;

        session.RequestorConfirmed = false;
        session.PartnerConfirmed = false;

        var update = new TradeOfferUpdateMsg
        {
            IsOwn = 1,
            InventoryObjectId = row.InventoryObjectId,
            ItemId = row.ItemId,
            Count = row.Count,
            EnchantLevel = row.EnchantLevel
        };
        FanoutTradeOffer(session, actor.ObjectId, update);
        FanoutTradeConfirm(session);
        return null;
    }

    string? TryTradeDone(ZoneSession actor, TradeDoneRequestMsg done)
    {
        if (!IsLive(actor))
            return "world";
        if (!_tradeByPlayer.TryGetValue(actor.ObjectId, out var session))
            return TradeProtocol.Session;

        if (done.Response == 0)
        {
            CancelTradeSession(session);
            return null;
        }

        if (!TradeInRange(session.RequestorObjectId, session.PartnerObjectId))
        {
            CancelTradeSession(session);
            return TradeProtocol.Range;
        }

        if (actor.ObjectId == session.RequestorObjectId)
            session.RequestorConfirmed = true;
        else
            session.PartnerConfirmed = true;

        FanoutTradeConfirm(session);

        if (!session.RequestorConfirmed || !session.PartnerConfirmed)
            return null;

        if (!TryExecuteTrade(session))
            CancelTradeSession(session);

        return null;
    }

    bool TryExecuteTrade(ZoneTradeSession session)
    {
        if (!_byId.TryGetValue(session.RequestorObjectId, out var requestor) ||
            !_byId.TryGetValue(session.PartnerObjectId, out var partner) ||
            requestor.Inventory == null ||
            partner.Inventory == null ||
            requestor.Session == null ||
            partner.Session == null ||
            !IsLive(requestor.Session) ||
            !IsLive(partner.Session))
            return false;

        if (!ValidateOffers(requestor.Inventory, session.RequestorOffers) ||
            !ValidateOffers(partner.Inventory, session.PartnerOffers))
            return false;

        // Mobius TradeList.doExchange (TradeList.java:574-583): each receiver is checked separately, weight before
        // slots, and both players get the same message. A trade that would overload the receiver or overflow their bag
        // is refused outright instead of the transfer half-applying.
        var deny = IncomingDenyOf(requestor, session.PartnerOffers);
        if (deny == TradeRules.IncomingDeny.None)
            deny = IncomingDenyOf(partner, session.RequestorOffers);
        if (deny != TradeRules.IncomingDeny.None)
        {
            DenyIncomingTrade(requestor, partner, deny);
            return false;
        }

        var requestorChanges = new List<InventoryUpdateEntryMsg>();
        var partnerChanges = new List<InventoryUpdateEntryMsg>();

        LedgerMove? move = null;
        if (session.RequestorOffers.Count + session.PartnerOffers.Count > 0)
        {
            move = BeginLedgerMove(LedgerTxKind.TradeSettle, requestor.ObjectId, partner.ObjectId);
            ObserveLedger(move, LedgerAccount.Inventory(requestor.ObjectId));
            ObserveLedger(move, LedgerAccount.Inventory(partner.ObjectId));
            foreach (var row in session.RequestorOffers)
                PostTradeRow(move, requestor, partner, row);
            foreach (var row in session.PartnerOffers)
                PostTradeRow(move, partner, requestor, row);
        }

        if (move != null && move.CheckCommit() != LedgerRejection.None)
        {
            ZoneLog.Warn(
                $"trade settlement refused between {requestor.ObjectId} and {partner.ObjectId}: ledger rejected the postings");
            CancelTradeSession(session);
            return false;
        }

        foreach (var row in session.RequestorOffers)
            TransferOfferRow(requestor.Inventory, partner.Inventory, row, requestorChanges, partnerChanges);
        foreach (var row in session.PartnerOffers)
            TransferOfferRow(partner.Inventory, requestor.Inventory, row, partnerChanges, requestorChanges);
        move?.Commit(LedgerHoldingsOf);

        SaveInventory(requestor.Session, requestor.Inventory);
        SaveInventory(partner.Session, partner.Inventory);

        if (requestorChanges.Count > 0)
        {
            var inv = new InventoryUpdateMsg { Changes = requestorChanges.ToArray() };
            SelfUserInfo(requestor);
        }

        if (partnerChanges.Count > 0)
        {
            var inv = new InventoryUpdateMsg { Changes = partnerChanges.ToArray() };
            SelfUserInfo(partner);
        }

        FinishTradeSession(session, exchanged: true);
        return true;
    }

}
