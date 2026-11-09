using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

sealed partial class NetWorldEventPublisher
{
    void IWorldEventPublisher.FanoutPartyAskJoin(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PartyAskJoinMsg ask)
    {
        var payload = NetCodec.EncodePartyAskJoin(new PartyAskJoinNetMsg
        {
            RequestorObjectId = ask.RequestorObjectId,
            RequestorName = ask.RequestorName ?? "",
            ItemDistribution = ask.ItemDistribution
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.PartyAskJoin, payload);
    }

    void IWorldEventPublisher.FanoutPartyJoinResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PartyJoinResultMsg result)
    {
        var payload = NetCodec.EncodePartyJoinResult(new PartyJoinResultNetMsg { Response = result.Response });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.PartyJoinResult, payload);
    }

    void IWorldEventPublisher.FanoutPartyMemberList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PartyMemberListMsg list)
    {
        var src = list.Members ?? [];
        var members = new PartyMemberEntryNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            members[i] = new PartyMemberEntryNet
            {
                ObjectId = src[i].ObjectId,
                Name = src[i].Name ?? "",
                ClassId = src[i].ClassId,
                Level = src[i].Level,
                CurHp = src[i].CurHp,
                MaxHp = src[i].MaxHp,
                CurMp = src[i].CurMp,
                MaxMp = src[i].MaxMp
            };
        }
        var payload = NetCodec.EncodePartyMemberList(new PartyMemberListNetMsg
        {
            LeaderObjectId = list.LeaderObjectId,
            PartyId = list.PartyId,
            Members = members
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.PartyMemberList, payload);
    }
}
