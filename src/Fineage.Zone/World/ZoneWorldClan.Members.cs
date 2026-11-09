using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Zone.Clan;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
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
        if (record.Skills != null)
        {
            foreach (var (id, level) in record.Skills)
            {
                if (id > 0 && level > 0)
                    clan.Skills[id] = level;
            }
        }
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
        clan.CastleId,
        clan.Skills.Count == 0 ? null : new Dictionary<int, int>(clan.Skills));

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

    void FanPledgeStatus(IReadOnlyList<int> watchers, ZoneClan clan) =>
        NetBridge?.FanoutPledgeStatus(watchers, StatusOf(clan));

    void FanMemberList(IReadOnlyList<int> watchers, ZoneClan clan) =>
        NetBridge?.FanoutPledgeMemberList(watchers, MembersOf(clan));

    void FanLine(IReadOnlyList<int> watchers, SystemMessageMsg line) =>
        NetBridge?.FanoutSystemMessage(watchers, new SystemMessageMsg
        {
            Id = line.Id,
            Text = line.Text ?? "",
            Params = line.Params ?? []
        });

    void FanJoinResult(IReadOnlyList<int> watchers, int response) =>
        NetBridge?.FanoutClanJoinResult(watchers, new ClanJoinResultMsg
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
        NetBridge?.FanoutPledgeStatus(self, new PledgeStatusChangedMsg());
        NetBridge?.FanoutPledgeMemberList(self, new PledgeMemberListMsg());
    }


    /// <summary>
    /// Mobius <c>EnterWorld</c>:561 sends <c>PledgeShowInfoUpdate</c> and <c>PledgeShowMemberListAll</c>
    /// to a returning member. Nothing in the Net enter sequence sent them, so a clan member logging
    /// back in saw an empty clan window until some other clan action happened to fan out a status.
    /// </summary>
    internal bool TryBuildNetClanSnapshot(
        ZoneSession session,
        out PledgeStatusChangedMsg status,
        out PledgeMemberListMsg members)
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

            status = StatusOf(clan);
            members = MembersOf(clan);
            return true;
        }
    }

    void SyncClanStanding(ZoneEntity entity)
    {
        var clan = entity.ClanId == 0 ? null : ClanOf(entity.ObjectId);
        entity.InClan = clan != null;
        entity.ClanLeader = clan != null && clan.LeaderId == entity.ObjectId;
        entity.CastleId = clan?.CastleId ?? 0;
        RefreshClanSkills(entity, clan);
    }

    /// <summary>
    /// Mobius <c>Clan.addSkillEffects(player)</c>: copy the clan skill book onto the live session map
    /// (or clear it when the player leaves the clan).
    /// </summary>
    void RefreshClanSkills(ZoneEntity entity, ZoneClan? clan)
    {
        var live = entity.ClanSkills;
        live.Clear();
        if (clan == null || clan.Skills.Count == 0)
            return;
        foreach (var (id, level) in clan.Skills)
        {
            if (id > 0 && level > 0)
                live[id] = level;
        }
    }

    /// <summary>Test/admin hook: put a skill on the clan book, persist it, and refresh every online member.</summary>
    internal bool TrySetClanSkillForTest(int clanId, int skillId, int level)
    {
        lock (_gate)
        {
            if (!_clans.TryGetValue(clanId, out var clan) || skillId <= 0)
                return false;
            if (level <= 0)
                clan.Skills.Remove(skillId);
            else
                clan.Skills[skillId] = level;
            if (_accounts != null)
                _accounts.UpdateClan(ToRecord(clan), []);
            foreach (var member in OnlineMembers(clan))
            {
                if (_byId.TryGetValue(member.ObjectId, out var entity))
                    RefreshClanSkills(entity, clan);
            }
            return true;
        }
    }

    /// <summary>The crest and clan name ride on the spawn row, so every watcher needs it again.</summary>
    void QueueCharInfo(ZoneEntity player) =>
        NetBridge?.FanoutCharInfo([.. player.Knownlist.OrderBy(value => value)], player.ObjectId);
}
