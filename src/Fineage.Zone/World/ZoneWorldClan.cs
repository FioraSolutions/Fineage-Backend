using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Zone.Clan;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    readonly Dictionary<int, ZoneClan> _clans = [];
    readonly Dictionary<int, int> _clanByPlayer = [];
    readonly Dictionary<int, long> _joinExpiry = [];
    readonly Dictionary<int, long> _createExpiry = [];
    readonly Dictionary<int, PendingClanInvite> _pendingClanInvites = [];
    int _nextClanId = 1;

    sealed class ZoneClan
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Level { get; set; }
        public int LeaderId { get; set; }
        public long DissolvingExpiryUnixMs { get; set; }
        public long CharPenaltyExpiryUnixMs { get; set; }
        /// <summary>Mobius <c>hasCastle</c>. 0 when this clan owns no castle.</summary>
        public int CastleId { get; set; }
        /// <summary>Mobius <c>clan_skills</c> book. Copied onto members via <see cref="RefreshClanSkills"/>.</summary>
        public Dictionary<int, int> Skills { get; } = [];
        public List<ZoneClanMember> Members { get; } = [];
    }

    sealed class ZoneClanMember
    {
        public int CharacterId { get; init; }
        public string Account { get; set; } = "";
        public string Name { get; set; } = "";
        public int ClassId { get; set; }
        public int Level { get; set; }
    }

    readonly record struct PendingClanInvite(int InviterObjectId, int ClanId, long ExpiresUnixMs);

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

        string? error;
        lock (_gate)
            error = TryClanCreate(session, request.Name, now.ToUnixTimeMilliseconds());
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

        string? error;
        lock (_gate)
            error = TryClanInvite(session, invite, now.ToUnixTimeMilliseconds());
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

        string? error;
        lock (_gate)
            error = TryClanAnswer(session, answer, now.ToUnixTimeMilliseconds());
        return error;
    }

    public async Task<string?> TryClanLeaveAsync(ZoneSession session, DateTimeOffset now, CancellationToken ct)
    {
        if (!session.InWorld)
            return "world";

        string? error;
        lock (_gate)
            error = TryClanLeave(session, now.ToUnixTimeMilliseconds());
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

        string? error;
        lock (_gate)
            error = TryClanKick(session, kick.TargetObjectId, now.ToUnixTimeMilliseconds());
        return error;
    }

    public async Task<string?> TryClanDissolveAsync(ZoneSession session, DateTimeOffset now, CancellationToken ct)
    {
        if (!session.InWorld)
            return "world";

        string? error;
        lock (_gate)
            error = TryClanDissolve(session, now.ToUnixTimeMilliseconds());
        return error;
    }

    public async Task<string?> TryClanCancelDissolveAsync(ZoneSession session, DateTimeOffset now, CancellationToken ct)
    {
        if (!session.InWorld)
            return "world";

        string? error;
        lock (_gate)
            error = TryClanCancelDissolve(session, now.ToUnixTimeMilliseconds());
        return error;
    }

    public void CompleteDueDissolutions(DateTimeOffset now)
    {
        lock (_gate)
            CompleteDueDissolutions(now.ToUnixTimeMilliseconds());
    }

    void LoadClans()
    {        if (_accounts == null)
            return;

        foreach (var record in _accounts.ListClans())
        {
            var clan = FromRecord(record);
            _clans[clan.Id] = clan;
            foreach (var member in clan.Members)
                _clanByPlayer[member.CharacterId] = clan.Id;
        }

        CompleteDueDissolutions(_clock().ToUnixTimeMilliseconds());
    }

    /// <summary>Test hook: reread clan rows the store gained after boot (siege registration reads this cache).</summary>
    internal bool ReloadClansForTests()
    {
        lock (_gate)
        {
            _clans.Clear();
            _clanByPlayer.Clear();
            LoadClans();
            return _clans.Count > 0;
        }
    }

    void CompleteDueDissolutions(long nowMs)
    {
        var due = new List<int>();
        foreach (var clan in _clans.Values)
        {
            if (ClanRules.DissolveDue(clan.DissolvingExpiryUnixMs, nowMs))
                due.Add(clan.Id);
        }

        foreach (int id in due)
            DestroyClan(id, nowMs);
    }

    void RememberPenalties(ZoneSession session, CharacterRecord? stored = null)
    {
        if (stored == null)
            return;

        _joinExpiry[session.ObjectId] = stored.ClanJoinExpiryUnixMs;
        _createExpiry[session.ObjectId] = stored.ClanCreateExpiryUnixMs;
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
        if (!_clans.TryGetValue(entity.ClanId, out var clan))
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
        if (_pendingClanInvites.Remove(objectId, out var pending) &&
            Online(pending.InviterObjectId) is { } inviter)
        {
            FanLine([inviter.ObjectId], ClanLines.InviteDeclinedRequestor(name));
            FanJoinResult([inviter.ObjectId], 0);
        }

        foreach (var pair in _pendingClanInvites.ToList())
        {
            if (pair.Value.InviterObjectId == objectId)
                _pendingClanInvites.Remove(pair.Key);
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

        _clans[clan.Id] = clan;
        _clanByPlayer[session.ObjectId] = clan.Id;
        _joinExpiry[session.ObjectId] = 0;
        _createExpiry[session.ObjectId] = 0;
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
