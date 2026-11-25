using Fineage.Protocol;
using Fineage.Zone.Party;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    string? TryPartyLeave(int objectId)
    {
        if (_party.Of(objectId) is not { } party)
        {
            _party.Unlink(objectId);
            return "party";
        }

        bool loneMember = party.MemberIds.Count == 1;

        RiftOnPartyLeave(objectId);
        _party.Remove(party, objectId);
        DuelOnPartyRemove(objectId);

        if (party.MemberIds.Count == 0)
        {
            if (loneMember)
                CommandChannelOnPartyEmpty(party);
            return null;
        }

        if (party.LeaderObjectId == objectId)
            party.LeaderObjectId = party.MemberIds[0];

        if (party.MemberIds.Count == 1)
        {
            CommandChannelOnPartyShrunkToOne(party);
            int lastId = party.MemberIds[0];
            _party.Remove(party, lastId);
            DuelOnPartyRemove(lastId);
            // Mobius sends an empty list so the last member's window closes.
            FanPartyList([lastId], 0, 0, []);
            return null;
        }

        FanPartyList(party);
        return null;
    }

    string? TryPartyKick(ZoneSession leader, int targetObjectId)
    {
        if (!IsLive(leader))
            return "world";
        if (_party.Of(leader.ObjectId) is not { } party)
            return "party";
        if (party.LeaderObjectId != leader.ObjectId)
            return "party_leader";
        if (!_party.SameParty(leader.ObjectId, targetObjectId))
            return "target";

        return TryPartyLeave(targetObjectId);
    }

    void FanPartyList(ZoneParty party) =>
        FanPartyList(
            party.MemberIds.Where(id => Online(id) != null).ToArray(),
            party.LeaderObjectId,
            party.PartyId,
            BuildPartyRows(party));

    void FanPartyList(IReadOnlyList<int> members, int leaderObjectId, int partyId, PartyMemberEntryMsg[] rows) =>
        NetBridge?.FanoutPartyMemberList(members, new PartyMemberListMsg
        {
            LeaderObjectId = leaderObjectId,
            PartyId = partyId,
            Members = rows
        });

    void FanPartyJoinResult(IReadOnlyList<int> watchers, int response) =>
        NetBridge?.FanoutPartyJoinResult(watchers, new PartyJoinResultMsg { Response = response });

    void FanPartyAsk(IReadOnlyList<int> watchers, PartyAskJoinMsg ask) =>
        NetBridge?.FanoutPartyAskJoin(watchers, ask);

    PartyMemberEntryMsg[] BuildPartyRows(ZoneParty party)
    {
        var members = new PartyMemberEntryMsg[party.MemberIds.Count];
        for (int i = 0; i < party.MemberIds.Count; i++)
        {
            int id = party.MemberIds[i];
            if (!_byId.TryGetValue(id, out var entity))
            {
                members[i] = new PartyMemberEntryMsg { ObjectId = id };
                continue;
            }

            members[i] = new PartyMemberEntryMsg
            {
                ObjectId = entity.ObjectId,
                Name = entity.Name,
                ClassId = entity.ClassId,
                Level = entity.Level,
                CurHp = entity.CurHp,
                MaxHp = entity.MaxHp,
                CurMp = entity.CurMp,
                MaxMp = entity.MaxMp,
                CurCp = entity.MaxCp > 0 ? Math.Clamp(entity.CurCp, 0, entity.MaxCp) : PartyMaxCp(entity),
                MaxCp = entity.MaxCp > 0 ? entity.MaxCp : PartyMaxCp(entity)
            };
        }

        return members;
    }
}
