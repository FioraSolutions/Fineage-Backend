using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Zone.Clan;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{

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
