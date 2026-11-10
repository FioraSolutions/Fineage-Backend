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
}
