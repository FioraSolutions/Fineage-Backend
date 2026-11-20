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

    /// <summary>Coalescing window for <c>PartyMemberUpdate</c>: at most one frame per member per window.</summary>
    internal const int PartyUpdateIntervalMs = 300;
    /// <summary>Interlude PartyMemberPosition cadence (map dots), only changed rows are sent.</summary>
    internal const int PartyPositionIntervalMs = 3000;

    readonly HashSet<int> _partyVitalsDirty = [];
    readonly Dictionary<int, (long At, PartyMemberUpdateMsg Msg)> _partyVitalsSent = [];
    readonly Dictionary<int, (int X, int Y, int Z)> _partyPositionSent = [];
    long _partyPositionNextAtMs;

    /// <summary>
    /// Mobius broadcasts PartySmallWindowUpdate on every HP/MP/CP/level change. Here the change only marks the
    /// member; <see cref="TickParty"/> coalesces it into one <c>PartyMemberUpdate</c> per window and skips no-ops.
    /// </summary>
    void FanPartyVitals(ZoneEntity subject)
    {
        if (subject.Kind == ZoneEntityKind.Player && _partyByPlayer.ContainsKey(subject.ObjectId))
            AtBarrier(() => _partyVitalsDirty.Add(subject.ObjectId));
    }

    /// <summary>Mobius PartySpelled: a member's effect list to the other members (apply / remove / expire).</summary>
    void FanPartySpelled(ZoneEntity subject)
    {
        if (NetBridge == null || subject.Kind != ZoneEntityKind.Player ||
            !_partyByPlayer.TryGetValue(subject.ObjectId, out int partyId) || !_parties.TryGetValue(partyId, out var party))
            return;
        var mates = OnlineMatesOf(party, subject.ObjectId);
        if (mates.Count > 0)
            NetBridge.FanoutPartySpelled(mates, AbnormalFor(subject));
    }

    /// <summary>On join: the newcomer gets every member's effects and the others get the newcomer's.</summary>
    void ExchangePartySpelled(ZoneParty party, int newcomerId)
    {
        if (NetBridge == null)
            return;
        foreach (int id in party.MemberIds)
        {
            if (!_byId.TryGetValue(id, out var member) || member.Session == null)
                continue;
            var watchers = id == newcomerId ? OnlineMatesOf(party, id) : (Online(newcomerId) != null ? [newcomerId] : []);
            if (watchers.Count > 0)
                NetBridge.FanoutPartySpelled(watchers, AbnormalFor(member));
        }
        // Fresh member: everyone's map dot and bars are (re)sent on the next tick.
        foreach (int id in party.MemberIds)
        {
            _partyPositionSent.Remove(id);
            _partyVitalsSent.Remove(id);
        }
        _partyPositionNextAtMs = 0;
    }

    List<int> OnlineMatesOf(ZoneParty party, int exceptId)
    {
        var list = new List<int>(party.MemberIds.Count);
        foreach (int id in party.MemberIds)
            if (id != exceptId && Online(id) != null)
                list.Add(id);
        return list;
    }

    PartyMemberUpdateMsg PartyMemberUpdateFor(ZoneEntity e)
    {
        int maxCp = PartyMaxCp(e);
        return new PartyMemberUpdateMsg
        {
            ObjectId = e.ObjectId,
            Level = e.Level,
            ClassId = e.ClassId,
            CurHp = e.CurHp,
            MaxHp = e.MaxHp,
            CurMp = e.CurMp,
            MaxMp = e.MaxMp,
            CurCp = e.MaxCp > 0 ? Math.Clamp(e.CurCp, 0, e.MaxCp) : maxCp,
            MaxCp = e.MaxCp > 0 ? e.MaxCp : maxCp
        };
    }

    int PartyMaxCp(ZoneEntity e)
    {
        if (!PlayerClasses.TryGet(e.ClassId, out var template) || template == null)
            return 0;
        try { return template.ModifiedCp(Math.Max(1, e.Level)); }
        catch (ArgumentOutOfRangeException) { return template.MaxCp; }
    }

    static bool SameBars(PartyMemberUpdateMsg a, PartyMemberUpdateMsg b) =>
        a.Level == b.Level && a.ClassId == b.ClassId && a.CurHp == b.CurHp && a.MaxHp == b.MaxHp &&
        a.CurMp == b.CurMp && a.MaxMp == b.MaxMp && a.CurCp == b.CurCp && a.MaxCp == b.MaxCp;

    /// <summary>Party bars (coalesced) + PartyMemberPosition (periodic). Runs on the combat/movement tick.</summary>
    void TickParty(long now)
    {
        if (_parties.Count == 0 || NetBridge == null)
        {
            _partyVitalsDirty.Clear();
            return;
        }

        if (_partyVitalsDirty.Count > 0)
        {
            foreach (int id in _partyVitalsDirty.ToArray())
            {
                if (!_byId.TryGetValue(id, out var e) || !_partyByPlayer.TryGetValue(id, out int partyId) ||
                    !_parties.TryGetValue(partyId, out var party))
                {
                    _partyVitalsDirty.Remove(id);
                    continue;
                }
                bool hadSent = _partyVitalsSent.TryGetValue(id, out var last);
                if (hadSent && now - last.At < PartyUpdateIntervalMs)
                    continue;   // stays dirty: flushed once the window passes
                _partyVitalsDirty.Remove(id);
                var msg = PartyMemberUpdateFor(e);
                if (hadSent && SameBars(last.Msg, msg))
                    continue;
                _partyVitalsSent[id] = (now, msg);
                var mates = OnlineMatesOf(party, id);
                if (mates.Count > 0)
                    NetBridge.FanoutPartyMemberUpdate(mates, msg);
            }
        }

        if (now < _partyPositionNextAtMs)
            return;
        _partyPositionNextAtMs = now + PartyPositionIntervalMs;
        foreach (var party in _parties.Values)
        {
            List<PartyMemberPositionEntryMsg>? changed = null;
            foreach (int id in party.MemberIds)
            {
                if (Online(id) == null || !_byId.TryGetValue(id, out var e))
                    continue;
                var pos = (e.X, e.Y, e.Z);
                if (_partyPositionSent.TryGetValue(id, out var sent) && sent == pos)
                    continue;
                _partyPositionSent[id] = pos;
                (changed ??= []).Add(new PartyMemberPositionEntryMsg { ObjectId = id, X = e.X, Y = e.Y, Z = e.Z });
            }
            if (changed == null)
                continue;
            foreach (int watcher in party.MemberIds)
            {
                if (Online(watcher) == null)
                    continue;
                var rows = changed.Where(r => r.ObjectId != watcher).ToArray();
                if (rows.Length > 0)
                    NetBridge.FanoutPartyMemberPosition([watcher], new PartyMemberPositionMsg { Members = rows });
            }
        }
    }

    /// <summary>Test hook: set a player's HP through the normal vitals fanout (party bars included).</summary>
    internal void SetPlayerHpForTest(ZoneEntity player, int hp)
    {
        lock (_gate)
        {
            player.CurHp = Math.Clamp(hp, 1, player.MaxHp);
            FanoutVitals(player, PlayerVitalsStatus(player));
        }
    }

    internal Task RunPartyTickForTest()
    {
        lock (_gate)
            TickParty(NowUnixMs());
        return Task.CompletedTask;
    }

    internal Task RunTradeRangeTickForTest()
    {
        lock (_gate)
            TickTradeRange();
        return Task.CompletedTask;
    }

    void ForgetPartyMemberCaches(int objectId)
    {
        _partyVitalsDirty.Remove(objectId);
        _partyVitalsSent.Remove(objectId);
        _partyPositionSent.Remove(objectId);
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
        ExchangePartySpelled(party, invitee.ObjectId);
        return null;
    }

}
