using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Zone.Clan;

namespace Fineage.Zone.World;

/// <summary>
/// Join rules per pledge type after Mobius <c>Clan.checkClanJoinCondition</c>, and the link cleanup of
/// <c>Clan.removeClanMember</c> (sponsor / apprentice, captaincy, non-noble title).
/// </summary>
public sealed partial class ZoneWorld
{
    string? JoinBlock(ZoneClan? clan, int inviterId, int pledgeType, int targetId, bool self, bool visible,
        bool targetBusy, bool selfBusy, long nowMs)
    {
        bool academyOk = true;
        if (pledgeType == PledgeProtocol.Academy && _byId.TryGetValue(targetId, out var target))
            academyOk = target.Level <= PledgeProtocol.AcademyMaxLevel && ClassLevelOf(target) < 2;
        return ClanRules.InviteBlock(
            inClan: clan != null,
            leader: clan != null && HasPriv(clan, inviterId, PledgeProtocol.PrivInviteMember),
            pledgeType: pledgeType,
            self: self,
            targetVisible: visible,
            targetBusy: targetBusy,
            selfBusy: selfBusy,
            clanRecruitPenalty: clan != null && ClanRules.PenaltyActive(clan.CharPenaltyExpiryUnixMs, nowMs),
            targetInClan: visible && ClanOf(targetId) != null,
            targetJoinPenalty: visible && ClanRules.PenaltyActive(JoinExpiry(targetId), nowMs),
            memberCount: clan == null ? 0 : UnitCount(clan, pledgeType),
            clanLevel: clan?.Level ?? 0,
            unitExists: clan != null && (pledgeType == ClanProtocol.MainPledge || SubPledge(clan, pledgeType) != null),
            academyEligible: academyOk);
    }

    /// <summary>
    /// Drops the member's detail row, sponsor/apprentice links and captaincy. Returns an undo for a store refusal.
    /// </summary>
    Action DetachMember(ZoneClan clan, ZoneClanMember member)
    {
        var before = clan.D.Clone();
        int id = member.CharacterId;
        if (MemberDetail(clan, id) is { } d)
        {
            if (d.ApprenticeId != 0 && MemberDetail(clan, d.ApprenticeId) is { } apprentice)
                apprentice.SponsorId = 0;
            if (d.SponsorId != 0 && MemberDetail(clan, d.SponsorId) is { } sponsor)
                sponsor.ApprenticeId = 0;
        }
        clan.D.Members.Remove(id);
        foreach (var sp in clan.D.SubPledges)
        {
            if (sp.LeaderId == id)
                sp.LeaderId = 0;
        }
        return () => clan.D = before;
    }

    /// <summary>Mobius <c>removeClanMember</c>: a non-noble loses the title (noblesse is not tracked yet).</summary>
    void ClearMemberTitle(ZoneEntity entity)
    {
        if (string.IsNullOrEmpty(entity.Title))
            return;
        entity.Title = "";
        if (entity.Session != null)
            _accounts?.TrySetCharacterTitle(entity.Session.Account, entity.ObjectId, "");
    }
}
