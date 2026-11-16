using Fineage.Net.Contract;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

sealed partial class NetWorldEventPublisher
{
    void SendAll(IReadOnlyList<int> watchers, ushort opcode, byte[] payload)
    {
        foreach (int watcher in watchers)
            _presence.TrySend(watcher, opcode, payload);
    }

    void IWorldEventPublisher.FanoutCrestData(IReadOnlyList<int> watcherObjectIds, CrestDataNetMsg crest) =>
        SendAll(watcherObjectIds, NetOpcode.CrestData, NetCodec.EncodeCrestData(crest));

    void IWorldEventPublisher.FanoutPledgeWarList(IReadOnlyList<int> watcherObjectIds, PledgeWarListNetMsg wars) =>
        SendAll(watcherObjectIds, NetOpcode.PledgeWarList, NetCodec.EncodePledgeWarList(wars));

    void IWorldEventPublisher.FanoutRelationChanged(IReadOnlyList<int> watcherObjectIds, RelationChangedNetMsg relation) =>
        SendAll(watcherObjectIds, NetOpcode.RelationChanged, NetCodec.EncodeRelationChanged(relation));

    void IWorldEventPublisher.FanoutPledgeInfo(IReadOnlyList<int> watcherObjectIds, PledgeInfoNetMsg info) =>
        SendAll(watcherObjectIds, NetOpcode.PledgeInfo, NetCodec.EncodePledgeInfo(info));

    void IWorldEventPublisher.FanoutAllyInfo(IReadOnlyList<int> watcherObjectIds, AllyInfoNetMsg info) =>
        SendAll(watcherObjectIds, NetOpcode.AllyInfo, NetCodec.EncodeAllyInfo(info));

    void IWorldEventPublisher.FanoutAllyAskJoin(IReadOnlyList<int> watcherObjectIds, AllyAskJoinNetMsg ask) =>
        SendAll(watcherObjectIds, NetOpcode.AllyAskJoin, NetCodec.EncodeAllyAskJoin(ask));

    void IWorldEventPublisher.FanoutClanHallList(IReadOnlyList<int> watcherObjectIds, ClanHallListNetMsg halls) =>
        SendAll(watcherObjectIds, NetOpcode.ClanHallList, NetCodec.EncodeClanHallList(halls));
}
