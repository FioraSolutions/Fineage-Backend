using Fineage.Login;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Clan;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Clan lifecycle over Net. Every case drives the public <c>TryClan*</c> entry points and asserts the
/// <see cref="INetPresenceBridge"/> fanout: the pledge status, the roster, the invite, the join result,
/// and the system lines that carry the rejection codes.
/// </summary>
public sealed class NetClanTests
{
    /// <summary>Mobius <c>Config.ALT_CLAN_CREATE_LEVEL</c>: the create path refuses anything under it.</summary>
    const int ClanCreateLevel = 10;

    static (NetTestKit Kit, NetBridgeRecorder Rec) World()
    {
        var kit = new NetTestKit();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return (kit, rec);
    }

    static PledgeStatusNetMsg LastStatus(NetBridgeRecorder rec) =>
        Assert.IsType<PledgeStatusNetMsg>(rec.Of(nameof(INetPresenceBridge.FanoutPledgeStatus)).Last()[1]);

    static PledgeMemberListNetMsg LastRoster(NetBridgeRecorder rec) =>
        Assert.IsType<PledgeMemberListNetMsg>(rec.Of(nameof(INetPresenceBridge.FanoutPledgeMemberList)).Last()[1]);

    /// <summary>Clan open, at a level and class the create gate accepts.</summary>
    static NetTestKit.Character LevelTen(NetTestKit kit) => kit.Create(level: ClanCreateLevel);

    static async Task<(ZoneSession Leader, int LeaderId)> Clan(
        NetTestKit kit, ZoneSession leader, ZoneEntity leaderEntity, string name = "Fine")
    {
        Assert.Null(await kit.World.TryClanCreateAsync(
            leader, new ClanCreateRequestMsg { Name = name }, kit.Now, default));
        return (leader, leaderEntity.ObjectId);
    }

    static async Task<int> Join(NetTestKit kit, ZoneSession leader, int leaderId, ZoneSession member, int memberId)
    {
        Assert.Null(await kit.World.TryClanInviteAsync(
            leader, new ClanInviteRequestMsg { TargetObjectId = memberId }, kit.Now, default));
        Assert.Null(await kit.World.TryClanAnswerAsync(
            member, new ClanAnswerJoinRequestMsg { RequestorObjectId = leaderId, Response = 1 }, kit.Now, default));
        return memberId;
    }

    [Fact]
    public async Task Create_publishes_the_pledge_status_and_the_roster()
    {
        var (kit, rec) = World();
        var (leader, leaderEntity) = await kit.EnterAsync();
        leaderEntity.Level = ClanCreateLevel;

        Assert.Null(await kit.World.TryClanCreateAsync(
            leader, new ClanCreateRequestMsg { Name = "Fine" }, kit.Now, default));

        PledgeStatusNetMsg status = LastStatus(rec);
        Assert.Equal("Fine", status.Name);
        Assert.Equal(leaderEntity.ObjectId, status.LeaderObjectId);
        Assert.Equal(1, status.MemberCount);

        PledgeMemberListNetMsg roster = LastRoster(rec);
        Assert.Equal(leaderEntity.ObjectId, Assert.Single(roster.Members).ObjectId);
    }

    [Fact]
    public async Task Invite_reaches_the_target_and_the_answer_reaches_the_leader()
    {
        var (kit, rec) = World();
        var (leader, leaderEntity) = await kit.EnterAsync();
        leaderEntity.Level = ClanCreateLevel;
        var (member, memberEntity) = await kit.EnterAsync();
        memberEntity.Level = ClanCreateLevel;
        Assert.Null(await kit.World.TryClanCreateAsync(
            leader, new ClanCreateRequestMsg { Name = "Fine" }, kit.Now, default));

        Assert.Null(await kit.World.TryClanInviteAsync(
            leader, new ClanInviteRequestMsg { TargetObjectId = memberEntity.ObjectId, PledgeType = 0 }, kit.Now, default));

        ClanAskJoinNetMsg ask = Assert.IsType<ClanAskJoinNetMsg>(
            Assert.Single(rec.Of(nameof(INetPresenceBridge.FanoutClanAskJoin)))[1]);
        Assert.Equal(leaderEntity.ObjectId, ask.RequestorObjectId);
        Assert.Equal("Fine", ask.ClanName);

        Assert.Null(await kit.World.TryClanAnswerAsync(
            member, new ClanAnswerJoinRequestMsg { RequestorObjectId = leaderEntity.ObjectId, Response = 2 }, kit.Now, default));

        ClanJoinResultNetMsg result = Assert.IsType<ClanJoinResultNetMsg>(
            Assert.Single(rec.Of(nameof(INetPresenceBridge.FanoutClanJoinResult)))[1]);
        Assert.Equal(2, result.Response);
        Assert.Equal(2, LastRoster(rec).Members.Length);
    }

    [Fact]
    public async Task Leave_publishes_a_status_without_the_leaver()
    {
        var (kit, rec) = World();
        var (leader, leaderEntity) = await kit.EnterAsync();
        leaderEntity.Level = ClanCreateLevel;
        var (member, memberEntity) = await kit.EnterAsync();
        memberEntity.Level = ClanCreateLevel;
        Assert.Null(await kit.World.TryClanCreateAsync(
            leader, new ClanCreateRequestMsg { Name = "Fine" }, kit.Now, default));
        Assert.Null(await kit.World.TryClanInviteAsync(
            leader, new ClanInviteRequestMsg { TargetObjectId = memberEntity.ObjectId, PledgeType = 0 }, kit.Now, default));
        Assert.Null(await kit.World.TryClanAnswerAsync(
            member, new ClanAnswerJoinRequestMsg { RequestorObjectId = 1, Response = 2 }, kit.Now, default));

        Assert.Null(await kit.World.TryClanLeaveAsync(member, kit.Now, default));

        Assert.Equal(1, LastStatus(rec).MemberCount);
    }

    [Fact]
    public async Task Kick_removes_the_member_from_the_published_roster()
    {
        var (kit, rec) = World();
        var (leader, leaderEntity) = await kit.EnterAsync();
        leaderEntity.Level = ClanCreateLevel;
        var (member, memberEntity) = await kit.EnterAsync();
        memberEntity.Level = ClanCreateLevel;
        Assert.Null(await kit.World.TryClanCreateAsync(
            leader, new ClanCreateRequestMsg { Name = "Fine" }, kit.Now, default));
        Assert.Null(await kit.World.TryClanInviteAsync(
            leader, new ClanInviteRequestMsg { TargetObjectId = memberEntity.ObjectId, PledgeType = 0 }, kit.Now, default));
        Assert.Null(await kit.World.TryClanAnswerAsync(
            member, new ClanAnswerJoinRequestMsg { RequestorObjectId = leaderEntity.ObjectId, Response = 2 }, kit.Now, default));

        Assert.Null(await kit.World.TryClanKickAsync(
            leader, new ClanKickRequestMsg { TargetObjectId = memberEntity.ObjectId }, kit.Now, default));

        Assert.Equal(1, LastStatus(rec).MemberCount);
        Assert.DoesNotContain(LastRoster(rec).Members, row => row.ObjectId == memberEntity.ObjectId);
    }

    [Fact]
    public async Task Dissolve_publishes_the_dissolving_expiry()
    {
        var (kit, rec) = World();
        var (leader, leaderEntity) = await kit.EnterAsync();
        leaderEntity.Level = ClanCreateLevel;
        Assert.Null(await kit.World.TryClanCreateAsync(
            leader, new ClanCreateRequestMsg { Name = "Fine" }, kit.Now, default));

        Assert.Null(await kit.World.TryClanDissolveAsync(leader, kit.Now, default));
        Assert.True(LastStatus(rec).DissolvingExpiryUnixMs > 0);

        Assert.Null(await kit.World.TryClanCancelDissolveAsync(leader, kit.Now, default));
        Assert.Equal(0, LastStatus(rec).DissolvingExpiryUnixMs);
    }

    [Fact]
    public async Task Creating_a_second_clan_is_refused_and_publishes_nothing_new()
    {
        var (kit, rec) = World();
        var (leader, leaderEntity) = await kit.EnterAsync();
        leaderEntity.Level = ClanCreateLevel;
        Assert.Null(await kit.World.TryClanCreateAsync(
            leader, new ClanCreateRequestMsg { Name = "Fine" }, kit.Now, default));
        int published = rec.Of(nameof(INetPresenceBridge.FanoutPledgeStatus)).Count;

        Assert.Equal(ClanProtocol.Member, await kit.World.TryClanCreateAsync(
            leader, new ClanCreateRequestMsg { Name = "Other" }, kit.Now, default));
        Assert.Equal(published, rec.Of(nameof(INetPresenceBridge.FanoutPledgeStatus)).Count);
    }

    /// <summary>
    /// Mobius EnterWorld:561 sends the pledge sheet and roster on login. Without this snapshot a clan
    /// member re-entering the world sat in front of an empty clan window.
    /// </summary>
    [Fact]
    public async Task A_clan_member_logging_in_gets_the_status_and_roster_snapshot()
    {
        var (kit, _) = World();
        var (leader, leaderEntity) = await kit.EnterAsync();
        leaderEntity.Level = ClanCreateLevel;
        var (member, memberEntity) = await kit.EnterAsync();
        memberEntity.Level = ClanCreateLevel;
        Assert.Null(await kit.World.TryClanCreateAsync(
            leader, new ClanCreateRequestMsg { Name = "Fine" }, kit.Now, default));
        Assert.Null(await kit.World.TryClanInviteAsync(
            leader, new ClanInviteRequestMsg { TargetObjectId = memberEntity.ObjectId, PledgeType = 0 }, kit.Now, default));
        Assert.Null(await kit.World.TryClanAnswerAsync(
            member, new ClanAnswerJoinRequestMsg { RequestorObjectId = leaderEntity.ObjectId, Response = 2 }, kit.Now, default));

        Assert.True(kit.World.TryBuildNetClanSnapshot(leader, out var status, out var roster));
        Assert.Equal("Fine", status.Name);
        Assert.Equal(leaderEntity.ObjectId, status.LeaderObjectId);
        Assert.Equal(2, roster.Members.Length);

        // A character with no clan must not produce a snapshot: an empty status would read as "clan left".
        var (outsider, _) = await kit.EnterAsync();
        Assert.False(kit.World.TryBuildNetClanSnapshot(outsider, out _, out _));
    }

    [Fact]
    public async Task Create_rejects_level_name_and_duplicates()
    {
        var (kit, _) = World();
        var low = kit.Create();
        var leader = LevelTen(kit);
        var other = LevelTen(kit);

        var (lowSession, lowEntity) = await kit.EnterAsync(low);
        Assert.True(lowEntity.Level < ClanCreateLevel);
        Assert.Equal(ClanProtocol.Level, await kit.World.TryClanCreateAsync(
            lowSession, new ClanCreateRequestMsg { Name = "Fine" }, kit.Now, default));

        var (session, _) = await kit.EnterAsync(leader);
        Assert.Equal(ClanProtocol.Name, await kit.World.TryClanCreateAsync(
            session, new ClanCreateRequestMsg { Name = "A" }, kit.Now, default));
        Assert.Equal(ClanProtocol.NameLength, await kit.World.TryClanCreateAsync(
            session, new ClanCreateRequestMsg { Name = new string('B', 17) }, kit.Now, default));
        Assert.Equal(ClanProtocol.Name, await kit.World.TryClanCreateAsync(
            session, new ClanCreateRequestMsg { Name = "bad name" }, kit.Now, default));

        Assert.Null(await kit.World.TryClanCreateAsync(
            session, new ClanCreateRequestMsg { Name = "Fine" }, kit.Now, default));

        // Clan names collide case-insensitively (Mobius lowercases the pledge name).
        var (otherSession, _) = await kit.EnterAsync(other);
        Assert.Equal(ClanProtocol.NameTaken, await kit.World.TryClanCreateAsync(
            otherSession, new ClanCreateRequestMsg { Name = "fine" }, kit.Now, default));
    }

    [Fact]
    public async Task Invite_requires_a_visible_target_and_a_leader()
    {
        // A range AOI keeps the far target out of the leader's knownlist: out of sight reads as clan_target.
        var kit = new NetTestKit(aoi: new Fineage.Zone.Interest.RangeAoi(50));
        var (bridge, _) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        var leader = LevelTen(kit);
        var member = kit.Create(level: ClanCreateLevel, x: NetTestKit.ElfX + 100_000);
        var (session, leaderEntity) = await kit.EnterAsync(leader);
        var (_, memberEntity) = await kit.EnterAsync(member);
        await Clan(kit, session, leaderEntity);

        Assert.Equal(ClanProtocol.Target, await kit.World.TryClanInviteAsync(
            session, new ClanInviteRequestMsg { TargetObjectId = memberEntity.ObjectId }, kit.Now, default));
        Assert.Equal(ClanProtocol.Self, await kit.World.TryClanInviteAsync(
            session, new ClanInviteRequestMsg { TargetObjectId = leaderEntity.ObjectId }, kit.Now, default));
        Assert.Equal(ClanProtocol.Pledge, await kit.World.TryClanInviteAsync(
            session, new ClanInviteRequestMsg { TargetObjectId = memberEntity.ObjectId, PledgeType = -1 },
            kit.Now, default));

    }

    [Fact]
    public async Task Decline_and_timeout_do_not_join()
    {
        var (kit, rec) = World();
        var leader = LevelTen(kit);
        var member = LevelTen(kit);
        var (session, leaderEntity) = await kit.EnterAsync(leader);
        var (memberSession, memberEntity) = await kit.EnterAsync(member);
        await Clan(kit, session, leaderEntity);

        Assert.Null(await kit.World.TryClanInviteAsync(
            session, new ClanInviteRequestMsg { TargetObjectId = memberEntity.ObjectId }, kit.Now, default));
        Assert.Null(await kit.World.TryClanAnswerAsync(
            memberSession, new ClanAnswerJoinRequestMsg { RequestorObjectId = leaderEntity.ObjectId, Response = 0 },
            kit.Now, default));

        var lines = rec.Of(nameof(INetPresenceBridge.FanoutSystemMessage));
        Assert.Contains(lines, call => ((SystemMessageNetMsg)call[1]).Id == ClanProtocol.InviteDeclinedSelf);
        Assert.Contains(lines, call => ((SystemMessageNetMsg)call[1]).Id == ClanProtocol.InviteDeclinedRequestor);
        Assert.Equal(0, Assert.IsType<ClanJoinResultNetMsg>(
            Assert.Single(rec.Of(nameof(INetPresenceBridge.FanoutClanJoinResult)))[1]).Response);
        Assert.Equal(0, kit.Store.FindById(member.Account, member.CharacterId)!.ClanId);

        // Second invite while the first is still pending is refused as busy.
        Assert.Null(await kit.World.TryClanInviteAsync(
            session, new ClanInviteRequestMsg { TargetObjectId = memberEntity.ObjectId }, kit.Now, default));
        Assert.Equal(ClanProtocol.Busy, await kit.World.TryClanInviteAsync(
            session, new ClanInviteRequestMsg { TargetObjectId = memberEntity.ObjectId }, kit.Now, default));

        // After the invite deadline the answer no longer matches a pending invite.
        kit.Now = kit.Now.AddSeconds(ClanProtocol.InviteTimeoutSeconds);
        Assert.Equal(ClanProtocol.Invite, await kit.World.TryClanAnswerAsync(
            memberSession, new ClanAnswerJoinRequestMsg { RequestorObjectId = leaderEntity.ObjectId, Response = 1 },
            kit.Now, default));
    }

    [Fact]
    public async Task Leave_sets_join_penalty_and_kick_sets_recruit_penalty()
    {
        var (kit, rec) = World();
        var leader = LevelTen(kit);
        var member = LevelTen(kit);
        var third = LevelTen(kit);
        var (session, leaderEntity) = await kit.EnterAsync(leader);
        var (memberSession, memberEntity) = await kit.EnterAsync(member);
        var (thirdSession, thirdEntity) = await kit.EnterAsync(third);
        await Clan(kit, session, leaderEntity);
        await Join(kit, session, leaderEntity.ObjectId, memberSession, memberEntity.ObjectId);

        Assert.Equal(ClanProtocol.LeaderLeave, await kit.World.TryClanLeaveAsync(session, kit.Now, default));
        Assert.Equal(ClanProtocol.Leader, await kit.World.TryClanKickAsync(
            memberSession, new ClanKickRequestMsg { TargetObjectId = leaderEntity.ObjectId }, kit.Now, default));
        Assert.Equal(ClanProtocol.DismissSelf, await kit.World.TryClanKickAsync(
            session, new ClanKickRequestMsg { TargetObjectId = leaderEntity.ObjectId }, kit.Now, default));

        Assert.Null(await kit.World.TryClanLeaveAsync(memberSession, kit.Now, default));
        Assert.Contains(rec.Of(nameof(INetPresenceBridge.FanoutSystemMessage)),
            call => ((SystemMessageNetMsg)call[1]).Id == ClanProtocol.Withdrawn);

        // The leaver is told the clan is gone: an empty status and an empty roster, both addressed to them only.
        Assert.Contains(rec.Of(nameof(INetPresenceBridge.FanoutPledgeStatus)), call =>
            Assert.IsType<PledgeStatusNetMsg>(call[1]).ClanId == 0 &&
            ((IReadOnlyList<int>)call[0]).SequenceEqual([memberEntity.ObjectId]));
        Assert.Contains(rec.Of(nameof(INetPresenceBridge.FanoutPledgeMemberList)), call =>
            Assert.IsType<PledgeMemberListNetMsg>(call[1]).Members.Length == 0 &&
            ((IReadOnlyList<int>)call[0]).SequenceEqual([memberEntity.ObjectId]));

        Assert.Equal(1, LastStatus(rec).MemberCount);
        Assert.True(kit.Store.FindById(member.Account, member.CharacterId)!.ClanJoinExpiryUnixMs >
                    kit.Now.ToUnixTimeMilliseconds());

        // The leaver carries a join penalty; the third character is still invited freely.
        Assert.Null(await kit.World.TryClanInviteAsync(
            session, new ClanInviteRequestMsg { TargetObjectId = thirdEntity.ObjectId }, kit.Now, default));
        Assert.Equal(ClanProtocol.Waiting, await kit.World.TryClanInviteAsync(
            session, new ClanInviteRequestMsg { TargetObjectId = memberEntity.ObjectId }, kit.Now, default));

        kit.Now = kit.Now.AddSeconds(ClanProtocol.InviteTimeoutSeconds);
        Assert.Equal(ClanProtocol.JoinPenalty, await kit.World.TryClanInviteAsync(
            session, new ClanInviteRequestMsg { TargetObjectId = memberEntity.ObjectId }, kit.Now, default));

        // The kick side of the same rule: the expelled member leaves a recruit penalty on the clan.
        var kicked = new NetTestKit();
        var (kickedBridge, _) = NetBridgeRecorder.Create();
        kicked.World.NetBridge = kickedBridge;
        var kLeader = LevelTen(kicked);
        var kMember = LevelTen(kicked);
        var kThird = LevelTen(kicked);
        var (kLeaderSession, kLeaderEntity) = await kicked.EnterAsync(kLeader);
        var (kMemberSession, kMemberEntity) = await kicked.EnterAsync(kMember);
        var (_, kThirdEntity) = await kicked.EnterAsync(kThird);
        await Clan(kicked, kLeaderSession, kLeaderEntity, "Kick");
        await Join(kicked, kLeaderSession, kLeaderEntity.ObjectId, kMemberSession, kMemberEntity.ObjectId);

        Assert.Null(await kicked.World.TryClanKickAsync(
            kLeaderSession, new ClanKickRequestMsg { TargetObjectId = kMemberEntity.ObjectId }, kicked.Now, default));

        Assert.Equal(ClanProtocol.RecruitPenalty, await kicked.World.TryClanInviteAsync(
            kLeaderSession, new ClanInviteRequestMsg { TargetObjectId = kThirdEntity.ObjectId }, kicked.Now, default));
    }

    static SystemMessageNetMsg LastLine(NetBridgeRecorder rec) =>
        Assert.IsType<SystemMessageNetMsg>(rec.Of(nameof(INetPresenceBridge.FanoutSystemMessage)).Last()[1]);

    [Fact]
    public async Task Combat_blocks_leave_and_dismiss_until_the_stance_ends()
    {
        var (kit, _) = World();
        var leader = LevelTen(kit);
        var member = LevelTen(kit);
        var (session, leaderEntity) = await kit.EnterAsync(leader);
        var (memberSession, memberEntity) = await kit.EnterAsync(member);
        await Clan(kit, session, leaderEntity);
        await Join(kit, session, leaderEntity.ObjectId, memberSession, memberEntity.ObjectId);

        // The stance is whatever the combat pipeline stamps; setting it directly keeps this a clan test.
        memberEntity.CombatUntilUnixMs = kit.Now.ToUnixTimeMilliseconds() + ClanProtocol.CombatStanceMillis;
        Assert.Equal(ClanProtocol.Combat, await kit.World.TryClanLeaveAsync(memberSession, kit.Now, default));
        Assert.Equal(ClanProtocol.Combat, await kit.World.TryClanKickAsync(
            session, new ClanKickRequestMsg { TargetObjectId = memberEntity.ObjectId }, kit.Now, default));

        kit.Now = kit.Now.AddMilliseconds(ClanProtocol.CombatStanceMillis);
        Assert.Null(await kit.World.TryClanLeaveAsync(memberSession, kit.Now, default));
    }

    [Fact]
    public async Task Dissolve_waits_seven_days_and_cancel_stops_it()
    {
        var (kit, rec) = World();
        var leader = LevelTen(kit);
        var member = LevelTen(kit);
        var (session, leaderEntity) = await kit.EnterAsync(leader);
        var (memberSession, memberEntity) = await kit.EnterAsync(member);
        await Clan(kit, session, leaderEntity);
        await Join(kit, session, leaderEntity.ObjectId, memberSession, memberEntity.ObjectId);
        int clanId = LastStatus(rec).ClanId;

        Assert.Equal(ClanProtocol.Leader, await kit.World.TryClanDissolveAsync(memberSession, kit.Now, default));

        long requested = kit.Now.ToUnixTimeMilliseconds();
        Assert.Null(await kit.World.TryClanDissolveAsync(session, kit.Now, default));
        Assert.Equal(ClanProtocol.DissolveRequested, LastLine(rec).Id);
        Assert.Equal(ClanRules.DissolveExpiry(requested), LastStatus(rec).DissolvingExpiryUnixMs);
        Assert.Equal(ClanProtocol.Dissolving, await kit.World.TryClanDissolveAsync(session, kit.Now, default));

        kit.Now = kit.Now.AddMilliseconds(ClanProtocol.DissolveDays * ClanProtocol.DayMillis - 1);
        kit.World.CompleteDueDissolutions(kit.Now);
        Assert.NotNull(kit.Store.FindClan(clanId));

        Assert.Null(await kit.World.TryClanCancelDissolveAsync(session, kit.Now, default));
        Assert.Equal(ClanProtocol.DissolveCancelled, LastLine(rec).Id);
        Assert.Equal(0, LastStatus(rec).DissolvingExpiryUnixMs);

        kit.Now = kit.Now.AddMilliseconds(ClanProtocol.DayMillis);
        kit.World.CompleteDueDissolutions(kit.Now);
        Assert.NotNull(kit.Store.FindClan(clanId));

        // Re-requesting takes a fresh seven days from that moment, not from the cancelled one.
        long again = kit.Now.ToUnixTimeMilliseconds();
        Assert.Null(await kit.World.TryClanDissolveAsync(session, kit.Now, default));
        Assert.Equal(ClanRules.DissolveExpiry(again), LastStatus(rec).DissolvingExpiryUnixMs);

        kit.Now = DateTimeOffset.FromUnixTimeMilliseconds(ClanRules.DissolveExpiry(again));
        kit.World.CompleteDueDissolutions(kit.Now);
        Assert.Equal(ClanProtocol.Dispersed, LastLine(rec).Id);
        Assert.Equal(0, ((PledgeStatusNetMsg)rec.Of(nameof(INetPresenceBridge.FanoutPledgeStatus))[^1][1]).ClanId);
        Assert.Equal(0, LastUserInfoClan(rec));
        Assert.Null(kit.Store.FindClan(clanId));

        Assert.Equal(ClanProtocol.CreatePenalty, await kit.World.TryClanCreateAsync(
            session, new ClanCreateRequestMsg { Name = "Again" }, kit.Now, default));
        Assert.Null(await kit.World.TryClanCreateAsync(
            memberSession, new ClanCreateRequestMsg { Name = "Next" }, kit.Now, default));
        Assert.Equal(0, kit.Store.FindById(member.Account, member.CharacterId)!.ClanJoinExpiryUnixMs);
    }

    static int LastUserInfoClan(NetBridgeRecorder rec) =>
        Assert.IsType<UserInfoNetMsg>(rec.Of(nameof(INetPresenceBridge.FanoutUserInfo)).Last()[1]).ClanId;

    [Fact]
    public async Task Level_0_clan_rejects_an_eleventh_member()
    {
        var kit = new NetTestKit();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;

        var tickets = new List<NetTestKit.Character>();
        var members = new List<ClanMemberRecord>();
        for (int i = 0; i < 10; i++)
        {
            var seat = kit.Create(level: ClanCreateLevel);
            tickets.Add(seat);
            members.Add(new ClanMemberRecord(seat.CharacterId, seat.Account, seat.Name, seat.ClassId, ClanCreateLevel, 0));
        }

        var clan = kit.Store.InsertClan(new ClanRecord(
            0, "Full", 0, tickets[0].CharacterId, 0, 0, 0, members),
            new CharacterClanState(tickets[0].Account, tickets[0].CharacterId, 0, 0, 0));
        Assert.NotNull(clan);
        Assert.Equal(10, clan!.Members.Count);
        // The row landed after the world booted, so the cache has to read it back before the leader can act.
        Assert.True(kit.World.ReloadClansForTests());

        var recruit = kit.Create();
        var (leader, _) = await kit.EnterAsync(tickets[0]);
        var (_, recruitEntity) = await kit.EnterAsync(recruit);

        Assert.Equal(ClanProtocol.Full, await kit.World.TryClanInviteAsync(
            leader, new ClanInviteRequestMsg { TargetObjectId = recruitEntity.ObjectId }, kit.Now, default));
        Assert.Empty(rec.Of(nameof(INetPresenceBridge.FanoutClanAskJoin)));
    }

    [Fact]
    public async Task Membership_survives_a_new_zone_world()
    {
        var kit = new NetTestKit();
        var leader = LevelTen(kit);
        var (session, leaderEntity) = await kit.EnterAsync(leader);
        await Clan(kit, session, leaderEntity);
        int clanId = LeaderClan(kit, leader.CharacterId);

        // A second world over the same store reads the clan rows back at boot.
        var restarted = new NetTestKit(store: kit.Store);
        var (again, entity) = await restarted.EnterAsync(leader);
        Assert.Equal(clanId, entity.ClanId);
        Assert.True(restarted.World.TryBuildNetClanSnapshot(again, out var status, out var roster));
        Assert.Equal("Fine", status.Name);
        Assert.Equal(clanId, status.ClanId);
        Assert.Equal(leader.CharacterId, Assert.Single(roster.Members).ObjectId);
    }

    /// <summary>Clan id of a character straight from the store: the Net side has no clan query of its own.</summary>
    static int LeaderClan(NetTestKit kit, int characterId) =>
        kit.Store.ListClans()
            .Single(clan => clan.Members.Any(row => row.CharacterId == characterId))
            .Id;
}
