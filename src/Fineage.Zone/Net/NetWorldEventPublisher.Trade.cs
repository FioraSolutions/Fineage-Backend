using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

sealed partial class NetWorldEventPublisher
{
    void IWorldEventPublisher.FanoutTradeAsk(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.TradeAskMsg ask)
    {
        var payload = NetCodec.EncodeTradeAsk(new TradeAskNetMsg
        {
            RequestorObjectId = ask.RequestorObjectId,
            RequestorName = ask.RequestorName ?? ""
        });
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.TradeAsk, payload);
    }

    void IWorldEventPublisher.FanoutTradeRequestResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.TradeRequestResultMsg result)
    {
        var payload = NetCodec.EncodeTradeRequestResult(new TradeRequestResultNetMsg { Response = result.Response });
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.TradeRequestResult, payload);
    }

    void IWorldEventPublisher.FanoutTradeStart(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.TradeStartMsg start)
    {
        var payload = NetCodec.EncodeTradeStart(new TradeStartNetMsg
        {
            PartnerObjectId = start.PartnerObjectId,
            PartnerName = start.PartnerName ?? ""
        });
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.TradeStart, payload);
    }

    void IWorldEventPublisher.FanoutTradeOfferUpdate(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.TradeOfferUpdateMsg update)
    {
        var payload = NetCodec.EncodeTradeOfferUpdate(new TradeOfferUpdateNetMsg
        {
            IsOwn = update.IsOwn,
            InventoryObjectId = update.InventoryObjectId,
            ItemId = update.ItemId,
            Count = update.Count,
            EnchantLevel = update.EnchantLevel
        });
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.TradeOfferUpdate, payload);
    }

    void IWorldEventPublisher.FanoutTradeConfirmState(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.TradeConfirmStateMsg state)
    {
        var payload = NetCodec.EncodeTradeConfirmState(new TradeConfirmStateNetMsg
        {
            SelfConfirmed = state.SelfConfirmed,
            PartnerConfirmed = state.PartnerConfirmed
        });
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.TradeConfirmState, payload);
    }

    void IWorldEventPublisher.FanoutTradeFinished(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.TradeFinishedMsg finished)
    {
        var payload = NetCodec.EncodeTradeFinished(new TradeFinishedNetMsg { Response = finished.Response });
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.TradeFinished, payload);
    }
}
