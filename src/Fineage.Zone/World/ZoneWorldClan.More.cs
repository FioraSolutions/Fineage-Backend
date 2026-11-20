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
        string? block = JoinBlock(clan, session.ObjectId, invite.PledgeType, invite.TargetObjectId, self, visible,
            targetBusy: visible && RequestBusy(invite.TargetObjectId), selfBusy: RequestBusy(session.ObjectId), nowMs);
        if (block != null)
        {
            if (block == ClanProtocol.Academy && _byId.TryGetValue(invite.TargetObjectId, out var refused))
            {
                SelfLine(session, 1735, $"{refused.Name} does not meet the requirements to join a Clan Academy.", refused.Name);
                SelfLine(session, 1734, "To join a Clan Academy, characters must be Level 40 or below, not belong another clan and not yet completed their 2nd class transfer.");
            }
            else if (block == ClanProtocol.Full && invite.PledgeType != ClanProtocol.MainPledge)
                SelfLine(session, 233, "The Academy/Royal Guard/Order of Knights is full and cannot accept new members at this time.");
            return block;
        }

        _clan.Invite(invite.TargetObjectId, new PendingClanInvite(
            session.ObjectId,
            clan!.Id,
            ClanRules.InviteDeadline(nowMs),
            invite.PledgeType));
        var target = _byId[invite.TargetObjectId].Session!;
        // Mobius AskJoinPledge names the sub-unit for a sub-unit invite.
        string unitName = invite.PledgeType == ClanProtocol.MainPledge ? clan.Name : SubPledge(clan, invite.PledgeType)?.Name ?? clan.Name;
        NetBridge?.FanoutClanAskJoin([target.ObjectId], new ClanAskJoinMsg
        {
            RequestorObjectId = session.ObjectId,
            RequestorName = session.Name,
            ClanId = clan.Id,
            ClanName = unitName,
            PledgeType = invite.PledgeType
        });
        if (invite.PledgeType == PledgeProtocol.Academy)
            SelfLine(session, 1742, $"A message inviting {target.Name} to join the Clan Academy is being sent.", target.Name);
        return null;
    }

    string? TryClanAnswer(ZoneSession session, ClanAnswerJoinRequestMsg answer, long nowMs)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var entity))
            return "world";

        ClearExpiredClanInvites(nowMs);
        if (!_clan.TryGetInvite(session.ObjectId, out var pending) ||
            pending.InviterObjectId != answer.RequestorObjectId)
            return ClanProtocol.Invite;

        _clan.DropInvite(session.ObjectId);
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

        if (!requestorOnline || !_clan.TryGet(pending.ClanId, out var clan) || ClanOf(pending.InviterObjectId) != clan)
            return ClanProtocol.Invite;

        string? block = JoinBlock(clan, pending.InviterObjectId, pending.PledgeType, session.ObjectId, self: false, visible: true,
            targetBusy: false, selfBusy: false, nowMs);
        if (block != null)
            return block;

        var member = new ZoneClanMember
        {
            CharacterId = session.ObjectId,
            Account = session.Account,
            Name = session.Name,
            ClassId = session.ClassId,
            Level = entity.Level,
            PledgeType = pending.PledgeType
        };
        // Mobius RequestAnswerJoinPledge: grade 9 + join level for academy, grade 5 otherwise.
        var detail = new ClanMemberDetails
        {
            PowerGrade = pending.PledgeType == PledgeProtocol.Academy ? PledgeProtocol.AcademyGrade : PledgeProtocol.NewMemberGrade,
            LevelJoinedAcademy = pending.PledgeType == PledgeProtocol.Academy ? entity.Level : 0
        };
        clan.D.Members[session.ObjectId] = detail;
        var state = new CharacterClanState(session.Account, session.ObjectId, clan.Id, 0, CreateExpiry(session.ObjectId));
        var record = ToRecord(clan, clan.Members.Append(member));
        if (_accounts != null && !_accounts.UpdateClan(record, [state]))
        {
            clan.D.Members.Remove(session.ObjectId);
            return ClanProtocol.Store;
        }

        clan.Members.Add(member);
        _clan.Link(session.ObjectId, clan.Id);
        _clan.SetJoinExpiry(session.ObjectId, 0);
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

        // Mobius Clan.removeClanMember: academy members leave without the join penalty.
        long join = member!.PledgeType == PledgeProtocol.Academy ? 0 : ClanRules.JoinPenaltyExpiry(nowMs);
        long create = CreateExpiry(session.ObjectId);
        var state = new CharacterClanState(member.Account, member.CharacterId, 0, join, create);
        var undo = DetachMember(clan, member);
        var record = ToRecord(clan, clan.Members.Where(m => m.CharacterId != session.ObjectId));
        if (_accounts != null && !_accounts.UpdateClan(record, [state]))
        {
            undo();
            return ClanProtocol.Store;
        }

        clan.Members.Remove(member);
        ClearMemberTitle(entity);
        _clan.Unlink(session.ObjectId);
        _clan.SetJoinExpiry(session.ObjectId, join);
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
