using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Clan;

namespace Fineage.Zone.World;

/// <summary>
/// Alliances after Mobius <c>Clan.createAlly / dissolveAlly / checkAllyJoinCondition</c>, <c>RequestJoinAlly</c>,
/// <c>RequestAnswerJoinAlly</c>, <c>AllyLeave</c>, <c>AllyDismiss</c>, <c>RequestDismissAlly</c>,
/// <c>RequestSetAllyCrest</c> and <c>RequestAllyInfo</c>. The alliance id is the leader clan's id; every member clan
/// carries the id, name and crest. Penalties (Mobius defaults): leave → 1 day before joining another alliance,
/// expelled → 1 day, expelling → the leader may not accept for 1 day, dissolving → no new alliance for 10 days.
/// </summary>
public sealed partial class ZoneWorld
{
    const long DayMs = 24L * 3600 * 1000;
    const int MinAllyNameLength = 2;
    const int MaxAllyNameLength = 16;

    readonly record struct PendingAllyInvite(int InviterObjectId, int AllyId, long ExpiresUnixMs);
    readonly Dictionary<int, PendingAllyInvite> _pendingAllyInvites = [];

    /// <summary>Test hook: the alliance of a clan (id, name, crest, penalty type).</summary>
    internal (int AllyId, string? Name, int CrestId, int PenaltyType) AllyOfForTest(int clanId)
    {
        lock (_gate)
            return _clans.TryGetValue(clanId, out var c) ? (c.D.AllyId, c.D.AllyName, c.D.AllyCrestId, c.D.AllyPenaltyType) : default;
    }

    /// <summary>Test hook: village master <c>create_ally</c>.</summary>
    internal string? CreateAllyForTest(ZoneSession session, string name)
    {
        lock (_gate)
            return CreateAlly(session, ClanOf(session.ObjectId), name, NowUnixMs());
    }

    IEnumerable<ZoneClan> AllyClans(int allyId) =>
        allyId == 0 ? [] : _clans.Values.Where(c => c.D.AllyId == allyId).OrderBy(c => c.Id);

    int[] AllyOnlineIds(int allyId) => AllyClans(allyId).SelectMany(OnlineIds).ToArray();

    bool IsAllyLeader(ZoneClan clan, int characterId) =>
        clan.D.AllyId != 0 && clan.D.AllyId == clan.Id && clan.LeaderId == characterId;

    void BroadcastAlly(IEnumerable<ZoneClan> clans)
    {
        foreach (var clan in clans.ToList())
        {
            BroadcastClan(clan, charInfo: true);
            BroadcastClanRelations(clan);
        }
    }

    static void SetAlly(ZoneClan clan, int allyId, string? name, int crestId)
    {
        clan.D.AllyId = allyId;
        clan.D.AllyName = name;
        clan.D.AllyCrestId = crestId;
    }

    static void SetAllyPenalty(ZoneClan clan, long expiry, int type)
    {
        clan.D.AllyPenaltyExpiryUnixMs = expiry;
        clan.D.AllyPenaltyType = type;
    }

    string? TryAllyCommand(ZoneSession session, ZoneEntity player, ZoneClan? clan, PledgeCommandNetMsg c, long now)
    {
        clan ??= ClanOf(session.ObjectId);
        if (c.Kind == PledgeCommandKind.AllyAnswer)
            return AnswerAlly(session, clan, c.IntArg1 != 0, now);
        if (clan == null)
        {
            SelfLine(session, 212, "You are not a clan member and cannot perform this action.");
            return ClanProtocol.None;
        }
        return c.Kind switch
        {
            PledgeCommandKind.RequestAllyInfo => SendAllyInfo(session, clan),
            PledgeCommandKind.AllyInvite => InviteToAlly(session, player, clan, c.TargetObjectId, now),
            PledgeCommandKind.AllyLeave => LeaveAlly(session, clan, now),
            PledgeCommandKind.AllyExpel => ExpelFromAlly(session, clan, c.Name, now),
            PledgeCommandKind.AllyDissolve => DissolveAlly(session, clan, now),
            PledgeCommandKind.SetAllyCrest => SetAllyCrest(session, clan, c.Blob),
            _ => PledgeProtocol.BadArgument
        };
    }

    /// <summary>Mobius <c>Clan.createAlly</c> (village master <c>create_ally</c>).</summary>
    string? CreateAlly(ZoneSession session, ZoneClan? clan, string name, long now)
    {
        if (clan == null || clan.LeaderId != session.ObjectId)
        {
            SelfLine(session, 504, "Only clan leaders may create alliances.");
            return PledgeProtocol.Denied;
        }
        if (clan.D.AllyId != 0)
        {
            SelfLine(session, 502, "You already belong to another alliance.");
            return PledgeProtocol.Conditions;
        }
        if (clan.Level < PledgeProtocol.AllyMinClanLevel)
        {
            SelfLine(session, 549, "To create an alliance, your clan must be Level 5 or higher.");
            return PledgeProtocol.Conditions;
        }
        if (ClanRules.PenaltyActive(clan.D.AllyPenaltyExpiryUnixMs, now) && clan.D.AllyPenaltyType == PledgeProtocol.AllyPenaltyDissolveAlly)
        {
            SelfLine(session, 505, "You cannot create a new alliance within 10 days after dissolution.");
            return PledgeProtocol.Conditions;
        }
        if (clan.DissolvingExpiryUnixMs > now)
        {
            SelfLine(session, 550, "You may not create an alliance during the term of dissolution postponement.");
            return PledgeProtocol.Dissolving;
        }
        name = (name ?? "").Trim();
        if (!ClanRules.IsAlphaNumeric(name))
        {
            SelfLine(session, 506, "Incorrect alliance name. Please try again.");
            return ClanProtocol.Name;
        }
        if (name.Length < MinAllyNameLength || name.Length > MaxAllyNameLength)
        {
            SelfLine(session, 507, "Incorrect length for an alliance name.");
            return ClanProtocol.NameLength;
        }
        if (_clans.Values.Any(other => string.Equals(other.D.AllyName, name, StringComparison.OrdinalIgnoreCase)))
        {
            SelfLine(session, 508, "This alliance name already exists.");
            return ClanProtocol.NameTaken;
        }

        var before = (clan.D.AllyId, clan.D.AllyName, clan.D.AllyCrestId, clan.D.AllyPenaltyExpiryUnixMs, clan.D.AllyPenaltyType);
        SetAlly(clan, clan.Id, name, 0);
        SetAllyPenalty(clan, 0, 0);
        if (!SaveClan(clan))
        {
            (clan.D.AllyId, clan.D.AllyName, clan.D.AllyCrestId, clan.D.AllyPenaltyExpiryUnixMs, clan.D.AllyPenaltyType) = before;
            return ClanProtocol.Store;
        }
        SelfLine(session, 0, $"Alliance {name} has been created.");
        BroadcastAlly([clan]);
        return null;
    }

    /// <summary>Mobius <c>RequestJoinAlly</c> + <c>checkAllyJoinCondition</c>.</summary>
    string? InviteToAlly(ZoneSession session, ZoneEntity player, ZoneClan clan, int targetObjectId, long now)
    {
        string? block = AllyJoinBlock(session, clan, targetObjectId, now, out var targetClan);
        if (block != null)
            return block;
        if (_pendingAllyInvites.TryGetValue(targetObjectId, out var pending) && pending.ExpiresUnixMs > now)
        {
            SelfLine(session, 153, $"{_byId[targetObjectId].Name} is busy. Please try again later.", _byId[targetObjectId].Name);
            return ClanProtocol.Busy;
        }
        _pendingAllyInvites[targetObjectId] = new PendingAllyInvite(session.ObjectId, clan.D.AllyId, ClanRules.InviteDeadline(now));
        string ally = clan.D.AllyName ?? "";
        ClanLine([targetObjectId], 527, $"{ally} leader, {session.Name}, has requested an alliance.", ally, session.Name);
        NetBridge?.FanoutAllyAskJoin([targetObjectId], new AllyAskJoinNetMsg
        {
            RequestorObjectId = session.ObjectId,
            RequestorName = session.Name,
            AllyName = ally
        });
        return null;
    }

    string? AllyJoinBlock(ZoneSession session, ZoneClan leaderClan, int targetObjectId, long now, out ZoneClan? targetClan)
    {
        targetClan = null;
        if (!IsAllyLeader(leaderClan, session.ObjectId))
        {
            SelfLine(session, 464, "This feature is only available alliance leaders.");
            return PledgeProtocol.Denied;
        }
        if (ClanRules.PenaltyActive(leaderClan.D.AllyPenaltyExpiryUnixMs, now) && leaderClan.D.AllyPenaltyType == PledgeProtocol.AllyPenaltyDismissClan)
        {
            SelfLine(session, 467, "You may not accept any clan within a day after expelling another clan.");
            return PledgeProtocol.Conditions;
        }
        if (!_byId.TryGetValue(targetObjectId, out var target) || target.Kind != ZoneEntityKind.Player || target.Session is not { InWorld: true })
        {
            SelfLine(session, 152, "You have invited the wrong target.");
            return ClanProtocol.Target;
        }
        if (targetObjectId == session.ObjectId)
        {
            SelfLine(session, 4, "You cannot ask yourself to apply to a clan.");
            return ClanProtocol.Self;
        }
        targetClan = ClanOf(targetObjectId);
        if (targetClan == null)
        {
            SelfLine(session, 234, "The target must be a clan member.");
            return ClanProtocol.Target;
        }
        if (targetClan.LeaderId != targetObjectId)
        {
            SelfLine(session, 9, $"{target.Name} is not a clan leader.", target.Name);
            return ClanProtocol.Leader;
        }
        if (targetClan.D.AllyId != 0)
        {
            SelfLine(session, 691, $"{targetClan.Name} clan is already a member of {targetClan.D.AllyName} alliance.", targetClan.Name, targetClan.D.AllyName ?? "");
            return ClanProtocol.TargetClan;
        }
        if (ClanRules.PenaltyActive(targetClan.D.AllyPenaltyExpiryUnixMs, now))
        {
            if (targetClan.D.AllyPenaltyType == PledgeProtocol.AllyPenaltyClanLeaved)
            {
                SelfLine(session, 761, $"{targetClan.Name} clan cannot join the alliance because one day has not yet passed since it left another alliance.", targetClan.Name);
                return ClanProtocol.JoinPenalty;
            }
            if (targetClan.D.AllyPenaltyType == PledgeProtocol.AllyPenaltyClanDismissed)
            {
                SelfLine(session, 468, "A clan that has withdrawn or been expelled cannot enter into an alliance within one day of withdrawal or expulsion.");
                return ClanProtocol.JoinPenalty;
            }
        }
        if (ClanInSiege(leaderClan.Id) && ClanInSiege(targetClan.Id))
        {
            SelfLine(session, 723, "The opposing clan is participating in a siege battle.");
            return PledgeProtocol.Conditions;
        }
        if (Declared(leaderClan.Id, targetClan.Id) || Declared(targetClan.Id, leaderClan.Id))
        {
            SelfLine(session, 469, "You may not ally with a clan you are currently at war with. That would be diabolical and treacherous.");
            return PledgeProtocol.AlreadyAtWar;
        }
        if (AllyClans(leaderClan.D.AllyId).Count() >= PledgeProtocol.AllyMaxClans)
        {
            SelfLine(session, 466, "You have exceeded the limit.");
            return ClanProtocol.Full;
        }
        return null;
    }

    /// <summary>Mobius <c>RequestAnswerJoinAlly</c>: conditions are re-checked on accept.</summary>
    string? AnswerAlly(ZoneSession session, ZoneClan? clan, bool accept, long now)
    {
        if (!_pendingAllyInvites.Remove(session.ObjectId, out var pending) || pending.ExpiresUnixMs <= now)
            return ClanProtocol.Invite;
        var requestor = Online(pending.InviterObjectId);
        if (requestor == null || !_clans.TryGetValue(pending.AllyId, out var leaderClan) || ClanOf(requestor.ObjectId) != leaderClan)
            return ClanProtocol.Invite;
        if (!accept)
        {
            SelfLine(session, 478, "No response. Your entrance to the alliance has been cancelled.");
            SelfLine(requestor, 477, "No response. Invitation to join an alliance has been cancelled.");
            return null;
        }
        string? block = AllyJoinBlock(requestor, leaderClan, session.ObjectId, now, out var targetClan);
        if (block != null || targetClan == null)
            return block ?? ClanProtocol.Invite;

        var before = (targetClan.D.AllyId, targetClan.D.AllyName, targetClan.D.AllyCrestId, targetClan.D.AllyPenaltyExpiryUnixMs, targetClan.D.AllyPenaltyType);
        SetAlly(targetClan, leaderClan.D.AllyId, leaderClan.D.AllyName, leaderClan.D.AllyCrestId);
        SetAllyPenalty(targetClan, 0, 0);
        if (!SaveClan(targetClan))
        {
            (targetClan.D.AllyId, targetClan.D.AllyName, targetClan.D.AllyCrestId, targetClan.D.AllyPenaltyExpiryUnixMs, targetClan.D.AllyPenaltyType) = before;
            return ClanProtocol.Store;
        }
        SelfLine(session, 517, "You have accepted the alliance.");
        SelfLine(requestor, 0, $"{targetClan.Name} has joined the alliance.");
        BroadcastAlly(AllyClans(leaderClan.D.AllyId));
        return null;
    }

    /// <summary>Mobius <c>AllyLeave</c>.</summary>
    string? LeaveAlly(ZoneSession session, ZoneClan clan, long now)
    {
        if (clan.LeaderId != session.ObjectId)
        {
            SelfLine(session, 470, "Only the clan leader may apply for withdrawal from the alliance.");
            return PledgeProtocol.Denied;
        }
        if (clan.D.AllyId == 0)
        {
            SelfLine(session, 465, "You are not currently allied with any clans.");
            return ClanProtocol.None;
        }
        if (clan.D.AllyId == clan.Id)
        {
            SelfLine(session, 471, "Alliance leaders cannot withdraw.");
            return ClanProtocol.LeaderLeave;
        }
        int allyId = clan.D.AllyId;
        var rest = AllyClans(allyId).Where(c => c.Id != clan.Id).ToList();
        var before = clan.D.Clone();
        SetAlly(clan, 0, null, 0);
        SetAllyPenalty(clan, now + PledgeProtocol.AllyJoinDaysWhenLeaved * DayMs, PledgeProtocol.AllyPenaltyClanLeaved);
        if (!SaveClan(clan))
        {
            clan.D = before;
            return ClanProtocol.Store;
        }
        SelfLine(session, 519, "You have withdrawn from the alliance.");
        ClanLine(rest.SelectMany(OnlineIds).ToArray(), 0, $"{clan.Name} clan has withdrawn from the alliance.");
        BroadcastAlly(rest.Append(clan));
        return null;
    }

    /// <summary>Mobius <c>AllyDismiss</c>.</summary>
    string? ExpelFromAlly(ZoneSession session, ZoneClan leaderClan, string? clanName, long now)
    {
        if (leaderClan.D.AllyId == 0)
        {
            SelfLine(session, 465, "You are not currently allied with any clans.");
            return ClanProtocol.None;
        }
        if (!IsAllyLeader(leaderClan, session.ObjectId))
        {
            SelfLine(session, 464, "This feature is only available alliance leaders.");
            return PledgeProtocol.Denied;
        }
        var clan = string.IsNullOrWhiteSpace(clanName)
            ? null
            : _clans.Values.FirstOrDefault(c => string.Equals(c.Name, clanName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (clan == null)
        {
            SelfLine(session, 474, "That clan does not exist.");
            return PledgeProtocol.NoSuchClan;
        }
        if (clan.Id == leaderClan.Id)
        {
            SelfLine(session, 471, "Alliance leaders cannot withdraw.");
            return ClanProtocol.LeaderLeave;
        }
        if (clan.D.AllyId != leaderClan.D.AllyId)
        {
            SelfLine(session, 473, "Different alliance.");
            return PledgeProtocol.BadArgument;
        }
        var beforeLeader = leaderClan.D.Clone();
        var beforeClan = clan.D.Clone();
        SetAllyPenalty(leaderClan, now + PledgeProtocol.AllyAcceptDaysWhenDismissed * DayMs, PledgeProtocol.AllyPenaltyDismissClan);
        SetAlly(clan, 0, null, 0);
        SetAllyPenalty(clan, now + PledgeProtocol.AllyJoinDaysWhenDismissed * DayMs, PledgeProtocol.AllyPenaltyClanDismissed);
        if (!SaveClan(leaderClan) || !SaveClan(clan))
        {
            leaderClan.D = beforeLeader;
            clan.D = beforeClan;
            return ClanProtocol.Store;
        }
        SelfLine(session, 521, "You have succeeded in expelling a clan.");
        ClanLine(OnlineIds(clan), 0, "Your clan has been expelled from the alliance.");
        BroadcastAlly(AllyClans(leaderClan.D.AllyId).Append(clan));
        return null;
    }

    /// <summary>Mobius <c>Clan.dissolveAlly</c> (RequestDismissAlly / village master <c>dissolve_ally</c>).</summary>
    string? DissolveAlly(ZoneSession session, ZoneClan? clan, long now)
    {
        if (clan == null || clan.D.AllyId == 0)
        {
            SelfLine(session, 465, "You are not currently allied with any clans.");
            return ClanProtocol.None;
        }
        if (!IsAllyLeader(clan, session.ObjectId))
        {
            SelfLine(session, 464, "This feature is only available alliance leaders.");
            return PledgeProtocol.Denied;
        }
        var members = AllyClans(clan.D.AllyId).ToList();
        if (members.Any(c => ClanInSiege(c.Id)))
        {
            SelfLine(session, 722, "You cannot dissolve an alliance while an affiliated clan is participating in a siege battle.");
            return PledgeProtocol.Conditions;
        }
        ClanLine(members.SelectMany(OnlineIds).ToArray(), 523, "The alliance has been dissolved.");
        int crestId = clan.D.AllyCrestId;
        foreach (var other in members.Where(c => c.Id != clan.Id))
        {
            SetAlly(other, 0, null, 0);
            SetAllyPenalty(other, 0, 0);
            SaveClan(other);
        }
        SetAlly(clan, 0, null, 0);
        SetAllyPenalty(clan, now + PledgeProtocol.AllyCreateDaysWhenDissolved * DayMs, PledgeProtocol.AllyPenaltyDissolveAlly);
        SaveClan(clan);
        DropCrest(crestId);
        foreach (var pair in _pendingAllyInvites.Where(p => p.Value.AllyId == clan.Id).ToList())
            _pendingAllyInvites.Remove(pair.Key);
        BroadcastAlly(members);
        return null;
    }

    /// <summary>Mobius <c>RequestSetAllyCrest</c>: 8x12 (≤ 192 bytes), alliance leader only; empty data deletes.</summary>
    string? SetAllyCrest(ZoneSession session, ZoneClan clan, byte[]? data)
    {
        data ??= [];
        if (data.Length > PledgeProtocol.MaxAllyCrestBytes)
        {
            SelfLine(session, 476, "Please adjust the image size to 8x12.");
            return PledgeProtocol.CrestSize;
        }
        if (!IsAllyLeader(clan, session.ObjectId))
        {
            SelfLine(session, 464, "This feature is only available alliance leaders.");
            return PledgeProtocol.Denied;
        }
        int oldId = clan.D.AllyCrestId;
        int newId = 0;
        if (data.Length > 0)
        {
            var crest = StoreCrest(PledgeProtocol.CrestKindAlly, data);
            if (crest == null)
                return ClanProtocol.Store;
            newId = crest.Id;
        }
        else if (oldId == 0)
            return null;

        var members = AllyClans(clan.D.AllyId).ToList();
        foreach (var member in members)
        {
            member.D.AllyCrestId = newId;
            SaveClan(member);
        }
        DropCrest(oldId);
        if (newId != 0)
            SelfLine(session, 0, "The crest was successfully registered.");
        BroadcastAlly(members);
        return null;
    }

    /// <summary>Mobius <c>RequestAllyInfo</c> → <c>AllianceInfo</c>.</summary>
    string? SendAllyInfo(ZoneSession session, ZoneClan clan)
    {
        int allyId = clan.D.AllyId;
        if (allyId == 0)
        {
            SelfLine(session, 465, "You are not currently allied with any clans.");
            return ClanProtocol.None;
        }
        var rows = AllyClans(allyId).Select(c => new AllyClanNet
        {
            ClanId = c.Id,
            Name = c.Name,
            Level = c.Level,
            LeaderName = MemberOf(c, c.LeaderId)?.Name ?? "",
            MemberCount = c.Members.Count,
            OnlineCount = OnlineIds(c).Length,
            CrestId = c.CrestId
        }).ToArray();
        NetBridge?.FanoutAllyInfo([session.ObjectId], new AllyInfoNetMsg
        {
            AllyId = allyId,
            Name = clan.D.AllyName ?? "",
            CrestId = clan.D.AllyCrestId,
            LeaderClanId = allyId,
            Clans = rows
        });
        return null;
    }

}
