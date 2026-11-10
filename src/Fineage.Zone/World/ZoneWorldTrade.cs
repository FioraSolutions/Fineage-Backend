using Fineage.Game;
using Fineage.Game.Services;
using Fineage.Protocol;
using Fineage.Zone.Trade;

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

    public Task<string?> TryTradeRequestAsync(ZoneSession session, TradeRequestMsg request, CancellationToken ct)
    {
        if (request == null)
            return Task.FromResult<string?>("bad_request");
        if (!session.InWorld)
            return Task.FromResult<string?>("world");
        if (request.TargetObjectId == session.ObjectId)
            return Task.FromResult<string?>("bad_request");

        return Task.FromResult(WithDeferredInventoryPersist(() => TryTradeRequest(session, request)));
    }

    public Task<string?> TryTradeAnswerAsync(ZoneSession session, TradeAnswerRequestMsg answer, CancellationToken ct)
    {
        if (answer == null)
            return Task.FromResult<string?>("bad_request");
        if (!session.InWorld)
            return Task.FromResult<string?>("world");

        return Task.FromResult(WithDeferredInventoryPersist(() => TryTradeAnswer(session, answer)));
    }

    public Task<string?> TryTradeAddItemAsync(ZoneSession session, TradeAddItemRequestMsg add, CancellationToken ct)
    {
        if (add == null)
            return Task.FromResult<string?>("bad_request");
        if (!session.InWorld)
            return Task.FromResult<string?>("world");

        return Task.FromResult(WithDeferredInventoryPersist(() => TryTradeAddItem(session, add)));
    }

    public Task<string?> TryTradeDoneAsync(ZoneSession session, TradeDoneRequestMsg done, CancellationToken ct)
    {
        if (done == null)
            return Task.FromResult<string?>("bad_request");
        if (!session.InWorld)
            return Task.FromResult<string?>("world");

        // World mutate under _gate; Postgres inventory write after unlock (B8).
        return Task.FromResult(WithDeferredInventoryPersist(() => TryTradeDone(session, done)));
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
        if (_byId.TryGetValue(requestor.ObjectId, out var self) &&
            self.PrivateStoreType != PrivateStoreProtocol.TypeNone)
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
        if (targetEntity.PrivateStoreType != PrivateStoreProtocol.TypeNone)
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

}
