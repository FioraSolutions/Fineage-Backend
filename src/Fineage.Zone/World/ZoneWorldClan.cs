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

        var outbound = new List<Pending>();
        string? error;
        lock (_gate)
            error = TryClanCreate(session, request.Name, now.ToUnixTimeMilliseconds(), outbound);
        if (error != null)
            return error;

        await FlushOthersAsync(outbound, ct).ConfigureAwait(false);
        return null;
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

        var outbound = new List<Pending>();
        string? error;
        lock (_gate)
            error = TryClanInvite(session, invite, now.ToUnixTimeMilliseconds(), outbound);
        if (error != null)
            return error;

        await FlushOthersAsync(outbound, ct).ConfigureAwait(false);
        return null;
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

        var outbound = new List<Pending>();
        string? error;
        lock (_gate)
            error = TryClanAnswer(session, answer, now.ToUnixTimeMilliseconds(), outbound);
        if (error != null)
            return error;

        await FlushOthersAsync(outbound, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> TryClanLeaveAsync(ZoneSession session, DateTimeOffset now, CancellationToken ct)
    {
        if (!session.InWorld)
            return "world";

        var outbound = new List<Pending>();
        string? error;
        lock (_gate)
            error = TryClanLeave(session, now.ToUnixTimeMilliseconds(), outbound);
        if (error != null)
            return error;

        await FlushOthersAsync(outbound, ct).ConfigureAwait(false);
        return null;
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

        var outbound = new List<Pending>();
        string? error;
        lock (_gate)
            error = TryClanKick(session, kick.TargetObjectId, now.ToUnixTimeMilliseconds(), outbound);
        if (error != null)
            return error;

        await FlushOthersAsync(outbound, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> TryClanDissolveAsync(ZoneSession session, DateTimeOffset now, CancellationToken ct)
    {
        if (!session.InWorld)
            return "world";

        var outbound = new List<Pending>();
        string? error;
        lock (_gate)
            error = TryClanDissolve(session, now.ToUnixTimeMilliseconds(), outbound);
        if (error != null)
            return error;

        await FlushOthersAsync(outbound, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> TryClanCancelDissolveAsync(ZoneSession session, DateTimeOffset now, CancellationToken ct)
    {
        if (!session.InWorld)
            return "world";

        var outbound = new List<Pending>();
        string? error;
        lock (_gate)
            error = TryClanCancelDissolve(session, now.ToUnixTimeMilliseconds(), outbound);
        if (error != null)
            return error;

        await FlushOthersAsync(outbound, ct).ConfigureAwait(false);
        return null;
    }

    public async Task CompleteDueDissolutionsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var outbound = new List<Pending>();
        lock (_gate)
            CompleteDueDissolutions(now.ToUnixTimeMilliseconds(), outbound);
        await FlushOthersAsync(outbound, ct).ConfigureAwait(false);
    }

    void LoadClans()
    {
        if (_accounts == null)
            return;

        foreach (var record in _accounts.ListClans())
        {
            var clan = FromRecord(record);
            _clans[clan.Id] = clan;
            foreach (var member in clan.Members)
                _clanByPlayer[member.CharacterId] = clan.Id;
        }

        CompleteDueDissolutions(_clock().ToUnixTimeMilliseconds(), []);
    }

    void CompleteDueDissolutions(long nowMs, List<Pending> outbound)
    {
        var due = new List<int>();
        foreach (var clan in _clans.Values)
        {
            if (ClanRules.DissolveDue(clan.DissolvingExpiryUnixMs, nowMs))
                due.Add(clan.Id);
        }

        foreach (int id in due)
            DestroyClan(id, nowMs, outbound);
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

    void AnnounceClanEnter(ZoneEntity entity, List<Pending> toSelf, List<Pending> toOthers)
    {
        if (entity.ClanId == 0 || entity.Session == null)
            return;
        if (!_clans.TryGetValue(entity.ClanId, out var clan))
            return;

        RefreshOnlineMember(entity);
        toSelf.Add(Pack(entity.Session, MsgType.PledgeStatusChanged, StatusOf(clan)));
        toSelf.Add(Pack(entity.Session, MsgType.PledgeMemberList, MembersOf(clan)));
        var list = MembersOf(clan);
        foreach (var other in OnlineMembers(clan))
        {
            if (other.ObjectId == entity.ObjectId)
                continue;
            toOthers.Add(Pack(other, MsgType.PledgeMemberList, list));
        }
    }

    void ClanPlayerLeftWorld(int objectId, string name, List<Pending> outbound)
    {
        if (_pendingClanInvites.Remove(objectId, out var pending) &&
            Online(pending.InviterObjectId) is { } inviter)
        {
            outbound.Add(Pack(inviter, MsgType.SystemMessage, ClanLines.InviteDeclinedRequestor(name)));
            outbound.Add(Pack(inviter, MsgType.ClanJoinResult, new ClanJoinResultMsg()));
        }

        foreach (var pair in _pendingClanInvites.ToList())
        {
            if (pair.Value.InviterObjectId == objectId)
                _pendingClanInvites.Remove(pair.Key);
        }

        if (ClanOf(objectId) is { } clan)
        {
            var list = MembersOf(clan);
            foreach (var other in OnlineMembers(clan))
                outbound.Add(Pack(other, MsgType.PledgeMemberList, list));
        }
    }

    string? TryClanCreate(ZoneSession session, string? name, long nowMs, List<Pending> outbound)
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

        outbound.Add(Pack(session, MsgType.SystemMessage, ClanLines.Created()));
        outbound.Add(Pack(session, MsgType.PledgeStatusChanged, StatusOf(clan)));
        outbound.Add(Pack(session, MsgType.PledgeMemberList, MembersOf(clan)));
        outbound.Add(Pack(session, MsgType.UserInfo, UserInfo(entity)));
        QueueCharInfo(entity, outbound);
        return null;
    }

    string? TryClanInvite(ZoneSession session, ClanInviteRequestMsg invite, long nowMs, List<Pending> outbound)
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
        outbound.Add(Pack(target, MsgType.ClanAskJoin, new ClanAskJoinMsg
        {
            RequestorObjectId = session.ObjectId,
            RequestorName = session.Name,
            ClanId = clan.Id,
            ClanName = clan.Name,
            PledgeType = ClanProtocol.MainPledge
        }));
        return null;
    }

    string? TryClanAnswer(ZoneSession session, ClanAnswerJoinRequestMsg answer, long nowMs, List<Pending> outbound)
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
            outbound.Add(Pack(session, MsgType.SystemMessage, ClanLines.InviteDeclinedSelf(
                requestorOnline ? requestorEntity!.Name : "")));
            if (requestorOnline)
            {
                outbound.Add(Pack(requestorEntity!.Session!, MsgType.SystemMessage, ClanLines.InviteDeclinedRequestor(session.Name)));
                outbound.Add(Pack(requestorEntity.Session!, MsgType.ClanJoinResult, new ClanJoinResultMsg { Response = 0 }));
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

        outbound.Add(Pack(requestorEntity!.Session!, MsgType.ClanJoinResult, new ClanJoinResultMsg { Response = answer.Response }));
        var joined = ClanLines.MemberJoined(session.Name);
        foreach (var online in OnlineMembers(clan))
        {
            if (online.ObjectId == session.ObjectId)
                outbound.Add(Pack(online, MsgType.SystemMessage, ClanLines.Entered(clan.Name)));
            outbound.Add(Pack(online, MsgType.SystemMessage, joined));
            outbound.Add(Pack(online, MsgType.PledgeStatusChanged, StatusOf(clan)));
            outbound.Add(Pack(online, MsgType.PledgeMemberList, MembersOf(clan)));
        }

        outbound.Add(Pack(session, MsgType.UserInfo, UserInfo(entity)));
        QueueCharInfo(entity, outbound);
        return null;
    }

    string? TryClanLeave(ZoneSession session, long nowMs, List<Pending> outbound)
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

        outbound.Add(Pack(session, MsgType.SystemMessage, ClanLines.Withdrawn()));
        outbound.Add(Pack(session, MsgType.SystemMessage, ClanLines.JoinWait()));
        QueueClearPledge(session, outbound);
        outbound.Add(Pack(session, MsgType.UserInfo, UserInfo(entity)));

        var left = ClanLines.MemberWithdrawn(member.Name);
        var status = StatusOf(clan);
        var list = MembersOf(clan);
        foreach (var online in OnlineMembers(clan))
        {
            outbound.Add(Pack(online, MsgType.SystemMessage, left));
            outbound.Add(Pack(online, MsgType.PledgeStatusChanged, status));
            outbound.Add(Pack(online, MsgType.PledgeMemberList, list));
        }

        QueueCharInfo(entity, outbound);
        return null;
    }

    string? TryClanKick(ZoneSession session, int targetObjectId, long nowMs, List<Pending> outbound)
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
            outbound.Add(Pack(targetSession, MsgType.SystemMessage, ClanLines.Dismissed()));
            outbound.Add(Pack(targetSession, MsgType.SystemMessage, ClanLines.JoinWait()));
            QueueClearPledge(targetSession, outbound);
            outbound.Add(Pack(targetSession, MsgType.UserInfo, UserInfo(targetEntity)));
        }

        var expelled = ClanLines.Expelled(targetMember.Name);
        var status = StatusOf(clan);
        var list = MembersOf(clan);
        foreach (var online in OnlineMembers(clan))
        {
            outbound.Add(Pack(online, MsgType.SystemMessage, expelled));
            if (online.ObjectId == session.ObjectId)
            {
                outbound.Add(Pack(online, MsgType.SystemMessage, ClanLines.ExpelledLeader()));
                outbound.Add(Pack(online, MsgType.SystemMessage, ClanLines.RecruitWait()));
            }

            outbound.Add(Pack(online, MsgType.PledgeStatusChanged, status));
            outbound.Add(Pack(online, MsgType.PledgeMemberList, list));
        }

        if (targetEntity != null)
            QueueCharInfo(targetEntity, outbound);
        return null;
    }

    string? TryClanDissolve(ZoneSession session, long nowMs, List<Pending> outbound)
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
        var status = StatusOf(clan);
        foreach (var online in OnlineMembers(clan))
        {
            outbound.Add(Pack(online, MsgType.SystemMessage, line));
            outbound.Add(Pack(online, MsgType.PledgeStatusChanged, status));
        }

        return null;
    }

    string? TryClanCancelDissolve(ZoneSession session, long nowMs, List<Pending> outbound)
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
        var status = StatusOf(clan);
        foreach (var online in OnlineMembers(clan))
        {
            outbound.Add(Pack(online, MsgType.SystemMessage, line));
            outbound.Add(Pack(online, MsgType.PledgeStatusChanged, status));
        }

        return null;
    }

    void DestroyClan(int clanId, long nowMs, List<Pending> outbound)
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
            outbound.Add(Pack(session, MsgType.SystemMessage, line));
            QueueClearPledge(session, outbound);
            outbound.Add(Pack(session, MsgType.UserInfo, UserInfo(entity)));
        }

        foreach (var entity in online.OrderBy(e => e.ObjectId))
            QueueCharInfo(entity, outbound);
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

    PledgeStatusChangedMsg StatusOf(ZoneClan clan) => new()
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

    PledgeMemberListMsg MembersOf(ZoneClan clan) => new()
    {
        ClanId = clan.Id,
        LeaderObjectId = clan.LeaderId,
        Members = clan.Members.Select(member => new PledgeMemberEntryMsg
        {
            ObjectId = member.CharacterId,
            Name = member.Name,
            ClassId = member.ClassId,
            Level = member.Level,
            Online = Online(member.CharacterId) != null,
            PledgeType = ClanProtocol.MainPledge
        }).ToArray()
    };

    static void QueueClearPledge(ZoneSession session, List<Pending> outbound)
    {
        outbound.Add(Pack(session, MsgType.PledgeStatusChanged, new PledgeStatusChangedMsg()));
        outbound.Add(Pack(session, MsgType.PledgeMemberList, new PledgeMemberListMsg()));
    }

    void SyncClanStanding(ZoneEntity entity)
    {
        var clan = entity.ClanId == 0 ? null : ClanOf(entity.ObjectId);
        entity.InClan = clan != null;
        entity.ClanLeader = clan != null && clan.LeaderId == entity.ObjectId;
        entity.CastleId = clan?.CastleId ?? 0;
    }

    void QueueCharInfo(ZoneEntity player, List<Pending> outbound)
    {
        byte[] body = MessagePack.MessagePackSerializer.Serialize(CharInfo(player));
        foreach (int id in player.Knownlist.OrderBy(value => value))
        {
            if (_byId.TryGetValue(id, out var other) && other.Session != null)
                outbound.Add(new Pending(other.Session, MsgType.CharInfo, body));
        }
    }
}
