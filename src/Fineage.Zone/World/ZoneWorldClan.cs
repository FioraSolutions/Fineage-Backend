using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Zone.Clan;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    readonly ClanActor _clan = new();

    public async Task<string?> TryClanCreateAsync(
        ZoneSession session,
        ClanCreateRequestMsg request,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (request == null)
            return "bad_request";
        if (!session.InWorld)
            return "world";

        string? error = TryClanCreate(session, request.Name, now.ToUnixTimeMilliseconds());
        return error;
    }

    public async Task<string?> TryClanInviteAsync(
        ZoneSession session,
        ClanInviteRequestMsg invite,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (invite == null)
            return "bad_request";
        if (!session.InWorld)
            return "world";

        string? error = TryClanInvite(session, invite, now.ToUnixTimeMilliseconds());
        return error;
    }

    public async Task<string?> TryClanAnswerAsync(
        ZoneSession session,
        ClanAnswerJoinRequestMsg answer,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (answer == null)
            return "bad_request";
        if (!session.InWorld)
            return "world";

        string? error = TryClanAnswer(session, answer, now.ToUnixTimeMilliseconds());
        return error;
    }

    public async Task<string?> TryClanLeaveAsync(ZoneSession session, DateTimeOffset now, CancellationToken ct)
    {
        if (!session.InWorld)
            return "world";

        string? error = TryClanLeave(session, now.ToUnixTimeMilliseconds());
        return error;
    }

    public async Task<string?> TryClanKickAsync(
        ZoneSession session,
        ClanKickRequestMsg kick,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (kick == null)
            return "bad_request";
        if (!session.InWorld)
            return "world";

        string? error = TryClanKick(session, kick.TargetObjectId, now.ToUnixTimeMilliseconds());
        return error;
    }

    public async Task<string?> TryClanDissolveAsync(ZoneSession session, DateTimeOffset now, CancellationToken ct)
    {
        if (!session.InWorld)
            return "world";

        string? error = TryClanDissolve(session, now.ToUnixTimeMilliseconds());
        return error;
    }

    public async Task<string?> TryClanCancelDissolveAsync(ZoneSession session, DateTimeOffset now, CancellationToken ct)
    {
        if (!session.InWorld)
            return "world";

        string? error = TryClanCancelDissolve(session, now.ToUnixTimeMilliseconds());
        return error;
    }

    /// <summary>Test and admin hook: runs the clan actor's dissolution step at the current tick.</summary>
    public void CompleteDueDissolutions()
    {
        using (_affinity.Step())
            CompleteDueDissolutionsNow();
    }

    void CompleteDueDissolutionsNow() => AtHookTick(tick => DissolveClans(tick, _clan.Step(tick)));

    void LoadClans()
    {
        if (_accounts == null)
            return;

        foreach (var record in _accounts.ListClans())
        {
            var clan = FromRecord(record);
            _clan.Add(clan);
            foreach (var member in clan.Members)
                _clan.Link(member.CharacterId, clan.Id);
        }

        // Mobius ClanTable load: a pending leader delegation takes effect at server start.
        foreach (var clan in _clan.All)
        {
            if (clan.D.NewLeaderId != 0)
                ApplyLeaderDelegation(clan);
        }
        _hallAuctions.Halls = null;
        _hallFunctions.Halls = null;

        CompleteDueDissolutionsNow();
    }

    /// <summary>Test hook: reread clan rows the store gained after boot (siege registration reads this cache).</summary>
    internal bool ReloadClansForTests()
    {
        using (_affinity.Step())
        {
            _clan.Clear();
            LoadClans();
            foreach (var entity in _byId.Values)
            {
                if (entity.Kind != ZoneEntityKind.Player)
                    continue;
                entity.ClanId = ClanIdFor(entity.ObjectId);
                SyncClanStanding(entity);
            }
            return _clan.Count > 0;
        }
    }

    int DissolveClans(TickContext tick, IReadOnlyList<ClanDissolutionDue> due)
    {
        foreach (var clan in due)
            DestroyClan(clan.ClanId, tick.WallUnixMs);
        return due.Count;
    }

    void RememberPenalties(ZoneSession session, CharacterRecord? stored = null)
    {
        if (stored == null)
            return;

        _clan.SetJoinExpiry(session.ObjectId, stored.ClanJoinExpiryUnixMs);
        _clan.SetCreateExpiry(session.ObjectId, stored.ClanCreateExpiryUnixMs);
        int clanId = ClanIdFor(session.ObjectId);
        if (stored.ClanId == clanId)
            return;
        _accounts?.TrySetCharacterClanState(new CharacterClanState(
            session.Account,
            session.ObjectId,
            clanId,
            stored.ClanJoinExpiryUnixMs,
            stored.ClanCreateExpiryUnixMs));
    }

    void AnnounceClanEnter(ZoneEntity entity)
    {
        if (entity.ClanId == 0 || entity.Session == null)
            return;
        if (!_clan.TryGet(entity.ClanId, out var clan))
            return;

        RefreshOnlineMember(entity);
        FanPledgeStatus([entity.ObjectId], clan);
        FanMemberList([entity.ObjectId], clan);
        var others = OnlineMembers(clan)
            .Where(other => other.ObjectId != entity.ObjectId)
            .Select(other => other.ObjectId)
            .ToArray();
        FanMemberList(others, clan);
    }

    void ClanPlayerLeftWorld(int objectId, string name)
    {
        if (_clan.TakeInvite(objectId, out var pending) &&
            Online(pending.InviterObjectId) is { } inviter)
        {
            FanLine([inviter.ObjectId], ClanLines.InviteDeclinedRequestor(name));
            FanJoinResult([inviter.ObjectId], 0);
        }

        foreach (var pair in _clan.Invites)
        {
            if (pair.Value.InviterObjectId == objectId)
                _clan.DropInvite(pair.Key);
        }

        if (ClanOf(objectId) is { } clan)
            FanMemberList(OnlineMembers(clan).Select(other => other.ObjectId).ToArray(), clan);
    }

    string? TryClanCreate(ZoneSession session, string? name, long nowMs)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var entity))
            return "world";

        ClearExpiredClanInvites(nowMs);
        int clanId = ClanIdFor(session.ObjectId);
        string? block = ClanRules.CreateBlock(
            entity.Level,
            clanId,
            CreateExpiry(session.ObjectId),
            nowMs,
            name,
            NameTaken(name));
        if (block != null)
            return block;

        var clan = new ZoneClan
        {
            Name = name!,
            Level = 0,
            LeaderId = session.ObjectId
        };
        clan.Members.Add(new ZoneClanMember
        {
            CharacterId = session.ObjectId,
            Account = session.Account,
            Name = session.Name,
            ClassId = session.ClassId,
            Level = entity.Level
        });
        var leader = new CharacterClanState(session.Account, session.ObjectId, 0, 0, 0);
        if (!PersistNew(clan, leader))
            return NameTaken(name) ? ClanProtocol.NameTaken : ClanProtocol.Store;

        _clan.Add(clan);
        _clan.Link(session.ObjectId, clan.Id);
        _clan.SetJoinExpiry(session.ObjectId, 0);
        _clan.SetCreateExpiry(session.ObjectId, 0);
        entity.ClanId = clan.Id;
        SyncClanStanding(entity);

        int[] self = [session.ObjectId];
        FanLine(self, ClanLines.Created());
        FanPledgeStatus(self, clan);
        FanMemberList(self, clan);
        FanUserInfo(entity);
        QueueCharInfo(entity);
        return null;
    }

}
