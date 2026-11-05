using Fineage.Login;
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

    void RememberPenalties(ZoneSession session)
    {
        if (_accounts == null)
            return;
        var record = _accounts.FindById(session.Account, session.ObjectId);
        if (record == null)
            return;

        _joinExpiry[session.ObjectId] = record.ClanJoinExpiryUnixMs;
        _createExpiry[session.ObjectId] = record.ClanCreateExpiryUnixMs;
        int clanId = ClanIdFor(session.ObjectId);
        if (record.ClanId == clanId)
            return;
        _accounts.TrySetCharacterClanState(new CharacterClanState(
            session.Account,
            session.ObjectId,
            clanId,
            record.ClanJoinExpiryUnixMs,
            record.ClanCreateExpiryUnixMs));
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

    string? TryClanInvite(ZoneSession session, ClanInviteRequestMsg invite, long nowMs)
    {
        if (!IsLive(session))
            return "world";

        ClearExpiredClanInvites(nowMs);
        var clan = ClanOf(session.ObjectId);
        bool self = invite.TargetObjectId == session.ObjectId;
        bool visible = !self && IsInviteTarget(session.ObjectId, invite.TargetObjectId);
        string? block = ClanRules.InviteBlock(
            inClan: clan != null,
            leader: clan != null && clan.LeaderId == session.ObjectId,
            pledgeType: invite.PledgeType,
            self: self,
            targetVisible: visible,
            targetBusy: visible && RequestBusy(invite.TargetObjectId),
            selfBusy: RequestBusy(session.ObjectId),
            clanRecruitPenalty: clan != null && ClanRules.PenaltyActive(clan.CharPenaltyExpiryUnixMs, nowMs),
            targetInClan: visible && ClanOf(invite.TargetObjectId) != null,
            targetJoinPenalty: visible && ClanRules.PenaltyActive(JoinExpiry(invite.TargetObjectId), nowMs),
            memberCount: clan?.Members.Count ?? 0,
            clanLevel: clan?.Level ?? 0);
        if (block != null)
            return block;

        _pendingClanInvites[invite.TargetObjectId] = new PendingClanInvite(
            session.ObjectId,
            clan!.Id,
            ClanRules.InviteDeadline(nowMs));
        var target = _byId[invite.TargetObjectId].Session!;
        NetBridge?.FanoutClanAskJoin([target.ObjectId], new Fineage.Net.Contract.ClanAskJoinNetMsg
        {
            RequestorObjectId = session.ObjectId,
            RequestorName = session.Name,
            ClanId = clan.Id,
            ClanName = clan.Name,
            PledgeType = ClanProtocol.MainPledge
        });
        return null;
    }

    string? TryClanAnswer(ZoneSession session, ClanAnswerJoinRequestMsg answer, long nowMs)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var entity))
            return "world";

        ClearExpiredClanInvites(nowMs);
        if (!_pendingClanInvites.TryGetValue(session.ObjectId, out var pending) ||
            pending.InviterObjectId != answer.RequestorObjectId)
            return ClanProtocol.Invite;

        _pendingClanInvites.Remove(session.ObjectId);
        bool requestorOnline = _byId.TryGetValue(pending.InviterObjectId, out var requestorEntity) &&
                               requestorEntity.Session != null &&
                               IsLive(requestorEntity.Session);

        if (answer.Response == 0)
        {
            FanLine([session.ObjectId], ClanLines.InviteDeclinedSelf(
                requestorOnline ? requestorEntity!.Name : ""));
            if (requestorOnline)
            {
                FanLine([requestorEntity!.ObjectId], ClanLines.InviteDeclinedRequestor(session.Name));
                FanJoinResult([requestorEntity.ObjectId], 0);
            }

            return null;
        }

        if (!requestorOnline || !_clans.TryGetValue(pending.ClanId, out var clan) || clan.LeaderId != pending.InviterObjectId)
            return ClanProtocol.Invite;

        string? block = ClanRules.InviteBlock(
            inClan: true,
            leader: true,
            pledgeType: ClanProtocol.MainPledge,
            self: false,
            targetVisible: true,
            targetBusy: false,
            selfBusy: false,
            clanRecruitPenalty: ClanRules.PenaltyActive(clan.CharPenaltyExpiryUnixMs, nowMs),
            targetInClan: ClanOf(session.ObjectId) != null,
            targetJoinPenalty: ClanRules.PenaltyActive(JoinExpiry(session.ObjectId), nowMs),
            memberCount: clan.Members.Count,
            clanLevel: clan.Level);
        if (block != null)
            return block;

        var member = new ZoneClanMember
        {
            CharacterId = session.ObjectId,
            Account = session.Account,
            Name = session.Name,
            ClassId = session.ClassId,
            Level = entity.Level
        };
        var state = new CharacterClanState(session.Account, session.ObjectId, clan.Id, 0, CreateExpiry(session.ObjectId));
        var record = ToRecord(clan, clan.Members.Append(member));
        if (_accounts != null && !_accounts.UpdateClan(record, [state]))
            return ClanProtocol.Store;

        clan.Members.Add(member);
        _clanByPlayer[session.ObjectId] = clan.Id;
        _joinExpiry[session.ObjectId] = 0;
        entity.ClanId = clan.Id;
        SyncClanStanding(entity);

        FanJoinResult([requestorEntity!.ObjectId], answer.Response);
        var joined = ClanLines.MemberJoined(session.Name);
        int[] online = OnlineMembers(clan).Select(member => member.ObjectId).ToArray();
        foreach (int id in online)
        {
            if (id == session.ObjectId)
                FanLine([id], ClanLines.Entered(clan.Name));
            FanLine([id], joined);
        }

        FanPledgeStatus(online, clan);
        FanMemberList(online, clan);
        FanUserInfo(entity);
        QueueCharInfo(entity);
        return null;
    }

    string? TryClanLeave(ZoneSession session, long nowMs)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var entity))
            return "world";

        var clan = ClanOf(session.ObjectId);
        var member = clan?.Members.FirstOrDefault(m => m.CharacterId == session.ObjectId);
        string? block = ClanRules.LeaveBlock(
            clan != null && member != null,
            clan != null && clan.LeaderId == session.ObjectId,
            ClanRules.IsInCombat(entity.CombatUntilUnixMs, nowMs));
        if (block != null || clan == null || member == null)
            return block ?? ClanProtocol.None;

        long join = ClanRules.JoinPenaltyExpiry(nowMs);
        long create = CreateExpiry(session.ObjectId);
        var state = new CharacterClanState(member!.Account, member.CharacterId, 0, join, create);
        var record = ToRecord(clan, clan.Members.Where(m => m.CharacterId != session.ObjectId));
        if (_accounts != null && !_accounts.UpdateClan(record, [state]))
            return ClanProtocol.Store;

        clan.Members.Remove(member);
        _clanByPlayer.Remove(session.ObjectId);
        _joinExpiry[session.ObjectId] = join;
        entity.ClanId = 0;
        SyncClanStanding(entity);

        int[] self = [session.ObjectId];
        FanLine(self, ClanLines.Withdrawn());
        FanLine(self, ClanLines.JoinWait());
        QueueClearPledge(session);
        FanUserInfo(entity);

        int[] rest = OnlineMembers(clan).Select(member => member.ObjectId).ToArray();
        FanLine(rest, ClanLines.MemberWithdrawn(member.Name));
        FanPledgeStatus(rest, clan);
        FanMemberList(rest, clan);
        QueueCharInfo(entity);
        return null;
    }

    string? TryClanKick(ZoneSession session, int targetObjectId, long nowMs)
    {
        if (!IsLive(session))
            return "world";

        var clan = ClanOf(session.ObjectId);
        var targetMember = clan?.Members.FirstOrDefault(m => m.CharacterId == targetObjectId);
        bool targetCombat = _byId.TryGetValue(targetObjectId, out var targetEntity) &&
                            ClanRules.IsInCombat(targetEntity.CombatUntilUnixMs, nowMs);
        string? block = ClanRules.KickBlock(
            clan != null,
            clan != null && clan.LeaderId == session.ObjectId,
            targetObjectId == session.ObjectId,
            targetMember != null,
            targetCombat);
        if (block != null || clan == null || targetMember == null)
            return block ?? ClanProtocol.None;

        long join = ClanRules.JoinPenaltyExpiry(nowMs);
        long recruit = join;
        long create = CreateExpiry(targetObjectId, targetMember!.Account);
        var state = new CharacterClanState(targetMember.Account, targetMember.CharacterId, 0, join, create);
        var record = ToRecord(clan, clan.Members.Where(m => m.CharacterId != targetObjectId)) with
        {
            CharPenaltyExpiryUnixMs = recruit
        };
        if (_accounts != null && !_accounts.UpdateClan(record, [state]))
            return ClanProtocol.Store;

        clan.Members.Remove(targetMember);
        _clanByPlayer.Remove(targetObjectId);
        clan.CharPenaltyExpiryUnixMs = recruit;
        _joinExpiry[targetObjectId] = join;
        _createExpiry[targetObjectId] = create;
        if (targetEntity != null)
        {
            targetEntity.ClanId = 0;
            SyncClanStanding(targetEntity);
        }

        if (targetEntity?.Session is { InWorld: true } targetSession)
        {
            int[] kicked = [targetSession.ObjectId];
            FanLine(kicked, ClanLines.Dismissed());
            FanLine(kicked, ClanLines.JoinWait());
            QueueClearPledge(targetSession);
            FanUserInfo(targetEntity);
        }

        var expelled = ClanLines.Expelled(targetMember.Name);
        int[] online = OnlineMembers(clan).Select(member => member.ObjectId).ToArray();
        foreach (int id in online)
        {
            FanLine([id], expelled);
            if (id == session.ObjectId)
            {
                FanLine([id], ClanLines.ExpelledLeader());
                FanLine([id], ClanLines.RecruitWait());
            }
        }

        FanPledgeStatus(online, clan);
        FanMemberList(online, clan);
        if (targetEntity != null)
            QueueCharInfo(targetEntity);
        return null;
    }

    string? TryClanDissolve(ZoneSession session, long nowMs)
    {
        if (!IsLive(session))
            return "world";

        var clan = ClanOf(session.ObjectId);
        string? block = ClanRules.DissolveBlock(
            clan != null,
            clan != null && clan.LeaderId == session.ObjectId,
            clan != null && clan.DissolvingExpiryUnixMs > nowMs);
        if (block != null || clan == null)
            return block ?? ClanProtocol.None;

        long expiry = ClanRules.DissolveExpiry(nowMs);
        var record = ToRecord(clan) with { DissolvingExpiryUnixMs = expiry };
        if (_accounts != null && !_accounts.UpdateClan(record, []))
            return ClanProtocol.Store;

        clan.DissolvingExpiryUnixMs = expiry;
        var line = ClanLines.DissolveRequested();
        int[] online = OnlineMembers(clan).Select(member => member.ObjectId).ToArray();
        foreach (int id in online)
            FanLine([id], line);
        FanPledgeStatus(online, clan);

        return null;
    }

    string? TryClanCancelDissolve(ZoneSession session, long nowMs)
    {
        if (!IsLive(session))
            return "world";

        var clan = ClanOf(session.ObjectId);
        bool dissolving = clan != null && clan.DissolvingExpiryUnixMs > nowMs;
        string? block = ClanRules.CancelDissolveBlock(
            clan != null,
            clan != null && clan.LeaderId == session.ObjectId,
            dissolving);
        if (block != null || clan == null)
            return block ?? ClanProtocol.None;

        var record = ToRecord(clan) with { DissolvingExpiryUnixMs = 0 };
        if (_accounts != null && !_accounts.UpdateClan(record, []))
            return ClanProtocol.Store;

        clan.DissolvingExpiryUnixMs = 0;
        var line = ClanLines.DissolveCancelled();
        int[] online = OnlineMembers(clan).Select(member => member.ObjectId).ToArray();
        foreach (int id in online)
            FanLine([id], line);
        FanPledgeStatus(online, clan);

        return null;
    }

    void DestroyClan(int clanId, long nowMs)
    {
        if (!_clans.TryGetValue(clanId, out var clan))
            return;

        long leaderCreate = ClanRules.CreatePenaltyExpiry(nowMs);
        var states = new List<CharacterClanState>(clan.Members.Count);
        var online = new List<ZoneEntity>();
        foreach (var member in clan.Members)
        {
            bool leader = member.CharacterId == clan.LeaderId;
            long create = leader ? leaderCreate : CreateExpiry(member.CharacterId, member.Account);
            states.Add(new CharacterClanState(member.Account, member.CharacterId, 0, 0, create));
            if (_byId.TryGetValue(member.CharacterId, out var entity) && entity.Session is { InWorld: true })
                online.Add(entity);
        }

        if (_accounts != null && !_accounts.DeleteClan(clanId, states))
            return;

        foreach (var member in clan.Members)
        {
            _clanByPlayer.Remove(member.CharacterId);
            _joinExpiry[member.CharacterId] = 0;
            if (member.CharacterId == clan.LeaderId)
                _createExpiry[member.CharacterId] = leaderCreate;
        }

        _clans.Remove(clanId);
        ReleaseSiegeClan(clanId);
        foreach (var pair in _pendingClanInvites.ToList())
        {
            if (pair.Value.ClanId == clanId)
                _pendingClanInvites.Remove(pair.Key);
        }

        var line = ClanLines.Dispersed();
        foreach (var entity in online.OrderBy(e => e.ObjectId))
        {
            entity.ClanId = 0;
            SyncClanStanding(entity);
            var session = entity.Session!;
            FanLine([entity.ObjectId], line);
            QueueClearPledge(session);
            FanUserInfo(entity);
        }

        foreach (var entity in online.OrderBy(e => e.ObjectId))
            QueueCharInfo(entity);
    }

    void RefreshOnlineMember(ZoneEntity entity)
    {
        if (entity.Session == null || !_clans.TryGetValue(entity.ClanId, out var clan))
            return;
        var member = clan.Members.FirstOrDefault(m => m.CharacterId == entity.ObjectId);
        if (member == null)
            return;
        if (member.Name == entity.Name &&
            member.ClassId == entity.ClassId &&
            member.Level == entity.Level &&
            member.Account == entity.Session.Account)
            return;

        member.Name = entity.Name;
        member.ClassId = entity.ClassId;
        member.Level = entity.Level;
        member.Account = entity.Session.Account;
        if (_accounts != null)
            _accounts.UpdateClan(ToRecord(clan, clan.Members), []);
    }

    void ClearExpiredClanInvites(long nowMs)
    {
        foreach (var pair in _pendingClanInvites.ToList())
        {
            if (ClanRules.InviteExpired(pair.Value.ExpiresUnixMs, nowMs))
                _pendingClanInvites.Remove(pair.Key);
        }
    }

    bool RequestBusy(int objectId)
    {
        if (_pendingClanInvites.ContainsKey(objectId))
            return true;
        foreach (var pending in _pendingClanInvites.Values)
        {
            if (pending.InviterObjectId == objectId)
                return true;
        }

        if (_pendingInvites.ContainsKey(objectId))
            return true;
        foreach (var pending in _pendingInvites.Values)
        {
            if (pending.InviterObjectId == objectId)
                return true;
        }

        return false;
    }

    bool IsInviteTarget(int inviterId, int targetId)
    {
        if (!_byId.TryGetValue(targetId, out var entity) || entity.Kind != ZoneEntityKind.Player)
            return false;
        if (entity.Session is not { InWorld: true } session || !IsLive(session))
            return false;
        return VisibleTo(inviterId, targetId);
    }

    int ClanIdFor(int characterId) =>
        _clanByPlayer.TryGetValue(characterId, out int id) && _clans.ContainsKey(id) ? id : 0;

    ZoneClan? ClanOf(int characterId)
    {
        int id = ClanIdFor(characterId);
        return id == 0 ? null : _clans[id];
    }

    bool NameTaken(string? name) =>
        name != null && _clans.Values.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    long JoinExpiry(int characterId)
    {
        if (_joinExpiry.TryGetValue(characterId, out long value))
            return value;
        return 0;
    }

    long CreateExpiry(int characterId, string? account = null)
    {
        if (_createExpiry.TryGetValue(characterId, out long value))
            return value;
        if (account != null && _accounts != null)
        {
            var record = _accounts.FindById(account, characterId);
            if (record != null)
                return record.ClanCreateExpiryUnixMs;
        }

        return 0;
    }

    ZoneSession? Online(int objectId) =>
        _byId.TryGetValue(objectId, out var entity) && entity.Session is { InWorld: true } session && IsLive(session)
            ? session
            : null;

    List<ZoneSession> OnlineMembers(ZoneClan clan)
    {
        var sessions = new List<ZoneSession>();
        foreach (var member in clan.Members.OrderBy(m => m.CharacterId))
        {
            if (Online(member.CharacterId) is { } session)
                sessions.Add(session);
        }

        return sessions;
    }

    bool PersistNew(ZoneClan clan, CharacterClanState leader)
    {
        if (_accounts == null)
        {
            clan.Id = _nextClanId++;
            return true;
        }

        var saved = _accounts.InsertClan(ToRecord(clan, clan.Members), leader);
        if (saved == null)
            return false;
        clan.Id = saved.Id;
        return true;
    }

    static ZoneClan FromRecord(ClanRecord record)
    {
        var clan = new ZoneClan
        {
            Id = record.Id,
            Name = record.Name,
            Level = record.Level,
            LeaderId = record.LeaderId,
            DissolvingExpiryUnixMs = record.DissolvingExpiryUnixMs,
            CharPenaltyExpiryUnixMs = record.CharPenaltyExpiryUnixMs,
            CastleId = record.CastleId
        };
        foreach (var member in record.Members)
        {
            clan.Members.Add(new ZoneClanMember
            {
                CharacterId = member.CharacterId,
                Account = member.Account,
                Name = member.Name,
                ClassId = member.ClassId,
                Level = member.Level
            });
        }

        return clan;
    }

    static ClanRecord ToRecord(ZoneClan clan) => ToRecord(clan, clan.Members);

    static ClanRecord ToRecord(ZoneClan clan, IEnumerable<ZoneClanMember> members) => new(
        clan.Id,
        clan.Name,
        clan.Level,
        clan.LeaderId,
        ClanProtocol.CrestId,
        clan.DissolvingExpiryUnixMs,
        clan.CharPenaltyExpiryUnixMs,
        members.Select(member => new ClanMemberRecord(
            member.CharacterId,
            member.Account,
            member.Name,
            member.ClassId,
            member.Level,
            ClanProtocol.MainPledge)).ToArray(),
        clan.CastleId);

    Fineage.Net.Contract.PledgeStatusNetMsg StatusOf(ZoneClan clan) => new()
    {
        ClanId = clan.Id,
        Name = clan.Name,
        Level = clan.Level,
        LeaderObjectId = clan.LeaderId,
        CrestId = ClanProtocol.CrestId,
        MemberCount = clan.Members.Count,
        DissolvingExpiryUnixMs = clan.DissolvingExpiryUnixMs,
        CharPenaltyExpiryUnixMs = clan.CharPenaltyExpiryUnixMs
    };

    Fineage.Net.Contract.PledgeMemberListNetMsg MembersOf(ZoneClan clan) => new()
    {
        ClanId = clan.Id,
        LeaderObjectId = clan.LeaderId,
        Members = clan.Members.Select(member => new Fineage.Net.Contract.PledgeMemberNet
        {
            ObjectId = member.CharacterId,
            Name = member.Name,
            ClassId = member.ClassId,
            Level = member.Level,
            Online = Online(member.CharacterId) != null,
            PledgeType = ClanProtocol.MainPledge
        }).ToArray()
    };

    void FanPledgeStatus(IReadOnlyList<int> watchers, ZoneClan clan) =>
        NetBridge?.FanoutPledgeStatus(watchers, StatusOf(clan));

    void FanMemberList(IReadOnlyList<int> watchers, ZoneClan clan) =>
        NetBridge?.FanoutPledgeMemberList(watchers, MembersOf(clan));

    void FanLine(IReadOnlyList<int> watchers, SystemMessageMsg line) =>
        NetBridge?.FanoutSystemMessage(watchers, new Fineage.Net.Contract.SystemMessageNetMsg
        {
            Id = line.Id,
            Text = line.Text ?? "",
            Params = line.Params ?? []
        });

    void FanJoinResult(IReadOnlyList<int> watchers, int response) =>
        NetBridge?.FanoutClanJoinResult(watchers, new Fineage.Net.Contract.ClanJoinResultNetMsg
        {
            Response = response
        });

    /// <summary>The Net sheet comes from the live entity: it also carries the combat stats the V2 row omitted.</summary>
    void FanUserInfo(ZoneEntity entity) =>
        NetBridge?.FanoutUserInfo([entity.ObjectId], NetUserInfo(entity));

    /// <summary>Mobius sends an empty pledge status and roster when a player leaves a clan.</summary>
    void QueueClearPledge(ZoneSession session)
    {
        int[] self = [session.ObjectId];
        NetBridge?.FanoutPledgeStatus(self, new Fineage.Net.Contract.PledgeStatusNetMsg());
        NetBridge?.FanoutPledgeMemberList(self, new Fineage.Net.Contract.PledgeMemberListNetMsg());
    }


    /// <summary>
    /// Mobius <c>EnterWorld</c>:561 sends <c>PledgeShowInfoUpdate</c> and <c>PledgeShowMemberListAll</c>
    /// to a returning member. Nothing in the Net enter sequence sent them, so a clan member logging
    /// back in saw an empty clan window until some other clan action happened to fan out a status.
    /// </summary>
    internal bool TryBuildNetClanSnapshot(
        ZoneSession session,
        out Fineage.Net.Contract.PledgeStatusNetMsg status,
        out Fineage.Net.Contract.PledgeMemberListNetMsg members)
    {
        status = null!;
        members = null!;
        lock (_gate)
        {
            if (_clans.Count == 0)
                return false;
            var clan = ClanOf(session.ObjectId);
            if (clan == null)
                return false;

            var v2Status = StatusOf(clan);
            status = new Fineage.Net.Contract.PledgeStatusNetMsg
            {
                ClanId = v2Status.ClanId,
                Name = v2Status.Name ?? "",
                Level = v2Status.Level,
                LeaderObjectId = v2Status.LeaderObjectId,
                CrestId = v2Status.CrestId,
                MemberCount = v2Status.MemberCount,
                DissolvingExpiryUnixMs = v2Status.DissolvingExpiryUnixMs,
                CharPenaltyExpiryUnixMs = v2Status.CharPenaltyExpiryUnixMs
            };

            var v2Members = MembersOf(clan);
            var rows = new Fineage.Net.Contract.PledgeMemberNet[v2Members.Members.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                var src = v2Members.Members[i];
                rows[i] = new Fineage.Net.Contract.PledgeMemberNet
                {
                    ObjectId = src.ObjectId,
                    Name = src.Name ?? "",
                    ClassId = src.ClassId,
                    Level = src.Level,
                    Online = src.Online,
                    PledgeType = src.PledgeType
                };
            }

            members = new Fineage.Net.Contract.PledgeMemberListNetMsg
            {
                ClanId = v2Members.ClanId,
                LeaderObjectId = v2Members.LeaderObjectId,
                Members = rows
            };
            return true;
        }
    }

    void SyncClanStanding(ZoneEntity entity)
    {
        var clan = entity.ClanId == 0 ? null : ClanOf(entity.ObjectId);
        entity.InClan = clan != null;
        entity.ClanLeader = clan != null && clan.LeaderId == entity.ObjectId;
        entity.CastleId = clan?.CastleId ?? 0;
    }

    /// <summary>The crest and clan name ride on the spawn row, so every watcher needs it again.</summary>
    void QueueCharInfo(ZoneEntity player) =>
        NetBridge?.FanoutCharInfo([.. player.Knownlist.OrderBy(value => value)], player.ObjectId);
}
