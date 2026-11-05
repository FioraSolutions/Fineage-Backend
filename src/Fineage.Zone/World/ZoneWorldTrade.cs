using Fineage.Game;
using Fineage.Game.Services;
using Fineage.Protocol;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    readonly Dictionary<int, int> _pendingTradeAsk = [];
    readonly Dictionary<int, ZoneTradeSession> _tradeByPlayer = [];

    sealed class ZoneTradeSession
    {
        public required int RequestorObjectId { get; init; }
        public required int PartnerObjectId { get; init; }
        public List<TradeOfferRow> RequestorOffers { get; } = [];
        public List<TradeOfferRow> PartnerOffers { get; } = [];
        public bool RequestorConfirmed { get; set; }
        public bool PartnerConfirmed { get; set; }
    }

    sealed class TradeOfferRow
    {
        public int InventoryObjectId { get; set; }
        public int ItemId { get; set; }
        public long Count { get; set; }
        public int EnchantLevel { get; set; }
    }

    public async Task<string?> TryTradeRequestAsync(ZoneSession session, TradeRequestMsg request, CancellationToken ct)
    {
        if (request == null)
            return "bad_request";
        if (!session.InWorld)
            return "world";
        if (request.TargetObjectId == session.ObjectId)
            return "bad_request";

        string? error;
        lock (_gate)
            error = TryTradeRequest(session, request);
        return error;
    }

    public async Task<string?> TryTradeAnswerAsync(ZoneSession session, TradeAnswerRequestMsg answer, CancellationToken ct)
    {
        if (answer == null)
            return "bad_request";
        if (!session.InWorld)
            return "world";

        string? error;
        lock (_gate)
            error = TryTradeAnswer(session, answer);
        return error;
    }

    public async Task<string?> TryTradeAddItemAsync(ZoneSession session, TradeAddItemRequestMsg add, CancellationToken ct)
    {
        if (add == null)
            return "bad_request";
        if (!session.InWorld)
            return "world";

        string? error;
        lock (_gate)
            error = TryTradeAddItem(session, add);
        return error;
    }

    public async Task<string?> TryTradeDoneAsync(ZoneSession session, TradeDoneRequestMsg done, CancellationToken ct)
    {
        if (done == null)
            return "bad_request";
        if (!session.InWorld)
            return "world";

        string? error;
        lock (_gate)
            error = TryTradeDone(session, done);
        return error;
    }

    void TradePlayerLeftWorld(int objectId)
    {
        _pendingTradeAsk.Remove(objectId);
        foreach (var (targetId, requestorId) in _pendingTradeAsk.ToList())
        {
            if (requestorId == objectId)
                _pendingTradeAsk.Remove(targetId);
        }

        if (!_tradeByPlayer.TryGetValue(objectId, out var session))
            return;

        CancelTradeSession(session);
    }

    string? TryTradeRequest(ZoneSession requestor, TradeRequestMsg request)
    {
        if (!IsLive(requestor))
            return "world";
        if (_tradeByPlayer.ContainsKey(requestor.ObjectId) || _pendingTradeAsk.ContainsKey(requestor.ObjectId))
            return TradeProtocol.Busy;

        if (!_byId.TryGetValue(request.TargetObjectId, out var targetEntity) ||
            targetEntity.Kind != ZoneEntityKind.Player ||
            targetEntity.Session == null ||
            !IsLive(targetEntity.Session))
            return "target";

        if (targetEntity.Dead)
            return "target";
        if (_tradeByPlayer.ContainsKey(request.TargetObjectId) || _pendingTradeAsk.ContainsKey(request.TargetObjectId))
            return TradeProtocol.Busy;

        if (!TradeInRange(requestor.ObjectId, request.TargetObjectId))
            return TradeProtocol.Range;

        _pendingTradeAsk[request.TargetObjectId] = requestor.ObjectId;
        var ask = new TradeAskMsg
        {
            RequestorObjectId = requestor.ObjectId,
            RequestorName = requestor.Name
        };
        QueueTradeAsk(targetEntity, ask);
        return null;
    }

    string? TryTradeAnswer(ZoneSession partner, TradeAnswerRequestMsg answer)
    {
        if (!IsLive(partner))
            return "world";
        if (!_pendingTradeAsk.TryGetValue(partner.ObjectId, out int requestorId) ||
            requestorId != answer.RequestorObjectId)
            return TradeProtocol.Invite;

        _pendingTradeAsk.Remove(partner.ObjectId);

        if (!_byId.TryGetValue(answer.RequestorObjectId, out var requestorEntity) ||
            requestorEntity.Session == null ||
            !IsLive(requestorEntity.Session))
            return TradeProtocol.Invite;

        if (answer.Response != 1)
        {
            QueueTradeRequestResult(requestorEntity, 0);
            return null;
        }

        if (_tradeByPlayer.ContainsKey(partner.ObjectId) || _tradeByPlayer.ContainsKey(answer.RequestorObjectId))
        {
            QueueTradeRequestResult(requestorEntity, 0);
            return null;
        }

        if (!TradeInRange(answer.RequestorObjectId, partner.ObjectId))
        {
            QueueTradeRequestResult(requestorEntity, 0);
            return null;
        }

        if (!_byId.TryGetValue(partner.ObjectId, out var partnerEntity) || partnerEntity.Session == null)
            return TradeProtocol.Invite;

        var session = new ZoneTradeSession
        {
            RequestorObjectId = answer.RequestorObjectId,
            PartnerObjectId = partner.ObjectId
        };
        BindTradeSession(session);

        QueueTradeRequestResult(requestorEntity, 1);
        SendTradeStart(requestorEntity, partner.ObjectId);
        SendTradeStart(partnerEntity, answer.RequestorObjectId);
        FanoutTradeConfirm(session);
        return null;
    }

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
        if (item == null)
            return InventoryProtocol.NotOwned;
        if (item.IsEquipped || !item.Template.CanDropTradeStore)
            return TradeProtocol.Item;
        if (count > item.Count)
            return TradeProtocol.Item;
        if (!item.Template.Stackable && count != 1)
            return TradeProtocol.Item;

        bool requestorSide = actor.ObjectId == session.RequestorObjectId;
        var offers = requestorSide ? session.RequestorOffers : session.PartnerOffers;
        var row = offers.Find(r => r.InventoryObjectId == add.InventoryObjectId);
        if (row == null)
        {
            if (offers.Count >= TradeProtocol.MaxOfferRows)
                return TradeProtocol.Item;
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
        if (deny == TradeIncomingDeny.None)
            deny = IncomingDenyOf(partner, session.RequestorOffers);
        if (deny != TradeIncomingDeny.None)
        {
            DenyIncomingTrade(requestor, partner, deny);
            return false;
        }

        var requestorChanges = new List<InventoryUpdateEntryMsg>();
        var partnerChanges = new List<InventoryUpdateEntryMsg>();

        foreach (var row in session.RequestorOffers)
            TransferOfferRow(requestor.Inventory, partner.Inventory, row, requestorChanges, partnerChanges);
        foreach (var row in session.PartnerOffers)
            TransferOfferRow(partner.Inventory, requestor.Inventory, row, partnerChanges, requestorChanges);

        SaveInventory(requestor.Session, requestor.Inventory);
        SaveInventory(partner.Session, partner.Inventory);

        if (requestorChanges.Count > 0)
        {
            var inv = new InventoryUpdateMsg { Changes = requestorChanges.ToArray() };
            NetBridge?.FanoutInventoryUpdate([requestor.ObjectId], ToNetInventoryUpdate(inv));
            SelfUserInfo(requestor);
        }

        if (partnerChanges.Count > 0)
        {
            var inv = new InventoryUpdateMsg { Changes = partnerChanges.ToArray() };
            NetBridge?.FanoutInventoryUpdate([partner.ObjectId], ToNetInventoryUpdate(inv));
            SelfUserInfo(partner);
        }

        FinishTradeSession(session, exchanged: true);
        return true;
    }

    static bool ValidateOffers(PlayerInventory inventory, List<TradeOfferRow> offers)
    {
        foreach (var row in offers)
        {
            var item = inventory.Find(row.InventoryObjectId);
            if (item == null || item.IsEquipped || item.Count < row.Count)
                return false;
            if (!item.Template.Stackable && row.Count != 1)
                return false;
        }

        return true;
    }

    /// <summary>Which message <see cref="TradeIncomingDeny"/> wants. Ordered the way Mobius reports it: weight first.</summary>
    enum TradeIncomingDeny
    {
        None = 0,
        Weight = 1,
        Slots = 2
    }

    /// <summary>
    /// Mobius <c>TradeList.doExchange</c>: <c>validateWeight(partnerList.calcItemsWeight())</c> then
    /// <c>validateCapacity(partnerList.countItemsSlots(owner))</c> against the receiving side.
    /// </summary>
    TradeIncomingDeny IncomingDenyOf(ZoneEntity receiver, List<TradeOfferRow> offers)
    {
        if (receiver.Inventory == null || offers.Count == 0)
            return TradeIncomingDeny.None;

        long incoming = 0;
        int slots = 0;
        foreach (var row in offers)
        {
            incoming = checked(incoming + (long)_weights.Of(row.ItemId) * row.Count);
            bool alreadyCarried = receiver.Inventory.FindByItemId(row.ItemId) != null;
            bool stackable = _game.Items.TryGet(row.ItemId, out var template) && template != null && template.Stackable;
            slots += WarehouseRules.SlotsFor(stackable, alreadyCarried, (int)Math.Min(int.MaxValue, row.Count));
        }

        if (receiver.Inventory.TotalWeight + incoming > MaxLoadOf(receiver))
            return TradeIncomingDeny.Weight;
        if (receiver.Inventory.Items.Count + slots > WarehouseRules.InventorySlots(receiver.Race))
            return TradeIncomingDeny.Slots;
        return TradeIncomingDeny.None;
    }

    /// <summary>Mobius sends the same reply to both trade partners, whichever side actually overflowed.</summary>
    void DenyIncomingTrade(ZoneEntity requestor, ZoneEntity partner, TradeIncomingDeny reason)
    {
        int id = reason == TradeIncomingDeny.Slots ? SysMsgInventoryFull : SysMsgWeightLimit;
        string text = reason == TradeIncomingDeny.Slots ? "Your inventory is full." : "You have exceeded the weight limit.";
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
        var start = new TradeStartMsg
        {
            PartnerObjectId = partnerId,
            PartnerName = partner.Name
        };
        NetBridge?.FanoutTradeStart([viewer.ObjectId], new Fineage.Net.Contract.TradeStartNetMsg
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
                new Fineage.Net.Contract.TradeOfferUpdateNetMsg
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
        NetBridge?.FanoutTradeConfirmState([viewerId], new Fineage.Net.Contract.TradeConfirmStateNetMsg
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
                NetBridge?.FanoutTradeFinished([playerId], new Fineage.Net.Contract.TradeFinishedNetMsg
                {
                    Response = response
                });
        }

        UnbindTradeSession(session);
    }

    void QueueTradeAsk(ZoneEntity target, TradeAskMsg ask)
    {
        NetBridge?.FanoutTradeAsk([target.ObjectId], new Fineage.Net.Contract.TradeAskNetMsg
        {
            RequestorObjectId = ask.RequestorObjectId,
            RequestorName = ask.RequestorName ?? ""
        });
    }

    void QueueTradeRequestResult(ZoneEntity requestor, int response)
    {
        NetBridge?.FanoutTradeRequestResult([requestor.ObjectId],
            new Fineage.Net.Contract.TradeRequestResultNetMsg { Response = response });
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
