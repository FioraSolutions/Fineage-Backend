using Fineage.Protocol;
using MessagePack;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public const int MaxPartyMembers = 9;

    readonly Dictionary<int, ZoneParty> _parties = [];
    readonly Dictionary<int, int> _partyByPlayer = [];
    readonly Dictionary<int, PendingPartyInvite> _pendingInvites = [];
    int _nextPartyId = 1;

    sealed class ZoneParty
    {
        public required int PartyId { get; init; }
        public int LeaderObjectId { get; set; }
        public int ItemDistribution { get; set; }
        public List<int> MemberIds { get; } = [];
    }

    readonly record struct PendingPartyInvite(int InviterObjectId, int ItemDistribution);

    public async Task<string?> TryPartyInviteAsync(ZoneSession session, PartyInviteRequestMsg invite, CancellationToken ct)
    {
        if (invite == null)
            return "bad_request";
        if (!session.InWorld)
            return "world";
        if (invite.TargetObjectId == session.ObjectId)
            return "bad_request";
        if (invite.ItemDistribution is < 0 or > 3)
            return "bad_request";

        var outbound = new List<Pending>();
        string? error;
        lock (_gate)
            error = TryPartyInvite(session, invite, outbound);
        if (error != null)
            return error;

        await FlushPartyAsync(outbound, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> TryPartyAnswerAsync(ZoneSession session, PartyAnswerJoinRequestMsg answer, CancellationToken ct)
    {
        if (answer == null)
            return "bad_request";
        if (!session.InWorld)
            return "world";

        var outbound = new List<Pending>();
        string? error;
        lock (_gate)
            error = TryPartyAnswer(session, answer, outbound);
        if (error != null)
            return error;

        await FlushPartyAsync(outbound, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> TryPartyLeaveAsync(ZoneSession session, CancellationToken ct)
    {
        if (!session.InWorld)
            return "world";

        var outbound = new List<Pending>();
        string? error;
        lock (_gate)
            error = TryPartyLeave(session.ObjectId, outbound);
        if (error != null)
            return error;

        await FlushPartyAsync(outbound, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> TryPartyKickAsync(ZoneSession session, PartyKickRequestMsg kick, CancellationToken ct)
    {
        if (kick == null)
            return "bad_request";
        if (!session.InWorld)
            return "world";

        var outbound = new List<Pending>();
        string? error;
        lock (_gate)
            error = TryPartyKick(session, kick.TargetObjectId, outbound);
        if (error != null)
            return error;

        await FlushPartyAsync(outbound, ct).ConfigureAwait(false);
        return null;
    }

    void PartyPlayerLeftWorld(int objectId, List<Pending> outbound)
    {
        _pendingInvites.Remove(objectId);
        foreach (var (inviteeId, pending) in _pendingInvites.ToList())
        {
            if (pending.InviterObjectId == objectId)
                _pendingInvites.Remove(inviteeId);
        }

        if (!_partyByPlayer.ContainsKey(objectId))
            return;

        TryPartyLeave(objectId, outbound);
    }

    string? TryPartyInvite(ZoneSession inviter, PartyInviteRequestMsg invite, List<Pending> outbound)
    {
        if (!IsLive(inviter))
            return "world";
        if (!_byId.TryGetValue(invite.TargetObjectId, out var targetEntity) ||
            targetEntity.Kind != ZoneEntityKind.Player ||
            targetEntity.Session == null ||
            !targetEntity.Session.InWorld)
            return "target";

        if (_partyByPlayer.ContainsKey(invite.TargetObjectId))
            return "party_busy";
        if (_pendingInvites.ContainsKey(invite.TargetObjectId))
            return "party_busy";

        int distribution = invite.ItemDistribution;
        if (_partyByPlayer.TryGetValue(inviter.ObjectId, out int partyId))
        {
            if (!_parties.TryGetValue(partyId, out var party))
                return "party";
            if (party.LeaderObjectId != inviter.ObjectId)
                return "party_leader";
            if (party.MemberIds.Count >= MaxPartyMembers)
                return "party_full";
            distribution = party.ItemDistribution;
        }

        _pendingInvites[invite.TargetObjectId] = new PendingPartyInvite(inviter.ObjectId, distribution);
        var ask = new PartyAskJoinMsg
        {
            RequestorObjectId = inviter.ObjectId,
            RequestorName = inviter.Name,
            ItemDistribution = distribution
        };
        outbound.Add(Pack(targetEntity.Session, MsgType.PartyAskJoin, ask));
        return null;
    }

    string? TryPartyAnswer(ZoneSession invitee, PartyAnswerJoinRequestMsg answer, List<Pending> outbound)
    {
        if (!IsLive(invitee))
            return "world";
        if (!_pendingInvites.TryGetValue(invitee.ObjectId, out var pending) ||
            pending.InviterObjectId != answer.RequestorObjectId)
            return "party_invite";

        _pendingInvites.Remove(invitee.ObjectId);

        if (!_byId.TryGetValue(answer.RequestorObjectId, out var requestorEntity) ||
            requestorEntity.Session == null ||
            !IsLive(requestorEntity.Session))
        {
            return "party_invite";
        }

        if (answer.Response != 1)
        {
            outbound.Add(Pack(requestorEntity.Session, MsgType.PartyJoinResult, new PartyJoinResultMsg { Response = 0 }));
            return null;
        }

        if (_partyByPlayer.ContainsKey(invitee.ObjectId))
            return "party_busy";

        ZoneParty party;
        if (_partyByPlayer.TryGetValue(answer.RequestorObjectId, out int partyId))
        {
            if (!_parties.TryGetValue(partyId, out party!))
                return "party";
            if (party.MemberIds.Count >= MaxPartyMembers)
            {
                outbound.Add(Pack(requestorEntity.Session, MsgType.PartyJoinResult, new PartyJoinResultMsg { Response = 0 }));
                return null;
            }
        }
        else
        {
            party = new ZoneParty
            {
                PartyId = _nextPartyId++,
                LeaderObjectId = answer.RequestorObjectId,
                ItemDistribution = pending.ItemDistribution
            };
            party.MemberIds.Add(answer.RequestorObjectId);
            _parties[party.PartyId] = party;
            _partyByPlayer[answer.RequestorObjectId] = party.PartyId;
        }

        party.MemberIds.Add(invitee.ObjectId);
        _partyByPlayer[invitee.ObjectId] = party.PartyId;

        outbound.Add(Pack(requestorEntity.Session, MsgType.PartyJoinResult, new PartyJoinResultMsg { Response = 1 }));
        FanoutPartyList(party, outbound);
        return null;
    }

    string? TryPartyLeave(int objectId, List<Pending> outbound)
    {
        if (!_partyByPlayer.TryGetValue(objectId, out int partyId) ||
            !_parties.TryGetValue(partyId, out var party))
        {
            _partyByPlayer.Remove(objectId);
            return "party";
        }

        party.MemberIds.Remove(objectId);
        _partyByPlayer.Remove(objectId);

        if (party.MemberIds.Count == 0)
        {
            _parties.Remove(partyId);
            return null;
        }

        if (party.LeaderObjectId == objectId)
            party.LeaderObjectId = party.MemberIds[0];

        if (party.MemberIds.Count == 1)
        {
            int lastId = party.MemberIds[0];
            party.MemberIds.RemoveAt(0);
            _partyByPlayer.Remove(lastId);
            _parties.Remove(partyId);
            if (_byId.TryGetValue(lastId, out var lastEntity) && lastEntity.Session != null)
            {
                outbound.Add(Pack(lastEntity.Session, MsgType.PartyMemberList, new PartyMemberListMsg
                {
                    LeaderObjectId = 0,
                    PartyId = 0,
                    Members = []
                }));
            }

            return null;
        }

        FanoutPartyList(party, outbound);
        return null;
    }

    string? TryPartyKick(ZoneSession leader, int targetObjectId, List<Pending> outbound)
    {
        if (!IsLive(leader))
            return "world";
        if (!_partyByPlayer.TryGetValue(leader.ObjectId, out int partyId) ||
            !_parties.TryGetValue(partyId, out var party))
            return "party";
        if (party.LeaderObjectId != leader.ObjectId)
            return "party_leader";
        if (!_partyByPlayer.TryGetValue(targetObjectId, out int targetPartyId) || targetPartyId != partyId)
            return "target";

        return TryPartyLeave(targetObjectId, outbound);
    }

    void FanoutPartyList(ZoneParty party, List<Pending> outbound)
    {
        var list = BuildPartyMemberList(party);
        foreach (int memberId in party.MemberIds)
        {
            if (!_byId.TryGetValue(memberId, out var entity) || entity.Session == null)
                continue;
            outbound.Add(Pack(entity.Session, MsgType.PartyMemberList, list));
        }
    }

    PartyMemberListMsg BuildPartyMemberList(ZoneParty party)
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
                MaxMp = entity.MaxMp
            };
        }

        return new PartyMemberListMsg
        {
            LeaderObjectId = party.LeaderObjectId,
            PartyId = party.PartyId,
            Members = members
        };
    }

    static async Task FlushPartyAsync(List<Pending> messages, CancellationToken ct)
    {
        foreach (var message in messages)
            await TrySendAsync(message, ct).ConfigureAwait(false);
    }
}
