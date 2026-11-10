using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Zone.Clan;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
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
        NetBridge?.FanoutClanAskJoin([target.ObjectId], new ClanAskJoinMsg
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

}
