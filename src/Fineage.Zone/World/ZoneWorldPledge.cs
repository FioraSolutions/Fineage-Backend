using Fineage.Accounts;
using Fineage.Game.Services;
using Fineage.Protocol;
using Fineage.Zone.Clan;

namespace Fineage.Zone.World;

/// <summary>
/// Interlude clan state beyond the roster (Block C), matched against L2J Mobius CT_0: <c>Clan</c> (reputation,
/// sub-pledges, rank privileges, wars), <c>ClanMember.calculatePledgeClass</c>, <c>ClanPrivilege</c>.
/// Rules reimplemented, not copied.
/// </summary>
public sealed partial class ZoneWorld
{
    PledgeSkillCatalog? _pledgeSkills;

    internal PledgeSkillCatalog PledgeSkills
    {
        get => _pledgeSkills ??= PledgeSkillCatalog.LoadDefault();
        set => _pledgeSkills = value;
    }

    static ClanMemberDetails? MemberDetail(ZoneClan clan, int characterId) =>
        clan.D.Members.TryGetValue(characterId, out var d) ? d : null;

    static ClanMemberDetails EnsureMemberDetail(ZoneClan clan, int characterId)
    {
        if (!clan.D.Members.TryGetValue(characterId, out var d))
        {
            d = new ClanMemberDetails();
            clan.D.Members[characterId] = d;
        }
        return d;
    }

    static ZoneClanMember? MemberOf(ZoneClan clan, int characterId) =>
        clan.Members.FirstOrDefault(m => m.CharacterId == characterId);

    static ZoneClanMember? MemberByName(ZoneClan clan, string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : clan.Members.FirstOrDefault(m => string.Equals(m.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Mobius power grade: 1 for the leader, the stored grade, else 5 (main) / 9 (academy).</summary>
    static int GradeOf(ZoneClan clan, int characterId)
    {
        if (clan.LeaderId == characterId)
            return PledgeProtocol.LeaderGrade;
        var d = MemberDetail(clan, characterId);
        if (d != null && d.PowerGrade is >= PledgeProtocol.MinPowerGrade and <= PledgeProtocol.MaxPowerGrade)
            return d.PowerGrade;
        return MemberOf(clan, characterId)?.PledgeType == PledgeProtocol.Academy
            ? PledgeProtocol.AcademyGrade
            : PledgeProtocol.NewMemberGrade;
    }

    static int RankPrivilegesOf(ZoneClan clan, int grade)
    {
        int mask = clan.D.RankPrivileges.TryGetValue(grade, out int m) ? m : 0;
        return grade == PledgeProtocol.AcademyGrade ? mask & PledgeProtocol.PrivAcademyMask : mask & PledgeProtocol.PrivAll;
    }

    /// <summary>Mobius <c>Player.getClanPrivileges</c>: the leader holds every bit, others their grade's mask.</summary>
    static int PrivilegesOf(ZoneClan clan, int characterId) =>
        clan.LeaderId == characterId ? PledgeProtocol.PrivAll : RankPrivilegesOf(clan, GradeOf(clan, characterId));

    static bool HasPriv(ZoneClan clan, int characterId, int privilege) =>
        (PrivilegesOf(clan, characterId) & privilege) == privilege;

    static SubPledgeSave? SubPledge(ZoneClan clan, int type) =>
        clan.D.SubPledges.FirstOrDefault(sp => sp.Type == type);

    /// <summary>Mobius <c>Clan.getLeaderSubPledge</c>: the sub-unit this member captains, 0 none.</summary>
    static int LeaderSubPledge(ZoneClan clan, int characterId) =>
        characterId == 0 ? 0 : clan.D.SubPledges.FirstOrDefault(sp => sp.LeaderId == characterId)?.Type ?? 0;

    /// <summary>Mobius <c>ClanMember.calculatePledgeClass</c> for Interlude clan levels 0–8 (noble floor 5, hero floor 8).</summary>
    int PledgeClassOf(ZoneClan? clan, int characterId, int pledgeType, bool hero = false)
    {
        int value = 0;
        if (clan != null)
        {
            bool leader = clan.LeaderId == characterId;
            int captain = LeaderSubPledge(clan, characterId);
            value = clan.Level switch
            {
                4 => leader ? 3 : 0,
                5 => leader ? 4 : 2,
                6 or 7 or 8 => ByUnit(clan.Level, pledgeType, leader, captain),
                _ => 1
            };
        }

        if (value < 5 && _byId.TryGetValue(characterId, out var who) && who.Noble)
            value = 5;
        if (hero && value < 8)
            value = 8;
        return value;

        static int ByUnit(int level, int type, bool leader, int captain)
        {
            if (type == PledgeProtocol.Academy)
                return 1;
            if (PledgeProtocol.IsRoyal(type))
                return level - 4;
            if (PledgeProtocol.IsKnight(type))
                return level == 6 ? 0 : level - 5;
            if (type != PledgeProtocol.Main)
                return 0;
            if (leader)
                return level == 6 ? 5 : level;
            if (PledgeProtocol.IsRoyal(captain))
                return level == 6 ? 4 : level - 1;
            if (PledgeProtocol.IsKnight(captain))
                return level == 6 ? 3 : level - 2;
            return level == 6 ? 3 : level - 3;
        }
    }

    int PledgeClassOf(ZoneClan clan, int characterId, int pledgeType) =>
        PledgeClassOf(clan, characterId, pledgeType, _byId.TryGetValue(characterId, out var e) && e.Hero);

    /// <summary>Mobius <c>Clan.getSubPledgeMembersCount</c> / <c>getMaxNrOfMembers</c>.</summary>
    static int UnitCount(ZoneClan clan, int type) => clan.Members.Count(m => m.PledgeType == type);

    /// <summary>Mobius <c>Clan.getAvailablePledgeTypes</c>: the next free unit id for this family, 0 when none.</summary>
    static int NextFreeType(ZoneClan clan, int family)
    {
        if (SubPledge(clan, family) == null)
            return family;
        return family switch
        {
            PledgeProtocol.Royal1 => SubPledge(clan, PledgeProtocol.Royal2) == null ? PledgeProtocol.Royal2 : 0,
            PledgeProtocol.Knight1 => SubPledge(clan, PledgeProtocol.Knight2) == null ? PledgeProtocol.Knight2
                : SubPledge(clan, PledgeProtocol.Knight3) == null ? PledgeProtocol.Knight3
                : SubPledge(clan, PledgeProtocol.Knight4) == null ? PledgeProtocol.Knight4 : 0,
            _ => 0
        };
    }

    /// <summary>Persists the whole clan row (roster + details) and returns false on a store refusal.</summary>
    bool SaveClan(ZoneClan clan, IReadOnlyList<CharacterClanState>? characters = null)
    {
        if (clan.Warehouse != null)
            clan.D.Warehouse = clan.Warehouse.Items
                .Select(s => new ClanItemSave { ObjectId = s.ObjectId, ItemId = s.ItemId, Count = s.Count, EnchantLevel = s.EnchantLevel })
                .ToList();
        return _accounts == null || _accounts.UpdateClan(ToRecord(clan), characters ?? []);
    }

    /// <summary>Details written with a roster snapshot; drops member rows that are no longer in the roster.</summary>
    static ClanDetails DetailsFor(ZoneClan clan, IEnumerable<ZoneClanMember> members)
    {
        var copy = clan.D.Clone();
        var ids = members.Select(m => m.CharacterId).ToHashSet();
        foreach (int id in copy.Members.Keys.ToList())
        {
            if (!ids.Contains(id))
                copy.Members.Remove(id);
        }
        return copy;
    }

    int[] OnlineIds(ZoneClan clan) => OnlineMembers(clan).Select(s => s.ObjectId).ToArray();

    /// <summary>Mobius <c>Clan.broadcastClanStatus</c> + <c>updateUserInfo</c> on every online member.</summary>
    void BroadcastClan(ZoneClan clan, bool charInfo = false)
    {
        var online = OnlineIds(clan);
        FanPledgeStatus(online, clan);
        FanMemberList(online, clan);
        foreach (int id in online)
        {
            if (!_byId.TryGetValue(id, out var entity))
                continue;
            SyncClanStanding(entity);
            FanUserInfo(entity);
            if (charInfo)
                QueueCharInfo(entity);
        }
    }

    void ClanLine(IReadOnlyList<int> watchers, int id, string text, params string[] args) =>
        NetBridge?.FanoutSystemMessage(watchers, new SystemMessageMsg { Id = id, Text = text, Params = args });

    void SelfLine(ZoneSession session, int id, string text, params string[] args) =>
        ClanLine([session.ObjectId], id, text, args);

    /// <summary>
    /// Mobius <c>Clan.addReputationScore</c> / <c>takeReputationScore</c>: clamp to ±100M; crossing below 0 disables
    /// clan skills (1789), back to 0+ re-enables them (1862).
    /// </summary>
    void ChangeReputation(ZoneClan clan, int delta, bool save = true)
    {
        if (delta == 0)
            return;
        int before = clan.D.Reputation;
        long next = Math.Clamp((long)before + delta, -PledgeProtocol.ReputationCap, PledgeProtocol.ReputationCap);
        clan.D.Reputation = (int)next;
        var online = OnlineIds(clan);
        if (before >= 0 && clan.D.Reputation < 0)
            ClanLine(online, 1789, "Since the Clan Reputation Score has dropped to 0 or lower, your clan skill(s) will be de-activated.");
        else if (before < 0 && clan.D.Reputation >= 0)
            ClanLine(online, 1862, "Clan skills will now be activated since the clan's reputation score is 0 or higher.");
        if (save)
            SaveClan(clan);
        BroadcastClan(clan);
    }

    /// <summary>Test hook: set a clan's reputation (persisted, broadcast).</summary>
    internal bool SetClanReputationForTest(int clanId, int reputation)
    {
        lock (_gate)
        {
            if (!_clans.TryGetValue(clanId, out var clan))
                return false;
            ChangeReputation(clan, reputation - clan.D.Reputation);
            return true;
        }
    }

    /// <summary>Test hook: set a clan's level directly (persisted, broadcast).</summary>
    internal bool SetClanLevelForTest(int clanId, int level)
    {
        lock (_gate)
        {
            if (!_clans.TryGetValue(clanId, out var clan))
                return false;
            clan.Level = level;
            SaveClan(clan);
            BroadcastClan(clan);
            return true;
        }
    }

    /// <summary>Test hook: the live clan details (copy).</summary>
    internal ClanDetails? ClanDetailsForTest(int clanId)
    {
        lock (_gate)
            return _clans.TryGetValue(clanId, out var clan) ? clan.D.Clone() : null;
    }

    internal (int Level, int LeaderId, int CrestId, int MemberCount)? ClanSummaryForTest(int clanId)
    {
        lock (_gate)
            return _clans.TryGetValue(clanId, out var c) ? (c.Level, c.LeaderId, c.CrestId, c.Members.Count) : null;
    }

    internal int ClanIdOfForTest(int characterId)
    {
        lock (_gate)
            return ClanIdFor(characterId);
    }

    internal int PledgeTypeOfForTest(int characterId)
    {
        lock (_gate)
            return ClanOf(characterId) is { } c ? MemberOf(c, characterId)?.PledgeType ?? 0 : 0;
    }
}

public sealed partial class ZoneWorld
{
    /// <summary>Mobius <c>SiegeManager.checkIsRegistered</c> over every castle.</summary>
    bool ClanInSiege(int clanId) => _siege.Board.ExportClans().Any(r => r.ClanId == clanId);
}
