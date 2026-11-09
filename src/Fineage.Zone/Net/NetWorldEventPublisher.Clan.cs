using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

sealed partial class NetWorldEventPublisher
{
    void IWorldEventPublisher.FanoutPledgeStatus(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PledgeStatusChangedMsg status)
    {
        var payload = NetCodec.EncodePledgeStatus(ToNetPledgeStatus(status));
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.PledgeStatus, payload);
    }

    void IWorldEventPublisher.FanoutPledgeMemberList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PledgeMemberListMsg list)
    {
        var payload = NetCodec.EncodePledgeMemberList(ToNetPledgeMemberList(list));
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.PledgeMemberList, payload);
    }

    void IWorldEventPublisher.FanoutClanAskJoin(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.ClanAskJoinMsg ask)
    {
        var payload = NetCodec.EncodeClanAskJoin(new ClanAskJoinNetMsg
        {
            RequestorObjectId = ask.RequestorObjectId,
            RequestorName = ask.RequestorName ?? "",
            ClanId = ask.ClanId,
            ClanName = ask.ClanName ?? "",
            PledgeType = ask.PledgeType
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.ClanAskJoin, payload);
    }

    void IWorldEventPublisher.FanoutClanJoinResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.ClanJoinResultMsg result)
    {
        var payload = NetCodec.EncodeClanJoinResult(new ClanJoinResultNetMsg { Response = result.Response });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.ClanJoinResult, payload);
    }

    internal static PledgeStatusNetMsg ToNetPledgeStatus(Fineage.Protocol.PledgeStatusChangedMsg status) => new()
    {
        ClanId = status.ClanId,
        Name = status.Name ?? "",
        Level = status.Level,
        LeaderObjectId = status.LeaderObjectId,
        CrestId = status.CrestId,
        MemberCount = status.MemberCount,
        DissolvingExpiryUnixMs = status.DissolvingExpiryUnixMs,
        CharPenaltyExpiryUnixMs = status.CharPenaltyExpiryUnixMs
    };

    internal static PledgeMemberListNetMsg ToNetPledgeMemberList(Fineage.Protocol.PledgeMemberListMsg list)
    {
        var src = list.Members ?? [];
        var rows = new PledgeMemberNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            rows[i] = new PledgeMemberNet
            {
                ObjectId = src[i].ObjectId,
                Name = src[i].Name ?? "",
                ClassId = src[i].ClassId,
                Level = src[i].Level,
                Online = src[i].Online,
                PledgeType = src[i].PledgeType
            };
        }

        return new PledgeMemberListNetMsg
        {
            ClanId = list.ClanId,
            LeaderObjectId = list.LeaderObjectId,
            Members = rows
        };
    }
}
