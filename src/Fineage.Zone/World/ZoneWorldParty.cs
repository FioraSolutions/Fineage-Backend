using Fineage.Protocol;
using Fineage.Zone.Party;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public const int MaxPartyMembers = PartyRules.MaxMembers;

    readonly Dictionary<int, ZoneParty> _parties = [];
    readonly Dictionary<int, int> _partyByPlayer = [];
    readonly Dictionary<int, PendingPartyInvite> _pendingInvites = [];
    int _nextPartyId = 1;

    sealed class ZoneParty
    {
        public required int PartyId { get; init; }
        public int LeaderObjectId { get; set; }
        public int ItemDistribution { get; set; }
        /// <summary>Mobius <c>Party._itemLastLoot</c> cursor for BY_TURN* modes.</summary>
        public int ItemLastLoot { get; set; }
        public List<int> MemberIds { get; } = [];
    }

    readonly record struct PendingPartyInvite(int InviterObjectId, int ItemDistribution);

    public Task<string?> TryPartyInviteAsync(
        ZoneSession session, int targetObjectId, int itemDistribution, CancellationToken ct)
    {
        if (!session.InWorld)
            return Task.FromResult<string?>("world");
        if (targetObjectId == session.ObjectId)
            return Task.FromResult<string?>("bad_request");
        if (!Fineage.Game.Services.PartyLootRules.IsValidDistribution(itemDistribution))
            return Task.FromResult<string?>("bad_request");

        lock (_gate)
            return Task.FromResult<string?>(TryPartyInvite(session, targetObjectId, itemDistribution));
    }

    public Task<string?> TryPartyAnswerAsync(
        ZoneSession session, int requestorObjectId, int response, CancellationToken ct)
    {
        if (!session.InWorld)
            return Task.FromResult<string?>("world");

        lock (_gate)
            return Task.FromResult<string?>(TryPartyAnswer(session, requestorObjectId, response));
    }

    public Task<string?> TryPartyLeaveAsync(ZoneSession session, CancellationToken ct)
    {
        if (!session.InWorld)
            return Task.FromResult<string?>("world");

        lock (_gate)
            return Task.FromResult<string?>(TryPartyLeave(session.ObjectId));
    }

    public Task<string?> TryPartyKickAsync(ZoneSession session, int targetObjectId, CancellationToken ct)
    {
        if (!session.InWorld)
            return Task.FromResult<string?>("world");

        lock (_gate)
            return Task.FromResult<string?>(TryPartyKick(session, targetObjectId));
    }

    public Task<string?> TryPartyChangeLeaderAsync(ZoneSession session, int targetObjectId, CancellationToken ct)
    {
        if (!session.InWorld)
            return Task.FromResult<string?>("world");

        lock (_gate)
            return Task.FromResult<string?>(TryPartyChangeLeader(session, targetObjectId));
    }

    /// <summary>
    /// Mobius RequestChangePartyLeader → Party.changePartyLeader: only the leader, only to another online member;
    /// the whole party gets the reordered roster.
    /// </summary>
    string? TryPartyChangeLeader(ZoneSession leader, int targetObjectId)
    {
        if (!IsLive(leader))
            return "world";
        if (!_partyByPlayer.TryGetValue(leader.ObjectId, out int partyId) ||
            !_parties.TryGetValue(partyId, out var party))
            return "party";
        if (party.LeaderObjectId != leader.ObjectId)
            return "party_leader";
        if (targetObjectId == leader.ObjectId ||
            !_partyByPlayer.TryGetValue(targetObjectId, out int targetPartyId) || targetPartyId != partyId ||
            Online(targetObjectId) == null)
            return "target";

        party.LeaderObjectId = targetObjectId;
        // Mobius keeps the leader at index 0 of the member list (the party window draws rows in that order).
        party.MemberIds.Remove(targetObjectId);
        party.MemberIds.Insert(0, targetObjectId);
        FanPartyList(party);
        return null;
    }

    /// <summary>
    /// Mobius broadcasts PartySmallWindowUpdate to the party on every HP/MP change. Net has no small-window
    /// update frame yet, so the roster (which carries HP/MP) is re-sent; the client already redraws from it.
    /// </summary>
    void FanPartyVitals(ZoneEntity subject)
    {
        if (subject.Kind != ZoneEntityKind.Player || NetBridge == null ||
            !_partyByPlayer.TryGetValue(subject.ObjectId, out int partyId) ||
            !_parties.TryGetValue(partyId, out var party))
            return;
        FanPartyList(party);
    }

    void PartyPlayerLeftWorld(int objectId)
    {
        _pendingInvites.Remove(objectId);
        foreach (var (inviteeId, pending) in _pendingInvites.ToList())
        {
            if (pending.InviterObjectId == objectId)
                _pendingInvites.Remove(inviteeId);
        }

        if (!_partyByPlayer.ContainsKey(objectId))
            return;

        TryPartyLeave(objectId);
    }

    string? TryPartyInvite(ZoneSession inviter, int targetObjectId, int itemDistribution)
    {
        if (!IsLive(inviter))
            return "world";
        if (!_byId.TryGetValue(targetObjectId, out var targetEntity) ||
            targetEntity.Kind != ZoneEntityKind.Player ||
            targetEntity.Session == null ||
            !targetEntity.Session.InWorld)
            return "target";

        if (_partyByPlayer.ContainsKey(targetObjectId))
            return "party_busy";
        if (_pendingInvites.ContainsKey(targetObjectId))
            return "party_busy";

        int distribution = itemDistribution;
        if (_partyByPlayer.TryGetValue(inviter.ObjectId, out int partyId))
        {
            if (!_parties.TryGetValue(partyId, out var party))
                return "party";
            if (party.LeaderObjectId != inviter.ObjectId)
                return "party_leader";
            if (PartyRules.IsFull(party.MemberIds.Count))
                return "party_full";
            distribution = party.ItemDistribution;
        }

        _pendingInvites[targetObjectId] = new PendingPartyInvite(inviter.ObjectId, distribution);
        FanPartyAsk([targetEntity.ObjectId], new PartyAskJoinMsg
        {
            RequestorObjectId = inviter.ObjectId,
            RequestorName = inviter.Name,
            ItemDistribution = distribution
        });
        return null;
    }

    string? TryPartyAnswer(ZoneSession invitee, int requestorObjectId, int response)
    {
        if (!IsLive(invitee))
            return "world";
        if (!_pendingInvites.TryGetValue(invitee.ObjectId, out var pending) ||
            pending.InviterObjectId != requestorObjectId)
            return "party_invite";

        _pendingInvites.Remove(invitee.ObjectId);

        if (!_byId.TryGetValue(requestorObjectId, out var requestorEntity) ||
            requestorEntity.Session == null ||
            !IsLive(requestorEntity.Session))
        {
            return "party_invite";
        }

        if (response != 1)
        {
            FanPartyJoinResult([requestorEntity.ObjectId], 0);
            return null;
        }

        if (_partyByPlayer.ContainsKey(invitee.ObjectId))
            return "party_busy";

        ZoneParty party;
        if (_partyByPlayer.TryGetValue(requestorObjectId, out int partyId))
        {
            if (!_parties.TryGetValue(partyId, out party!))
                return "party";
            if (PartyRules.IsFull(party.MemberIds.Count))
            {
                FanPartyJoinResult([requestorEntity.ObjectId], 0);
                return null;
            }
        }
        else
        {
            party = new ZoneParty
            {
                PartyId = _nextPartyId++,
                LeaderObjectId = requestorObjectId,
                ItemDistribution = pending.ItemDistribution
            };
            party.MemberIds.Add(requestorObjectId);
            _parties[party.PartyId] = party;
            _partyByPlayer[requestorObjectId] = party.PartyId;
        }

        party.MemberIds.Add(invitee.ObjectId);
        _partyByPlayer[invitee.ObjectId] = party.PartyId;

        FanPartyJoinResult([requestorEntity.ObjectId], 1);
        FanPartyList(party);
        return null;
    }

}
