using Fineage.Accounts;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Clan;

namespace Fineage.Zone.World;

/// <summary>
/// The clan window requests multiplexed on <see cref="NetOpcode.PledgeCommand"/>, after Mobius
/// <c>RequestGiveNickName</c>, <c>RequestPledgeSetMemberPowerGrade</c>, <c>RequestPledgePower</c>,
/// <c>RequestPledgeReorganizeMember</c>, <c>RequestPledgeSetAcademyMaster</c>, the war requests,
/// <c>RequestPledgeInfo</c> and <c>RequestPledgeMemberList</c>. Crests and halls live in their own partials.
/// </summary>
public sealed partial class ZoneWorld
{
    public async Task<string?> TryPledgeCommandAsync(
        ZoneSession session,
        PledgeCommandNetMsg command,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (command == null)
            return "bad_request";
        if (!session.InWorld)
            return "world";

        string? error = TryPledgeCommand(session, command, now.ToUnixTimeMilliseconds());
        return error;
    }

    string? TryPledgeCommand(ZoneSession session, PledgeCommandNetMsg c, long now)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var player))
            return "world";

        // Requests that do not need the caller's clan.
        switch (c.Kind)
        {
            case PledgeCommandKind.RequestCrest:
                return SendCrest(session, c.IntArg1, c.IntArg2);
            case PledgeCommandKind.RequestPledgeInfo:
                return SendPledgeInfo(session, c.IntArg1);
            case PledgeCommandKind.HallList:
                return SendHallList(session, now);
            case PledgeCommandKind.RequestAllyInfo:
            case PledgeCommandKind.AllyAnswer:
                return TryAllyCommand(session, player, null, c, now);
        }

        var clan = ClanOf(session.ObjectId);
        if (clan == null)
        {
            SelfLine(session, 212, "You are not a clan member and cannot perform this action.");
            return ClanProtocol.None;
        }

        return c.Kind switch
        {
            PledgeCommandKind.SetTitle => SetMemberTitle(session, clan, c.TargetObjectId, c.Name, c.Text),
            PledgeCommandKind.SetMemberPowerGrade => SetMemberPowerGrade(session, clan, c.Name, c.IntArg1),
            PledgeCommandKind.SetRankPrivileges => SetRankPrivileges(session, clan, c.IntArg1, c.IntArg2),
            PledgeCommandKind.RequestRankPrivileges => SendPledgeStatusToSelf(session, clan),
            PledgeCommandKind.ReorganizeMember => ReorganizeMember(session, clan, c.Name, c.IntArg1, c.Text),
            PledgeCommandKind.SetSponsor => SetSponsor(session, clan, c.Name, c.Text, c.IntArg1 != 0),
            PledgeCommandKind.DeclareWar => DeclareWar(session, clan, c.Name),
            PledgeCommandKind.StopWar => StopWar(session, clan, c.Name, surrender: false),
            PledgeCommandKind.SurrenderWar => StopWar(session, clan, c.Name, surrender: true),
            PledgeCommandKind.RequestWarList => SendWarListOk(session, clan),
            PledgeCommandKind.SetCrest => SetClanCrest(session, clan, c.Blob, large: false, now),
            PledgeCommandKind.SetLargeCrest => SetClanCrest(session, clan, c.Blob, large: true, now),
            PledgeCommandKind.RequestMemberList => SendMemberListToSelf(session, clan),
            PledgeCommandKind.HallBid => BidOnHall(session, clan, c.IntArg1, c.Amount, now),
            PledgeCommandKind.HallCancelBid => CancelHallBid(session, clan, c.IntArg1),
            >= PledgeCommandKind.AllyInvite and <= PledgeCommandKind.RequestAllyInfo => TryAllyCommand(session, player, clan, c, now),
            _ => PledgeProtocol.BadArgument
        };
    }

    string? SendWarListOk(ZoneSession session, ZoneClan clan)
    {
        SendWarList(session, clan);
        return null;
    }

    string? SendPledgeStatusToSelf(ZoneSession session, ZoneClan clan)
    {
        FanPledgeStatus([session.ObjectId], clan);
        return null;
    }

    string? SendMemberListToSelf(ZoneSession session, ZoneClan clan)
    {
        FanMemberList([session.ObjectId], clan);
        return null;
    }

    /// <summary>Mobius <c>RequestPledgeInfo</c>: name, crest and alliance of any clan by id.</summary>
    string? SendPledgeInfo(ZoneSession session, int clanId)
    {
        if (!_clan.TryGet(clanId, out var clan))
            return PledgeProtocol.NoSuchClan;
        NetBridge?.FanoutPledgeInfo([session.ObjectId], new PledgeInfoNetMsg
        {
            ClanId = clan.Id,
            Name = clan.Name,
            CrestId = clan.CrestId,
            AllyId = clan.D.AllyId,
            AllyName = clan.D.AllyName ?? "",
            AllyCrestId = clan.D.AllyCrestId
        });
        return null;
    }

    /// <summary>
    /// Mobius <c>RequestGiveNickName</c>: ASSIGN_TITLE privilege, clan level 3+, target must be an online clan member.
    /// </summary>
    string? SetMemberTitle(ZoneSession session, ZoneClan clan, int targetObjectId, string? targetName, string? title)
    {
        title = (title ?? "").Trim();
        if (title.Length > PledgeProtocol.MaxTitleLength)
            return PledgeProtocol.BadArgument;
        if (!HasPriv(clan, session.ObjectId, PledgeProtocol.PrivAssignTitle))
            return NotAuthorized(session);
        if (clan.Level < PledgeProtocol.TitleMinClanLevel)
        {
            SelfLine(session, 271, "A player can only be granted a title if the clan is level 3 or above.");
            return PledgeProtocol.Conditions;
        }
        var member = targetObjectId != 0 ? MemberOf(clan, targetObjectId) : MemberByName(clan, targetName);
        if (member == null)
        {
            SelfLine(session, 234, "The target must be a clan member.");
            return ClanProtocol.Target;
        }
        if (!_byId.TryGetValue(member.CharacterId, out var target) || target.Session is not { InWorld: true })
        {
            SelfLine(session, 145, "That player is not online.");
            return PledgeProtocol.Offline;
        }

        if (_accounts != null && !_accounts.TrySetCharacterTitle(member.Account, member.CharacterId, title))
            return ClanProtocol.Store;
        target.Title = title;
        SelfLine(target.Session, 214, "Your title has been changed.");
        FanUserInfo(target);
        QueueCharInfo(target);
        return null;
    }

    /// <summary>
    /// Mobius <c>RequestPledgeSetMemberPowerGrade</c>: MODIFY_RANKS, not the leader, not an academy member.
    /// Grades 2–8 for regular members (1 is the leader, 9 the academy).
    /// </summary>
    string? SetMemberPowerGrade(ZoneSession session, ZoneClan clan, string? name, int grade)
    {
        if (!HasPriv(clan, session.ObjectId, PledgeProtocol.PrivModifyRanks))
            return NotAuthorized(session);
        var member = MemberByName(clan, name);
        if (member == null || member.CharacterId == clan.LeaderId)
            return ClanProtocol.Target;
        if (member.PledgeType == PledgeProtocol.Academy)
        {
            SelfLine(session, 1754, "That privilege cannot be granted to a clan academy member.");
            return PledgeProtocol.AcademyMember;
        }
        if (grade <= PledgeProtocol.LeaderGrade || grade >= PledgeProtocol.AcademyGrade)
            return PledgeProtocol.BadArgument;

        var detail = EnsureMemberDetail(clan, member.CharacterId);
        int before = detail.PowerGrade;
        detail.PowerGrade = grade;
        if (!SaveClan(clan))
        {
            detail.PowerGrade = before;
            return ClanProtocol.Store;
        }
        BroadcastClan(clan);
        return null;
    }

    /// <summary>
    /// Mobius <c>RequestPledgePower</c> action 2: leader only; grade 9 (academy) may only hold warehouse + doors.
    /// </summary>
    string? SetRankPrivileges(ZoneSession session, ZoneClan clan, int grade, int mask)
    {
        if (clan.LeaderId != session.ObjectId)
            return NotAuthorized(session);
        if (grade < PledgeProtocol.MinPowerGrade + 1 || grade > PledgeProtocol.MaxPowerGrade)
            return PledgeProtocol.BadArgument;
        mask &= grade == PledgeProtocol.AcademyGrade ? PledgeProtocol.PrivAcademyMask : PledgeProtocol.PrivAll;

        bool had = clan.D.RankPrivileges.TryGetValue(grade, out int before);
        clan.D.RankPrivileges[grade] = mask;
        if (!SaveClan(clan))
        {
            if (had)
                clan.D.RankPrivileges[grade] = before;
            else
                clan.D.RankPrivileges.Remove(grade);
            return ClanProtocol.Store;
        }
        BroadcastClan(clan);
        return null;
    }

    /// <summary>
    /// Mobius <c>RequestPledgeReorganizeMember</c>: MODIFY_RANKS; moves a member to another existing unit, swapping
    /// with <paramref name="swapName"/> when given (the Interlude client always sends one when the unit is full).
    /// The leader never moves; the academy is joined only by invitation.
    /// </summary>
    string? ReorganizeMember(ZoneSession session, ZoneClan clan, string? name, int newType, string? swapName)
    {
        if (!HasPriv(clan, session.ObjectId, PledgeProtocol.PrivModifyRanks))
            return NotAuthorized(session);
        var member = MemberByName(clan, name);
        if (member == null || member.CharacterId == clan.LeaderId)
            return ClanProtocol.Target;
        if (!PledgeProtocol.IsKnownType(newType) || newType == PledgeProtocol.Academy || member.PledgeType == PledgeProtocol.Academy)
            return PledgeProtocol.BadArgument;
        if (newType != PledgeProtocol.Main && SubPledge(clan, newType) == null)
            return ClanProtocol.Pledge;
        if (member.PledgeType == newType)
            return null;
        // Captains stay in the main clan (Mobius keeps the sub-unit leader as a main member).
        if (LeaderSubPledge(clan, member.CharacterId) != 0)
            return PledgeProtocol.Conditions;

        int oldType = member.PledgeType;
        ZoneClanMember? swap = null;
        if (!string.IsNullOrWhiteSpace(swapName))
        {
            swap = MemberByName(clan, swapName);
            if (swap == null || swap.CharacterId == clan.LeaderId || swap.PledgeType != newType)
                return ClanProtocol.Target;
            if (LeaderSubPledge(clan, swap.CharacterId) != 0)
                return PledgeProtocol.Conditions;
        }
        else if (UnitCount(clan, newType) >= ClanRules.MemberLimit(clan.Level, newType))
        {
            SelfLine(session, 233, "The Academy/Royal Guard/Order of Knights is full and cannot accept new members at this time.");
            return ClanProtocol.Full;
        }

        member.PledgeType = newType;
        if (swap != null)
            swap.PledgeType = oldType;
        if (!SaveClan(clan))
        {
            member.PledgeType = oldType;
            if (swap != null)
                swap.PledgeType = newType;
            return ClanProtocol.Store;
        }
        BroadcastClan(clan, charInfo: true);
        return null;
    }

    /// <summary>
    /// Mobius <c>RequestPledgeSetAcademyMaster</c>: DISMISS_MENTEE privilege; one side must be an academy member;
    /// linking needs both sides free (sponsor and apprentice are one-to-one).
    /// </summary>
    string? SetSponsor(ZoneSession session, ZoneClan clan, string? first, string? second, bool set)
    {
        if (!HasPriv(clan, session.ObjectId, PledgeProtocol.PrivDismissMentee))
        {
            SelfLine(session, 1762, "You do not have the right to dismiss an apprentice.");
            return PledgeProtocol.Denied;
        }
        var a = MemberByName(clan, first);
        var b = MemberByName(clan, second);
        if (a == null || b == null || a.CharacterId == b.CharacterId)
            return ClanProtocol.Target;
        var (apprentice, sponsor) = a.PledgeType == PledgeProtocol.Academy ? (a, b) : (b, a);
        if (apprentice.PledgeType != PledgeProtocol.Academy || sponsor.PledgeType == PledgeProtocol.Academy)
            return PledgeProtocol.BadArgument;

        var ad = EnsureMemberDetail(clan, apprentice.CharacterId);
        var sd = EnsureMemberDetail(clan, sponsor.CharacterId);
        var before = (ad.SponsorId, ad.ApprenticeId, sd.SponsorId, sd.ApprenticeId);
        int lineId;
        string text;
        if (!set)
        {
            if (ad.SponsorId != sponsor.CharacterId)
                return ClanProtocol.Target;
            ad.SponsorId = 0;
            sd.ApprenticeId = 0;
            lineId = 1763;
            text = $"{apprentice.Name}, clan member {sponsor.Name}'s apprentice, has been removed.";
        }
        else
        {
            if (ad.SponsorId != 0 || sd.ApprenticeId != 0 || ad.ApprenticeId != 0 || sd.SponsorId != 0)
            {
                SelfLine(session, 0, "Remove previous connections first.");
                return PledgeProtocol.Conditions;
            }
            ad.SponsorId = sponsor.CharacterId;
            sd.ApprenticeId = apprentice.CharacterId;
            lineId = 1755;
            text = $"{apprentice.Name} has been designated as the apprentice of clan member {sponsor.Name}.";
        }
        if (!SaveClan(clan))
        {
            (ad.SponsorId, ad.ApprenticeId, sd.SponsorId, sd.ApprenticeId) = before;
            return ClanProtocol.Store;
        }

        var watchers = new HashSet<int> { session.ObjectId };
        if (Online(sponsor.CharacterId) != null)
            watchers.Add(sponsor.CharacterId);
        if (Online(apprentice.CharacterId) != null)
            watchers.Add(apprentice.CharacterId);
        ClanLine([.. watchers.Order()], lineId, text, sponsor.Name, apprentice.Name);
        BroadcastClan(clan);
        return null;
    }

    /// <summary>
    /// Mobius <c>Player.setPlayerClass</c>: an academy member who completes the 2nd class transfer graduates — the
    /// clan gains reputation by join level, the member leaves without any penalty and receives the Academy Circlet.
    /// </summary>
    void GraduateFromAcademy(ZoneSession session, ZoneEntity player)
    {
        var clan = ClanOf(player.ObjectId);
        var member = clan == null ? null : MemberOf(clan, player.ObjectId);
        if (clan == null || member == null || member.PledgeType != PledgeProtocol.Academy || ClassLevelOf(player) < 2)
            return;

        int joined = MemberDetail(clan, player.ObjectId)?.LevelJoinedAcademy ?? 0;
        if (joined <= 0)
            joined = player.Level;
        long create = CreateExpiry(player.ObjectId);
        var state = new CharacterClanState(member.Account, member.CharacterId, 0, 0, create);
        var undo = DetachMember(clan, member);
        var record = ToRecord(clan, clan.Members.Where(m => m.CharacterId != player.ObjectId));
        if (_accounts != null && !_accounts.UpdateClan(record, [state]))
        {
            undo();
            return;
        }

        clan.Members.Remove(member);
        ClearMemberTitle(player);
        _clan.Unlink(player.ObjectId);
        _clan.SetJoinExpiry(player.ObjectId, 0);
        player.ClanId = 0;
        SyncClanStanding(player);
        int rep = PledgeProtocol.AcademyGraduationRep(joined);
        ChangeReputation(clan, rep);

        var rest = OnlineIds(clan);
        ClanLine(rest, 191, $"Clan member {member.Name} has been expelled.", member.Name);
        ClanLine(rest, 1752, $"Since the clan has received a graduate of the Clan Academy, it has earned {rep} points toward its reputation score.", rep.ToString());
        SelfLine(session, 1749, "Congratulations! You will now graduate from the Clan Academy and leave your current clan. As a graduate of the academy, you can immediately join a clan as a regular member without being subject to any penalties.");
        QueueClearPledge(session);

        if (player.Inventory is { } inventory && _game.Items.TryGet(PledgeProtocol.AcademyCircletItemId, out _))
        {
            var before = new HashSet<int>(inventory.Items.Select(i => i.ObjectId));
            var touches = new List<Fineage.Game.Quests.BagTouch>();
            foreach (var piece in inventory.AddPieces(PledgeProtocol.AcademyCircletItemId, 1))
                touches.Add(new Fineage.Game.Quests.BagTouch(before.Contains(piece.ObjectId)
                    ? Fineage.Game.Quests.BagTouchKind.Modify
                    : Fineage.Game.Quests.BagTouchKind.Add, piece));
            SaveInventory(session, inventory);
        }
        FanUserInfo(player);
        QueueCharInfo(player);
    }
}

public sealed partial class ZoneWorld
{
    /// <summary>Test hook: a proof-free class transfer through the village master path.</summary>
    internal bool ChangeClassForTest(ZoneSession session, int classId)
    {
        using (_affinity.Step())
        {
            if (!_byId.TryGetValue(session.ObjectId, out var player))
                return false;
            var option = new Fineage.Game.Services.ClassChangeOption("test", classId, player.ClassId, [], 0, "", "", "", "");
            return ApplyClassChange(session, player, option, 0, 0);
        }
    }

    /// <summary>Test hook: the village master create_academy / create_royal / create_knight verb.</summary>
    internal string? CreateSubUnitForTest(ZoneSession session, string name, string leaderName, int family)
    {
        using (_affinity.Step())
        {
            var clan = ClanOf(session.ObjectId);
            if (clan == null)
                return ClanProtocol.None;
            int minLevel = family == PledgeProtocol.Academy ? PledgeProtocol.AcademyMinClanLevel
                : PledgeProtocol.IsRoyal(family) ? PledgeProtocol.RoyalMinClanLevel : PledgeProtocol.KnightMinClanLevel;
            return CreateSubUnit(session, clan, name, leaderName, family, minLevel);
        }
    }

    /// <summary>Test hook: the village master change_clan_leader request, stored for the next start.</summary>
    internal bool RequestDelegationForTest(int clanId, int characterId)
    {
        using (_affinity.Step())
        {
            if (!_clan.TryGet(clanId, out var clan) || MemberOf(clan, characterId) == null)
                return false;
            clan.D.NewLeaderId = characterId;
            return SaveClan(clan);
        }
    }

    internal bool PlayerAutoAttackableForTest(ZoneEntity attacker, ZoneEntity target)
    {
        using (_affinity.Step())
            return PlayerAutoAttackable(attacker, target);
    }
}
