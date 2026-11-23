using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

sealed partial class NetWorldEventPublisher
{
    void IWorldEventPublisher.FanoutCommandChannelAskJoin(
        IReadOnlyList<int> watcherObjectIds,
        CommandChannelAskJoinMsg ask)
    {
        var payload = NetCodec.EncodeCommandChannelAskJoin(new CommandChannelAskJoinNetMsg
        {
            RequestorName = ask.RequestorName ?? "",
            RequestorObjectId = ask.RequestorObjectId
        });
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.CommandChannelAskJoin, payload);
    }

    void IWorldEventPublisher.FanoutCommandChannelOpen(IReadOnlyList<int> watcherObjectIds)
    {
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.CommandChannelOpen, []);
    }

    void IWorldEventPublisher.FanoutCommandChannelClose(IReadOnlyList<int> watcherObjectIds)
    {
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.CommandChannelClose, []);
    }

    void IWorldEventPublisher.FanoutCommandChannelInfo(
        IReadOnlyList<int> watcherObjectIds,
        CommandChannelInfoMsg info)
    {
        var parties = info.Parties ?? [];
        var rows = new CommandChannelPartyRowNet[parties.Length];
        for (int i = 0; i < parties.Length; i++)
        {
            rows[i] = new CommandChannelPartyRowNet
            {
                LeaderName = parties[i].LeaderName ?? "",
                LeaderObjectId = parties[i].LeaderObjectId,
                MemberCount = parties[i].MemberCount
            };
        }

        var payload = NetCodec.EncodeCommandChannelInfo(new CommandChannelInfoNetMsg
        {
            LeaderName = info.LeaderName ?? "",
            LootType = info.LootType,
            MemberCount = info.MemberCount,
            Parties = rows
        });
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.CommandChannelInfo, payload);
    }
}