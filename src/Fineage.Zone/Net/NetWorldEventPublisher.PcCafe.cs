using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

sealed partial class NetWorldEventPublisher
{
    void IWorldEventPublisher.FanoutPcCafePointInfo(IReadOnlyList<int> watcherObjectIds, PcCafePointInfoMsg info)
    {
        var payload = NetCodec.EncodePcCafePointInfo(new PcCafePointInfoNetMsg
        {
            Points = info.Points,
            PointsToAdd = info.PointsToAdd,
            PeriodType = info.PeriodType,
            RemainTime = info.RemainTime,
            PointType = info.PointType,
            Time = info.Time
        });
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.ExPCCafePointInfo, payload);
    }
}
