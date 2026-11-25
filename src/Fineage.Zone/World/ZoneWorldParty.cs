using Fineage.Protocol;
using Fineage.Zone.Party;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public const int MaxPartyMembers = PartyRules.MaxMembers;

    readonly PartyActor _party;

    /// <summary>The party actor's view of the members: their entities, read on the scheduler thread.</summary>
    sealed class PartyMembers(ZoneWorld world) : IPartyMembers
    {
        public bool IsOnline(int objectId) => world.Online(objectId) != null;

        public PartyMemberUpdateMsg? Bars(int objectId) =>
            world._byId.TryGetValue(objectId, out var e) ? world.PartyMemberUpdateFor(e) : null;

        public (int X, int Y, int Z)? Position(int objectId) =>
            world.Online(objectId) != null && world._byId.TryGetValue(objectId, out var e) ? (e.X, e.Y, e.Z) : null;
    }

    public Task<string?> TryPartyInviteAsync(
        ZoneSession session, int targetObjectId, int itemDistribution, CancellationToken ct)
    {
        if (!session.InWorld)
            return Task.FromResult<string?>("world");
        if (targetObjectId == session.ObjectId)
            return Task.FromResult<string?>("bad_request");
        if (!Fineage.Game.Services.PartyLootRules.IsValidDistribution(itemDistribution))
            return Task.FromResult<string?>("bad_request");

        return Task.FromResult<string?>(TryPartyInvite(session, targetObjectId, itemDistribution));
    }

    public Task<string?> TryPartyAnswerAsync(
        ZoneSession session, int requestorObjectId, int response, CancellationToken ct)
    {
        if (!session.InWorld)
            return Task.FromResult<string?>("world");

        return Task.FromResult<string?>(TryPartyAnswer(session, requestorObjectId, response));
    }

    public Task<string?> TryPartyLeaveAsync(ZoneSession session, CancellationToken ct)
    {
        if (!session.InWorld)
            return Task.FromResult<string?>("world");

        return Task.FromResult<string?>(TryPartyLeave(session.ObjectId));
    }

    public Task<string?> TryPartyKickAsync(ZoneSession session, int targetObjectId, CancellationToken ct)
    {
        if (!session.InWorld)
            return Task.FromResult<string?>("world");

        return Task.FromResult<string?>(TryPartyKick(session, targetObjectId));
    }

    public Task<string?> TryPartyChangeLeaderAsync(ZoneSession session, int targetObjectId, CancellationToken ct)
    {
        if (!session.InWorld)
            return Task.FromResult<string?>("world");

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
        if (_party.Of(leader.ObjectId) is not { } party)
            return "party";
        if (party.LeaderObjectId != leader.ObjectId)
            return "party_leader";
        if (targetObjectId == leader.ObjectId || !_party.SameParty(leader.ObjectId, targetObjectId) ||
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
    /// Mobius broadcasts PartySmallWindowUpdate on every HP/MP/CP/level change. Here the change is reported to the
    /// party actor, which coalesces it into one <c>PartyMemberUpdate</c> per window and skips no-ops.
    /// </summary>
    void FanPartyVitals(ZoneEntity subject)
    {
        if (subject.Kind == ZoneEntityKind.Player)
            AtBarrier(() => _party.Changed(subject.ObjectId));
    }

    /// <summary>Mobius PartySpelled: a member's effect list to the other members (apply / remove / expire).</summary>
    void FanPartySpelled(ZoneEntity subject)
    {
        if (NetBridge == null || subject.Kind != ZoneEntityKind.Player)
            return;
        var update = AbnormalFor(subject);
        AtBarrier(() =>
        {
            if (_party.Of(subject.ObjectId) is not { } party)
                return;
            var mates = _party.OnlineMatesOf(party, subject.ObjectId);
            if (mates.Count > 0)
                NetBridge?.FanoutPartySpelled(mates, update);
        });
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
            var watchers = id == newcomerId ? _party.OnlineMatesOf(party, id) : (Online(newcomerId) != null ? [newcomerId] : []);
            if (watchers.Count > 0)
                NetBridge.FanoutPartySpelled(watchers, AbnormalFor(member));
        }
        // Fresh member: everyone's map dot and bars are (re)sent on the next tick.
        _party.Resync(party);
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
        catch (ArgumentOutOfRangeException ex)
        {
            ZoneLog.Warn($"party CP formula out of range for class {e.ClassId} level {e.Level}: {ex.Message}; using max CP");
            return template.MaxCp;
        }
    }

    /// <summary>Party bars (coalesced) and PartyMemberPosition (periodic) the party actor produced this step.</summary>
    void FanParty(IReadOnlyList<PartyFrame> frames)
    {
        foreach (var frame in frames)
        {
            switch (frame)
            {
                case PartyBarsFrame bars:
                    NetBridge?.FanoutPartyMemberUpdate(bars.To, bars.Bars);
                    break;
                case PartyPositionsFrame positions:
                    NetBridge?.FanoutPartyMemberPosition(positions.To, positions.Positions);
                    break;
            }
        }
    }

    void TickParty(TickContext tick)
    {
        if (NetBridge == null)
            _party.DiscardChanges();
        else
            FanParty(_party.Step(tick));
    }

    /// <summary>Test hook: set a player's HP through the normal vitals fanout (party bars included).</summary>
    internal void SetPlayerHpForTest(ZoneEntity player, int hp)
    {
        using (_affinity.Step())
        {
            player.CurHp = Math.Clamp(hp, 1, player.MaxHp);
            FanoutVitals(player, PlayerVitalsStatus(player));
        }
    }

    internal Task RunPartyTickForTest()
    {
        using (_affinity.Step())
            AtHookTick(tick =>
            {
                TickParty(tick);
                return 0;
            });
        return Task.CompletedTask;
    }

    internal Task RunTradeRangeTickForTest()
    {
        using (_affinity.Step())
            TickTradeRange();
        return Task.CompletedTask;
    }

    void PartyPlayerLeftWorld(int objectId)
    {
        CommandChannelPlayerLeft(objectId);
        _party.DropInvitesOf(objectId);
        if (!_party.InParty(objectId))
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

        if (_party.InParty(targetObjectId))
            return "party_busy";
        if (_party.IsInvited(targetObjectId))
            return "party_busy";
        if (_party.Of(inviter.ObjectId) is { } riftParty && RiftBoard.PartyInRift(riftParty.PartyId))
            return "party_busy";
        if (_party.Of(targetObjectId) is { } targetParty && RiftBoard.PartyInRift(targetParty.PartyId))
            return "party_busy";

        int distribution = itemDistribution;
        if (_party.InParty(inviter.ObjectId))
        {
            if (_party.Of(inviter.ObjectId) is not { } party)
                return "party";
            if (party.LeaderObjectId != inviter.ObjectId)
                return "party_leader";
            if (PartyRules.IsFull(party.MemberIds.Count))
                return "party_full";
            distribution = party.ItemDistribution;
        }

        _party.Invite(targetObjectId, new PendingPartyInvite(inviter.ObjectId, distribution));
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
        if (!_party.TryGetInvite(invitee.ObjectId, out var pending) ||
            pending.InviterObjectId != requestorObjectId)
            return "party_invite";

        _party.DropInvite(invitee.ObjectId);

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

        if (_party.InParty(invitee.ObjectId))
            return "party_busy";

        ZoneParty party;
        if (_party.InParty(requestorObjectId))
        {
            if (_party.Of(requestorObjectId) is not { } existing)
                return "party";
            party = existing;
            if (PartyRules.IsFull(party.MemberIds.Count))
            {
                FanPartyJoinResult([requestorEntity.ObjectId], 0);
                return null;
            }
        }
        else
        {
            party = _party.Create(requestorObjectId, pending.ItemDistribution);
        }

        _party.Join(party, invitee.ObjectId);

        FanPartyJoinResult([requestorEntity.ObjectId], 1);
        FanPartyList(party);
        ExchangePartySpelled(party, invitee.ObjectId);
        return null;
    }

}
