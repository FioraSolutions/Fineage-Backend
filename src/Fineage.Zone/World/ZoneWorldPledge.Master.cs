using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Zone.Clan;
using Fineage.Zone.NpcHtml;

namespace Fineage.Zone.World;

/// <summary>
/// Village master clan pages and <c>npc_%objectId%_*</c> clan bypasses after Mobius <c>ClanMaster</c>, <c>AllianceMaster</c>
/// and <c>VillageMaster.onBypassFeedback</c> (create_clan, increase_clan_level, dissolve_clan, recover_clan,
/// change_clan_leader, cancel_clan_leader_change, create_academy/royal/knight, rename_pledge, assign_subpl_leader,
/// learn_clan_skills). Pages: data/html/village_master/ClanMaster, AllianceMaster.
/// </summary>
public sealed partial class ZoneWorld
{
    const string ClanMasterScript = "ClanMaster";
    const string AllianceMasterScript = "AllianceMaster";

    /// <summary>Mobius ClanMaster LEADER_REQUIRED: these pages fall back to the "-no" page for non-leaders.</summary>
    static readonly Dictionary<string, string> ClanMasterLeaderPages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["9000-03.htm"] = "9000-03-no.htm",
        ["9000-04.htm"] = "9000-04-no.htm",
        ["9000-05.htm"] = "9000-05-no.htm",
        ["9000-07.htm"] = "9000-07-no.htm",
        ["9000-12a.htm"] = "9000-07-no.htm",
        ["9000-12b.htm"] = "9000-07-no.htm",
        ["9000-13a.htm"] = "9000-07-no.htm",
        ["9000-13b.htm"] = "9000-07-no.htm",
        ["9000-14a.htm"] = "9000-07-no.htm",
        ["9000-14b.htm"] = "9000-07-no.htm",
        ["9000-15.htm"] = "9000-07-no.htm"
    };

    static readonly HashSet<string> ClanMasterVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "create_clan", "increase_clan_level", "dissolve_clan", "recover_clan", "change_clan_leader",
        "cancel_clan_leader_change", "create_academy", "create_royal", "create_knight", "rename_pledge",
        "assign_subpl_leader", "learn_clan_skills"
    };

    static bool IsVillageMasterNpc(ZoneEntity npc) =>
        npc.NpcType != null && npc.NpcType.StartsWith("VillageMaster", StringComparison.Ordinal);

    bool TryClanMasterScript(ZoneSession session, ZoneEntity player, ZoneEntity npc, string script, string evt)
    {
        bool clanMaster = string.Equals(script, ClanMasterScript, StringComparison.OrdinalIgnoreCase);
        bool allyMaster = string.Equals(script, AllianceMasterScript, StringComparison.OrdinalIgnoreCase);
        if (!clanMaster && !allyMaster)
            return false;

        string folder = clanMaster ? ClanMasterScript : AllianceMasterScript;
        string page = evt.Trim();
        if (page.Length == 0 || !page.EndsWith(".htm", StringComparison.OrdinalIgnoreCase) ||
            page.Contains('/') || page.Contains('\\') || page.Contains(".."))
            page = clanMaster ? "9000-01.htm" : "9001-01.htm";
        if (clanMaster && ClanMasterLeaderPages.TryGetValue(page, out var fallback) && !player.ClanLeader)
            page = fallback;
        SendClanPage(session, player, npc, $"village_master/{folder}/{page}");
        return true;
    }

    void SendClanPage(ZoneSession session, ZoneEntity player, ZoneEntity npc, string path)
    {
        if (!_html.TryGet(path, out var html))
            html = QuestBypassHandler.Unavailable;
        SendHtml(session, player, npc, html);
    }

    /// <summary>Clan verbs on a village master; false when <paramref name="verb"/> is not one of them.</summary>
    bool TryClanMasterVerb(ZoneSession session, ZoneEntity player, ZoneEntity npc, string verb, out string? error)
    {
        error = null;
        var parts = verb.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || !ClanMasterVerbs.Contains(parts[0]) || !IsVillageMasterNpc(npc))
            return false;

        string command = parts[0].ToLowerInvariant();
        string arg1 = parts.Length > 1 ? parts[1] : "";
        string arg2 = parts.Length > 2 ? parts[2] : "";
        long now = _clock().ToUnixTimeMilliseconds();
        var clan = ClanOf(session.ObjectId);
        error = command switch
        {
            "create_clan" => CreateClanAtMaster(session, arg1, arg2, now),
            "learn_clan_skills" => ShowPledgeSkills(session, player, npc),
            _ when clan == null || clan.LeaderId != session.ObjectId => NotAuthorized(session),
            "increase_clan_level" => IncreaseClanLevel(session, player, clan!, now),
            "dissolve_clan" => DissolveAtMaster(session, player, clan!, now),
            "recover_clan" => RecoverClan(session, clan!, now),
            "change_clan_leader" => DelegateLeader(session, player, npc, clan!, arg1),
            "cancel_clan_leader_change" => CancelDelegation(session, player, npc, clan!),
            "create_academy" => CreateSubUnit(session, clan!, arg1, "", PledgeProtocol.Academy, PledgeProtocol.AcademyMinClanLevel),
            "create_royal" => CreateSubUnit(session, clan!, arg1, arg2, PledgeProtocol.Royal1, PledgeProtocol.RoyalMinClanLevel),
            "create_knight" => CreateSubUnit(session, clan!, arg1, arg2, PledgeProtocol.Knight1, PledgeProtocol.KnightMinClanLevel),
            "rename_pledge" => RenameSubUnit(session, clan!, arg1, arg2),
            "assign_subpl_leader" => AssignCaptain(session, clan!, arg1, arg2),
            _ => null
        };
        return true;
    }

    string NotAuthorized(ZoneSession session)
    {
        SelfLine(session, 794, "You are not authorized to do that.");
        return PledgeProtocol.Denied;
    }

    string? CreateClanAtMaster(ZoneSession session, string name, string extra, long now)
    {
        if (name.Length == 0)
        {
            SelfLine(session, 188, "Please create your clan name.");
            return ClanProtocol.Name;
        }
        if (extra.Length > 0)
        {
            SelfLine(session, 261, "Clan name is invalid.");
            return ClanProtocol.Name;
        }
        return TryClanCreate(session, name, now);
    }

    /// <summary>Mobius <c>Clan.levelUpClan</c>.</summary>
    string? IncreaseClanLevel(ZoneSession session, ZoneEntity player, ZoneClan clan, long now)
    {
        if (clan.DissolvingExpiryUnixMs > now)
        {
            SelfLine(session, 551, "As you are currently schedule for clan dissolution, your clan level cannot be increased.");
            return PledgeProtocol.Dissolving;
        }

        var cost = PledgeProtocol.LevelUp(clan.Level);
        var inventory = player.Inventory;
        bool ok = cost.Exists && inventory != null &&
                  player.Sp >= cost.Sp &&
                  (cost.Adena <= 0 || inventory.CountOf(PledgeProtocol.AdenaItemId) >= cost.Adena) &&
                  (cost.ItemId <= 0 || inventory.CountOf(cost.ItemId) >= 1) &&
                  clan.D.Reputation >= cost.Reputation &&
                  clan.Members.Count >= cost.MinMembers;
        if (!ok)
        {
            SelfLine(session, 1790, "The conditions necessary to increase the clan's level have not been met.");
            return PledgeProtocol.Conditions;
        }

        var changes = new List<InventoryUpdateEntryMsg>();
        if (cost.Adena > 0 && !Take(inventory!, PledgeProtocol.AdenaItemId, (int)cost.Adena, changes))
            return PledgeProtocol.Conditions;
        if (cost.ItemId > 0 && !Take(inventory!, cost.ItemId, 1, changes))
            return PledgeProtocol.Conditions;
        if (cost.Sp > 0)
        {
            player.Sp -= cost.Sp;
            _persist.SaveProgress(session.Account, session.ObjectId, player.Level, player.Exp, player.Sp, player.MaxHp, player.CurHp, player.MaxMp, player.CurMp);
        }
        if (changes.Count > 0)
        {
            FanInventory(session, Collapse(changes));
            SaveInventory(session, inventory!);
        }
        if (cost.Reputation > 0)
        {
            clan.D.Reputation -= cost.Reputation;
            SelfLine(session, 1787, $"{cost.Reputation} points have been deducted from the clan's reputation score.", cost.Reputation.ToString());
        }

        clan.Level++;
        if (!SaveClan(clan))
            return ClanProtocol.Store;

        ClanLine(OnlineIds(clan), 274, "Your clan's skill level has increased.");
        NetBridge?.FanoutMagicSkillUse(WatchersOf(player), new MagicSkillUseMsg
        {
            CasterObjectId = player.ObjectId,
            TargetObjectId = player.ObjectId,
            SkillId = PledgeProtocol.LevelUpSkillId,
            SkillLevel = 1
        });
        BroadcastClan(clan, charInfo: true);
        return null;
    }

    int[] WatchersOf(ZoneEntity player)
    {
        var list = new List<int> { player.ObjectId };
        foreach (int id in player.Knownlist)
        {
            if (_byId.TryGetValue(id, out var other) && other.Session != null)
                list.Add(id);
        }
        return list.ToArray();
    }

    /// <summary>Mobius <c>VillageMaster.dissolveClan</c>: refused while allied, at war, owning a hall or castle, or siege-registered.</summary>
    string? DissolveAtMaster(ZoneSession session, ZoneEntity player, ZoneClan clan, long now)
    {
        if (clan.D.AllyId != 0)
        {
            SelfLine(session, 554, "You cannot disperse the clans in your alliance.");
            return PledgeProtocol.Conditions;
        }
        if (IsAtWar(clan))
        {
            SelfLine(session, 264, "You cannot dissolve a clan while engaged in a war.");
            return PledgeProtocol.Conditions;
        }
        if (clan.CastleId != 0 || clan.D.HallId != 0)
        {
            SelfLine(session, 266, "You cannot dissolve a clan while owning a clan hall or castle.");
            return PledgeProtocol.Conditions;
        }
        if (ClanInSiege(clan.Id))
        {
            SelfLine(session, 265, "You cannot dissolve a clan during a siege or while protecting a castle.");
            return PledgeProtocol.Conditions;
        }
        if (clan.DissolvingExpiryUnixMs > now)
        {
            SelfLine(session, 263, "You have already requested the dissolution of your clan.");
            return ClanProtocol.Dissolving;
        }
        return TryClanDissolve(session, now);
    }

    string? RecoverClan(ZoneSession session, ZoneClan clan, long now) =>
        clan.DissolvingExpiryUnixMs > now ? TryClanCancelDissolve(session, now) : null;

    /// <summary>Mobius change_clan_leader with AltClanLeaderInstantActivation=false: stored, applied at the next start.</summary>
    string? DelegateLeader(ZoneSession session, ZoneEntity player, ZoneEntity npc, ZoneClan clan, string name)
    {
        if (name.Length == 0 || string.Equals(name, player.Name, StringComparison.OrdinalIgnoreCase))
            return null;
        var member = MemberByName(clan, name);
        if (member == null)
        {
            SelfLine(session, 2, $"{name} does not exist.", name);
            return PledgeProtocol.BadArgument;
        }
        if (Online(member.CharacterId) == null)
        {
            SelfLine(session, 145, "That player is not currently online.");
            return PledgeProtocol.Offline;
        }
        if (member.PledgeType == PledgeProtocol.Academy)
        {
            SelfLine(session, 1754, "That privilege cannot be granted to a clan academy member.");
            return PledgeProtocol.AcademyMember;
        }
        if (clan.D.NewLeaderId != 0)
        {
            SendClanPage(session, player, npc, "village_master/ClanMaster/9000-07-in-progress.htm");
            return null;
        }
        clan.D.NewLeaderId = member.CharacterId;
        if (!SaveClan(clan))
            return ClanProtocol.Store;
        SendClanPage(session, player, npc, "village_master/ClanMaster/9000-07-success.htm");
        BroadcastClan(clan);
        return null;
    }

    string? CancelDelegation(ZoneSession session, ZoneEntity player, ZoneEntity npc, ZoneClan clan)
    {
        if (clan.D.NewLeaderId == 0)
        {
            SendHtml(session, player, npc, "<html><body>You don't have clan leader delegation applications submitted yet!</body></html>");
            return null;
        }
        clan.D.NewLeaderId = 0;
        if (!SaveClan(clan))
            return ClanProtocol.Store;
        SendClanPage(session, player, npc, "village_master/ClanMaster/9000-07-canceled.htm");
        BroadcastClan(clan);
        return null;
    }

    /// <summary>
    /// Mobius <c>Clan.setNewLeader</c> applied from a stored delegation at load (server start). Old leader keeps
    /// the main pledge at grade 5 (Mobius setPowerGrade(SUBUNIT default)).
    /// </summary>
    void ApplyLeaderDelegation(ZoneClan clan)
    {
        int next = clan.D.NewLeaderId;
        clan.D.NewLeaderId = 0;
        var member = MemberOf(clan, next);
        if (member == null || member.PledgeType == PledgeProtocol.Academy)
        {
            SaveClan(clan);
            return;
        }
        int previous = clan.LeaderId;
        clan.LeaderId = next;
        EnsureMemberDetail(clan, previous).PowerGrade = PledgeProtocol.NewMemberGrade;
        EnsureMemberDetail(clan, next).PowerGrade = 0;
        // A captain who becomes leader stops captaining (Mobius clears the sub-pledge leader slot).
        foreach (var sp in clan.D.SubPledges.Where(sp => sp.LeaderId == next))
            sp.LeaderId = 0;
        SaveClan(clan);
        ZoneLog.Info($"[clan] leader delegation applied clan={clan.Id} {previous}->{next}");
    }

    /// <summary>Test hook: run the start-of-server delegation step without reloading the world.</summary>
    internal bool ApplyLeaderDelegationForTest(int clanId)
    {
        lock (_gate)
        {
            if (!_clans.TryGetValue(clanId, out var clan) || clan.D.NewLeaderId == 0)
                return false;
            ApplyLeaderDelegation(clan);
            BroadcastClan(clan, charInfo: true);
            return true;
        }
    }

    bool ValidUnitName(ZoneSession session, string name)
    {
        if (!ClanRules.IsAlphaNumeric(name) || name.Length < ClanProtocol.MinNameLength)
        {
            SelfLine(session, 261, "Clan name is invalid.");
            return false;
        }
        if (name.Length > ClanProtocol.MaxNameLength)
        {
            SelfLine(session, 262, "Clan name's length is incorrect.");
            return false;
        }
        return true;
    }

    bool UnitNameTaken(string name) =>
        _clans.Values.Any(c => c.D.SubPledges.Any(sp => string.Equals(sp.Name, name, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Mobius <c>VillageMaster.createSubPledge</c> + <c>Clan.createSubPledge</c>.</summary>
    string? CreateSubUnit(ZoneSession session, ZoneClan clan, string name, string leaderName, int family, int minLevel)
    {
        bool academy = family == PledgeProtocol.Academy;
        if (clan.Level < minLevel)
        {
            if (academy)
                SelfLine(session, 1730, "To establish a Clan Academy, your clan must be Level 5 or higher.");
            else
                SelfLine(session, 1791, "The conditions necessary to create a military unit have not been met.");
            return PledgeProtocol.Conditions;
        }
        if (name.Length == 0)
        {
            if (academy)
                SelfLine(session, 1740, "Please enter the name of the clan academy.");
            return PledgeProtocol.BadArgument;
        }
        if (!ValidUnitName(session, name))
            return ClanProtocol.Name;
        if (UnitNameTaken(name))
        {
            if (academy)
                SelfLine(session, 5, $"{name} already exists.", name);
            else
                SelfLine(session, 1855, "Another military unit is already using that name. Please enter a different name.");
            return ClanProtocol.NameTaken;
        }

        int leaderId = 0;
        if (!academy)
        {
            var member = MemberByName(clan, leaderName);
            if (member == null || member.PledgeType != PledgeProtocol.Main || LeaderSubPledge(clan, member.CharacterId) != 0 ||
                member.CharacterId == clan.LeaderId)
            {
                if (PledgeProtocol.IsKnight(family))
                    SelfLine(session, 1850, "The Captain of the Order of Knights cannot be appointed.");
                else
                    SelfLine(session, 1851, "The Captain of the Royal Guard cannot be appointed.");
                return PledgeProtocol.BadArgument;
            }
            leaderId = member.CharacterId;
        }

        int type = NextFreeType(clan, family);
        if (type == 0)
        {
            SelfLine(session, 1791, "The conditions necessary to create a military unit have not been met.");
            return PledgeProtocol.Conditions;
        }
        int price = PledgeProtocol.IsRoyal(type) ? PledgeProtocol.RoyalGuardCost
            : PledgeProtocol.IsKnight(type) ? PledgeProtocol.KnightUnitCost : 0;
        if (price > 0 && clan.D.Reputation < price)
        {
            SelfLine(session, 1860, "The Clan Reputation Score is too low.");
            return PledgeProtocol.Reputation;
        }

        clan.D.SubPledges.Add(new SubPledgeSave { Type = type, Name = name, LeaderId = leaderId });
        if (price > 0)
        {
            clan.D.Reputation -= price;
            SelfLine(session, 1787, $"{price} points have been deducted from the clan's reputation score.", price.ToString());
        }
        if (!SaveClan(clan))
            return ClanProtocol.Store;

        if (academy)
            SelfLine(session, 1741, $"Congratulations! The {clan.Name}'s Clan Academy has been created.", clan.Name);
        else if (PledgeProtocol.IsKnight(type))
            SelfLine(session, 1794, $"The Knights of {clan.Name} have been created.", clan.Name);
        else
            SelfLine(session, 1795, $"The Royal Guard of {clan.Name} have been created.", clan.Name);
        BroadcastClan(clan);
        return null;
    }

    string? RenameSubUnit(ZoneSession session, ZoneClan clan, string typeText, string name)
    {
        if (!int.TryParse(typeText, out int type) || name.Length == 0)
            return PledgeProtocol.BadArgument;
        var unit = SubPledge(clan, type);
        if (unit == null)
        {
            SelfLine(session, 0, "Pledge don't exists.");
            return PledgeProtocol.BadArgument;
        }
        if (!ValidUnitName(session, name))
            return ClanProtocol.Name;
        unit.Name = name;
        if (!SaveClan(clan))
            return ClanProtocol.Store;
        BroadcastClan(clan);
        SelfLine(session, 0, "Pledge name changed.");
        return null;
    }

    string? AssignCaptain(ZoneSession session, ZoneClan clan, string unitName, string leaderName)
    {
        if (leaderName.Length > PledgeProtocol.MaxTitleLength)
        {
            SelfLine(session, 80, "Your title cannot exceed 16 characters in length. Please try again.");
            return PledgeProtocol.BadArgument;
        }
        var unit = clan.D.SubPledges.FirstOrDefault(sp => string.Equals(sp.Name, unitName, StringComparison.OrdinalIgnoreCase));
        if (unit == null || unit.Type == PledgeProtocol.Academy)
        {
            SelfLine(session, 261, "Clan name is invalid.");
            return PledgeProtocol.BadArgument;
        }
        var member = MemberByName(clan, leaderName);
        if (member == null || member.CharacterId == clan.LeaderId || member.PledgeType != PledgeProtocol.Main ||
            LeaderSubPledge(clan, member.CharacterId) != 0)
        {
            if (PledgeProtocol.IsKnight(unit.Type))
                SelfLine(session, 1850, "The Captain of the Order of Knights cannot be appointed.");
            else
                SelfLine(session, 1851, "The Captain of the Royal Guard cannot be appointed.");
            return PledgeProtocol.BadArgument;
        }
        unit.LeaderId = member.CharacterId;
        if (!SaveClan(clan))
            return ClanProtocol.Store;
        ClanLine(OnlineIds(clan), 1793, $"{member.Name} has been selected as the captain of {unit.Name}.", member.Name, unit.Name);
        BroadcastClan(clan);
        return null;
    }
}
