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
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.PartyAskJoin, payload);
    }

    void IWorldEventPublisher.FanoutPartyJoinResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PartyJoinResultMsg result)
    {
        var payload = NetCodec.EncodePartyJoinResult(new PartyJoinResultNetMsg { Response = result.Response });
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.PartyJoinResult, payload);
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
                MaxMp = src[i].MaxMp,
                CurCp = src[i].CurCp,
                MaxCp = src[i].MaxCp
            };
        }
        var payload = NetCodec.EncodePartyMemberList(new PartyMemberListNetMsg
        {
            LeaderObjectId = list.LeaderObjectId,
            PartyId = list.PartyId,
            Members = members
        });
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.PartyMemberList, payload);
    }

    void IWorldEventPublisher.FanoutPartyMemberUpdate(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PartyMemberUpdateMsg u)
    {
        var payload = NetCodec.EncodePartyMemberUpdate(new PartyMemberUpdateNetMsg
        {
            ObjectId = u.ObjectId,
            Level = u.Level,
            ClassId = u.ClassId,
            CurHp = u.CurHp,
            MaxHp = u.MaxHp,
            CurMp = u.CurMp,
            MaxMp = u.MaxMp,
            CurCp = u.CurCp,
            MaxCp = u.MaxCp
        });
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.PartyMemberUpdate, payload);
    }

    void IWorldEventPublisher.FanoutPartySpelled(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.AbnormalStatusUpdateMsg update)
    {
        var effects = update.Effects ?? [];
        var rows = new AbnormalEffectNet[effects.Length];
        for (int i = 0; i < effects.Length; i++)
            rows[i] = new AbnormalEffectNet
            {
                Id = effects[i].Id,
                Value = effects[i].Value,
                DurationSeconds = effects[i].DurationSeconds,
                Slot = effects[i].Slot ?? "",
                Visual = effects[i].Visual ?? ""
            };
        var payload = NetCodec.EncodePartySpelled(new AbnormalStatusUpdateNetMsg { ObjectId = update.ObjectId, Effects = rows });
        _presence.TryBroadcastCritical(watcherObjectIds, NetOpcode.PartySpelled, payload);
    }

    void IWorldEventPublisher.FanoutPartyMemberPosition(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PartyMemberPositionMsg positions)
    {
        var src = positions.Members ?? [];
        var rows = new PartyMemberPositionEntryNet[src.Length];
        for (int i = 0; i < src.Length; i++)
            rows[i] = new PartyMemberPositionEntryNet { ObjectId = src[i].ObjectId, X = src[i].X, Y = src[i].Y, Z = src[i].Z };
        var payload = NetCodec.EncodePartyMemberPosition(new PartyMemberPositionNetMsg { Members = rows });
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.PartyMemberPosition, payload);
    }
}
