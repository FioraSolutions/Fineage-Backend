using Fineage.Protocol;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.Party;

internal sealed class ZoneParty
{
    public required int PartyId { get; init; }
    public int LeaderObjectId { get; set; }
    public int ItemDistribution { get; set; }
    /// <summary>Mobius <c>Party._itemLastLoot</c> cursor for BY_TURN* modes.</summary>
    public int ItemLastLoot { get; set; }
    /// <summary>The leader first (Mobius keeps it at index 0; the party window draws rows in this order).</summary>
    public List<int> MemberIds { get; } = [];
}

internal readonly record struct PendingPartyInvite(int InviterObjectId, int ItemDistribution);

/// <summary>What the party actor reads of a member when it steps; the world answers from the member's entity.</summary>
internal interface IPartyMembers
{
    bool IsOnline(int objectId);

    /// <summary>The member's bars, or null when it is not in the world.</summary>
    PartyMemberUpdateMsg? Bars(int objectId);

    /// <summary>The member's position, or null when it is offline.</summary>
    (int X, int Y, int Z)? Position(int objectId);
}

/// <summary>A party frame for <see cref="To"/>, in the order the actor produced it.</summary>
internal abstract record PartyFrame(IReadOnlyList<int> To);

internal sealed record PartyBarsFrame(IReadOnlyList<int> To, PartyMemberUpdateMsg Bars) : PartyFrame(To);

internal sealed record PartyPositionsFrame(IReadOnlyList<int> To, PartyMemberPositionMsg Positions) : PartyFrame(To);

/// <summary>
/// Parties as a social actor keyed by party id: the roster, pending invites and the pacing of the members' bars and
/// map dots. Membership changes are requests from commands; a member whose vitals changed is reported with
/// <see cref="Changed"/> (region bodies report it at the barrier). The step coalesces the reports into at most one
/// bars frame per member per <see cref="UpdateIntervalMs"/> and sends the changed map dots every
/// <see cref="PositionIntervalMs"/>, to the online members whichever region they are in. Members and parties are
/// visited in ascending id order.
/// </summary>
internal sealed class PartyActor : GlobalActor<PartyFrame>
{
    public const string StageName = "party";

    /// <summary>Coalescing window for <c>PartyMemberUpdate</c>: at most one frame per member per window.</summary>
    public const int UpdateIntervalMs = 300;

    /// <summary>Interlude PartyMemberPosition cadence (map dots); only changed rows are sent.</summary>
    public const int PositionIntervalMs = 3000;

    readonly IPartyMembers _members;
    readonly SortedDictionary<int, ZoneParty> _parties = [];
    readonly Dictionary<int, int> _byPlayer = [];
    readonly Dictionary<int, PendingPartyInvite> _invites = [];
    readonly SortedSet<int> _changed = [];
    readonly Dictionary<int, (long At, PartyMemberUpdateMsg Msg)> _barsSent = [];
    readonly Dictionary<int, (int X, int Y, int Z)> _positionSent = [];
    int _nextPartyId = 1;
    long _positionNextAtMs;

    public PartyActor(IPartyMembers members)
        : base(StageName, ActorTime.Simulation)
    {
        _members = members ?? throw new ArgumentNullException(nameof(members));
    }

    public int Count
    {
        get
        {
            RequireActorThread();
            return _parties.Count;
        }
    }

    public ZoneParty? Of(int objectId)
    {
        RequireActorThread();
        return _byPlayer.TryGetValue(objectId, out int partyId) && _parties.TryGetValue(partyId, out var party) ? party : null;
    }

    public bool InParty(int objectId)
    {
        RequireActorThread();
        return _byPlayer.ContainsKey(objectId);
    }

    public bool SameParty(int a, int b)
    {
        RequireActorThread();
        return _byPlayer.TryGetValue(a, out int pa) && _byPlayer.TryGetValue(b, out int pb) && pa == pb;
    }

    /// <summary>A new party led by <paramref name="leaderObjectId"/>, its only member.</summary>
    public ZoneParty Create(int leaderObjectId, int itemDistribution)
    {
        RequireActorThread();
        if (_byPlayer.ContainsKey(leaderObjectId))
            throw new InvalidOperationException($"player {leaderObjectId} already is in party {_byPlayer[leaderObjectId]}");
        var party = new ZoneParty
        {
            PartyId = _nextPartyId++,
            LeaderObjectId = leaderObjectId,
            ItemDistribution = itemDistribution
        };
        party.MemberIds.Add(leaderObjectId);
        _parties[party.PartyId] = party;
        _byPlayer[leaderObjectId] = party.PartyId;
        return party;
    }

    public void Join(ZoneParty party, int objectId)
    {
        RequireActorThread();
        if (!_parties.TryGetValue(party.PartyId, out var known) || !ReferenceEquals(known, party))
            throw new InvalidOperationException($"party {party.PartyId} is not on the roster");
        if (_byPlayer.ContainsKey(objectId))
            throw new InvalidOperationException($"player {objectId} already is in party {_byPlayer[objectId]}");
        party.MemberIds.Add(objectId);
        _byPlayer[objectId] = party.PartyId;
    }

    /// <summary>Takes the member out of its party; the party is dropped from the roster once it has no members.</summary>
    public void Remove(ZoneParty party, int objectId)
    {
        RequireActorThread();
        party.MemberIds.Remove(objectId);
        _byPlayer.Remove(objectId);
        Forget(objectId);
        if (party.MemberIds.Count == 0)
            _parties.Remove(party.PartyId);
    }

    /// <summary>A player whose party is gone from the roster: only the stale index entry is dropped.</summary>
    public void Unlink(int objectId)
    {
        RequireActorThread();
        _byPlayer.Remove(objectId);
    }

    public bool TryGetInvite(int inviteeObjectId, out PendingPartyInvite invite)
    {
        RequireActorThread();
        return _invites.TryGetValue(inviteeObjectId, out invite);
    }

    public bool IsInvited(int inviteeObjectId)
    {
        RequireActorThread();
        return _invites.ContainsKey(inviteeObjectId);
    }

    public void Invite(int inviteeObjectId, PendingPartyInvite invite)
    {
        RequireActorThread();
        _invites[inviteeObjectId] = invite;
    }

    public void DropInvite(int inviteeObjectId)
    {
        RequireActorThread();
        _invites.Remove(inviteeObjectId);
    }

    /// <summary>The player has a party invite pending, received or sent.</summary>
    public bool InInvite(int objectId)
    {
        RequireActorThread();
        return _invites.ContainsKey(objectId) || _invites.Values.Any(i => i.InviterObjectId == objectId);
    }

    /// <summary>Drops the invites the player received and sent.</summary>
    public void DropInvitesOf(int objectId)
    {
        RequireActorThread();
        _invites.Remove(objectId);
        foreach (var (inviteeId, pending) in _invites.ToList())
        {
            if (pending.InviterObjectId == objectId)
                _invites.Remove(inviteeId);
        }
    }

    /// <summary>The member's vitals changed; its bars go out at the next step once its window passed.</summary>
    public void Changed(int objectId)
    {
        RequireActorThread();
        if (_byPlayer.ContainsKey(objectId))
            _changed.Add(objectId);
    }

    /// <summary>Discards pending bar reports (no network to send them to).</summary>
    public void DiscardChanges()
    {
        RequireActorThread();
        _changed.Clear();
    }

    /// <summary>A member joined: every member's bars and map dot go out again at the next step.</summary>
    public void Resync(ZoneParty party)
    {
        RequireActorThread();
        foreach (int id in party.MemberIds)
        {
            _positionSent.Remove(id);
            _barsSent.Remove(id);
        }

        _positionNextAtMs = 0;
    }

    public List<int> OnlineMatesOf(ZoneParty party, int exceptObjectId)
    {
        RequireActorThread();
        var list = new List<int>(party.MemberIds.Count);
        foreach (int id in party.MemberIds)
        {
            if (id != exceptObjectId && _members.IsOnline(id))
                list.Add(id);
        }

        return list;
    }

    protected override void OnStep(long nowUnixMs, List<PartyFrame> effects)
    {
        if (_parties.Count == 0)
        {
            _changed.Clear();
            return;
        }

        StepBars(nowUnixMs, effects);
        if (nowUnixMs < _positionNextAtMs)
            return;
        _positionNextAtMs = nowUnixMs + PositionIntervalMs;
        StepPositions(effects);
    }

    void StepBars(long nowUnixMs, List<PartyFrame> effects)
    {
        foreach (int id in _changed.ToArray())
        {
            var party = Of(id);
            var bars = party == null ? null : _members.Bars(id);
            if (party == null || bars == null)
            {
                _changed.Remove(id);
                continue;
            }

            bool hadSent = _barsSent.TryGetValue(id, out var last);
            if (hadSent && nowUnixMs - last.At < UpdateIntervalMs)
                continue;
            _changed.Remove(id);
            if (hadSent && SameBars(last.Msg, bars))
                continue;
            _barsSent[id] = (nowUnixMs, bars);
            var mates = OnlineMatesOf(party, id);
            if (mates.Count > 0)
                effects.Add(new PartyBarsFrame(mates, bars));
        }
    }

    void StepPositions(List<PartyFrame> effects)
    {
        foreach (var party in _parties.Values)
        {
            List<PartyMemberPositionEntryMsg>? changed = null;
            foreach (int id in party.MemberIds)
            {
                if (_members.Position(id) is not { } pos)
                    continue;
                if (_positionSent.TryGetValue(id, out var sent) && sent == pos)
                    continue;
                _positionSent[id] = pos;
                (changed ??= []).Add(new PartyMemberPositionEntryMsg { ObjectId = id, X = pos.X, Y = pos.Y, Z = pos.Z });
            }

            if (changed == null)
                continue;
            foreach (int watcher in party.MemberIds)
            {
                if (!_members.IsOnline(watcher))
                    continue;
                var rows = changed.Where(r => r.ObjectId != watcher).ToArray();
                if (rows.Length > 0)
                    effects.Add(new PartyPositionsFrame([watcher], new PartyMemberPositionMsg { Members = rows }));
            }
        }
    }

    void Forget(int objectId)
    {
        _changed.Remove(objectId);
        _barsSent.Remove(objectId);
        _positionSent.Remove(objectId);
    }

    static bool SameBars(PartyMemberUpdateMsg a, PartyMemberUpdateMsg b) =>
        a.Level == b.Level && a.ClassId == b.ClassId && a.CurHp == b.CurHp && a.MaxHp == b.MaxHp &&
        a.CurMp == b.CurMp && a.MaxMp == b.MaxMp && a.CurCp == b.CurCp && a.MaxCp == b.MaxCp;
}
