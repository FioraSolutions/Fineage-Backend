using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

sealed partial class NetWorldEventPublisher
{
    void IWorldEventPublisher.FanoutPledgeStatus(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PledgeStatusChangedMsg status)
    {
        var payload = NetCodec.EncodePledgeStatus(ToNetPledgeStatus(status));
                    _presence.TryBroadcast(watcherObjectIds, NetOpcode.PledgeStatus, payload);
    }

    void IWorldEventPublisher.FanoutPledgeMemberList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PledgeMemberListMsg list)
    {
        var payload = NetCodec.EncodePledgeMemberList(ToNetPledgeMemberList(list));
                    _presence.TryBroadcast(watcherObjectIds, NetOpcode.PledgeMemberList, payload);
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
                    _presence.TryBroadcast(watcherObjectIds, NetOpcode.ClanAskJoin, payload);
    }

    void IWorldEventPublisher.FanoutClanJoinResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.ClanJoinResultMsg result)
    {
        var payload = NetCodec.EncodeClanJoinResult(new ClanJoinResultNetMsg { Response = result.Response });
                    _presence.TryBroadcast(watcherObjectIds, NetOpcode.ClanJoinResult, payload);
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
        CharPenaltyExpiryUnixMs = status.CharPenaltyExpiryUnixMs,
        Reputation = status.Reputation,
        LargeCrestId = status.LargeCrestId,
        AllyId = status.AllyId,
        AllyName = status.AllyName ?? "",
        AllyCrestId = status.AllyCrestId,
        CastleId = status.CastleId,
        HallId = status.HallId,
        AtWar = status.AtWar,
        NewLeaderObjectId = status.NewLeaderObjectId,
        SubPledges = (status.SubPledges ?? []).Select(s => new SubPledgeNet { Type = s.Type, Name = s.Name ?? "", LeaderObjectId = s.LeaderObjectId }).ToArray(),
        RankPrivileges = status.RankPrivileges ?? [],
        Skills = (status.Skills ?? []).Select(s => new ClanSkillNet { SkillId = s.SkillId, Level = s.Level }).ToArray()
    };

    internal static PledgeMemberListNetMsg ToNetPledgeMemberList(Fineage.Protocol.PledgeMemberListMsg list)
    {
        var src = list.Members ?? [];
        var rows = new PledgeMemberNet[src.Length];
        var extras = new PledgeMemberExtraNet[list.ClanId == 0 ? 0 : src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            if (extras.Length > 0)
            {
                extras[i] = new PledgeMemberExtraNet
                {
                    PowerGrade = src[i].PowerGrade,
                    SponsorObjectId = src[i].SponsorObjectId,
                    ApprenticeObjectId = src[i].ApprenticeObjectId,
                    PledgeClass = src[i].PledgeClass
                };
            }
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
            Members = rows,
            Extras = extras
        };
    }
}
