using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

sealed partial class NetWorldEventPublisher
{
    void IWorldEventPublisher.FanoutWeddingEngageAsk(IReadOnlyList<int> watcherObjectIds, WeddingEngageAskMsg ask)
    {
        var payload = NetCodec.EncodeWeddingEngageAsk(new WeddingEngageAskNetMsg
        {
            RequestorName = ask.RequestorName ?? "",
            RequestorObjectId = ask.RequestorObjectId
        });
                    _presence.TryBroadcast(watcherObjectIds, NetOpcode.WeddingEngageAsk, payload);
    }
}