using System.Net;
using System.Net.Sockets;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.Clan;
using Fineage.Zone.Interest;
using Fineage.Zone.World;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

public class ZoneClanTests
{
    [Fact]
    public async Task Create_invite_accept_updates_pledge_and_charinfo()
    {
        await using var fx = await ClanFixture.Open();
        var leader = fx.LevelTen("leader", "Leader", HumanClasses.Fighter);
        var member = fx.LevelTen("member", "Member", HumanClasses.Mystic);
        await using var host = fx.Host();
        await using var left = await ClanPeer.ConnectAsync(host.Port);
        await using var right = await ClanPeer.ConnectAsync(host.Port);

        await left.AuthAsync(leader);
        await right.AuthAsync(member);
        var leaderEnter = await left.EnterAsync();
        Assert.Equal(0, leaderEnter.ClanId);
        Assert.Equal(0, leaderEnter.ClanCrestId);
        await right.EnterAsync(others: 1);
        var seen = await left.ExpectAsync<CharInfoMsg>(MsgType.CharInfo);
        Assert.Equal(member.CharacterId, seen.ObjectId);
        Assert.Equal(0, seen.ClanId);

        await left.SendAsync(MsgType.ClanCreateRequest, new ClanCreateRequestMsg { Name = "Fine" });
        var created = await left.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
        Assert.Equal(ClanProtocol.Created, created.Id);
        var status = await left.ExpectAsync<PledgeStatusChangedMsg>(MsgType.PledgeStatusChanged);
        Assert.Equal("Fine", status.Name);
        Assert.Equal(0, status.Level);
        Assert.Equal(leader.CharacterId, status.LeaderObjectId);
        Assert.Equal(0, status.CrestId);
        Assert.Equal(1, status.MemberCount);
        var list = await left.ExpectAsync<PledgeMemberListMsg>(MsgType.PledgeMemberList);
        Assert.Equal(leader.CharacterId, Assert.Single(list.Members).ObjectId);
        var user = await left.ExpectAsync<UserInfoMsg>(MsgType.UserInfo);
        Assert.True(user.ClanId > 0);
        Assert.Equal(0, user.ClanCrestId);

        var leaderInfo = await right.ExpectAsync<CharInfoMsg>(MsgType.CharInfo);
        Assert.Equal(user.ClanId, leaderInfo.ClanId);
        Assert.Equal(0, leaderInfo.ClanCrestId);

        await left.SendAsync(MsgType.ClanCreateRequest, new ClanCreateRequestMsg { Name = "Other" });
        Assert.Equal(ClanProtocol.Member, (await left.ExpectErrorAsync()).Code);

        await left.SendAsync(MsgType.ClanInviteRequest, new ClanInviteRequestMsg
        {
            TargetObjectId = member.CharacterId,
            PledgeType = 0
        });
        var ask = await right.ExpectAsync<ClanAskJoinMsg>(MsgType.ClanAskJoin);
        Assert.Equal(leader.CharacterId, ask.RequestorObjectId);
        Assert.Equal("Fine", ask.ClanName);
        Assert.Equal(user.ClanId, ask.ClanId);

        await right.SendAsync(MsgType.ClanAnswerJoinRequest, new ClanAnswerJoinRequestMsg
        {
            RequestorObjectId = leader.CharacterId,
            Response = 2
        });
        var result = await left.ExpectAsync<ClanJoinResultMsg>(MsgType.ClanJoinResult);
        Assert.Equal(2, result.Response);
        Assert.Equal(ClanProtocol.MemberJoined, (await left.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage)).Id);
        var joined = await left.ExpectAsync<PledgeStatusChangedMsg>(MsgType.PledgeStatusChanged);
        Assert.Equal(2, joined.MemberCount);
        Assert.Equal(2, (await left.ExpectAsync<PledgeMemberListMsg>(MsgType.PledgeMemberList)).Members.Length);
        var memberInfo = await left.ExpectAsync<CharInfoMsg>(MsgType.CharInfo);
        Assert.Equal(member.CharacterId, memberInfo.ObjectId);
        Assert.Equal(user.ClanId, memberInfo.ClanId);
        Assert.Equal(0, memberInfo.ClanCrestId);

        Assert.Equal(ClanProtocol.Entered, (await right.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage)).Id);
        Assert.Equal(ClanProtocol.MemberJoined, (await right.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage)).Id);
        await right.ExpectAsync<PledgeStatusChangedMsg>(MsgType.PledgeStatusChanged);
        await right.ExpectAsync<PledgeMemberListMsg>(MsgType.PledgeMemberList);
        var memberUser = await right.ExpectAsync<UserInfoMsg>(MsgType.UserInfo);
        Assert.Equal(user.ClanId, memberUser.ClanId);
        Assert.Equal(0, memberUser.ClanCrestId);
    }

    [Fact]
    public async Task Create_rejects_level_name_and_duplicates()
    {
        await using var fx = await ClanFixture.Open();
        var low = fx.Play("low", "Newbie");
        var leader = fx.LevelTen("leader", "Leader", HumanClasses.Fighter);
        var other = fx.LevelTen("other", "Other", HumanClasses.Mystic);
        await using var host = fx.Host();
        await using var a = await ClanPeer.ConnectAsync(host.Port);
        await using var b = await ClanPeer.ConnectAsync(host.Port);
        await using var c = await ClanPeer.ConnectAsync(host.Port);
        await a.AuthAsync(low);
        await b.AuthAsync(leader);
        await c.AuthAsync(other);
        await a.EnterAsync();
        await b.EnterAsync(others: 1);
        await a.ExpectAsync<CharInfoMsg>(MsgType.CharInfo);
        await c.EnterAsync(others: 2);
        await a.ExpectAsync<CharInfoMsg>(MsgType.CharInfo);
        await b.ExpectAsync<CharInfoMsg>(MsgType.CharInfo);

        await a.SendAsync(MsgType.ClanCreateRequest, new ClanCreateRequestMsg { Name = "Fine" });
        Assert.Equal(ClanProtocol.Level, (await a.ExpectErrorAsync()).Code);

        await b.SendAsync(MsgType.ClanCreateRequest, new ClanCreateRequestMsg { Name = "A" });
        Assert.Equal(ClanProtocol.Name, (await b.ExpectErrorAsync()).Code);
        await b.SendAsync(MsgType.ClanCreateRequest, new ClanCreateRequestMsg { Name = new string('B', 17) });
        Assert.Equal(ClanProtocol.NameLength, (await b.ExpectErrorAsync()).Code);
        await b.SendAsync(MsgType.ClanCreateRequest, new ClanCreateRequestMsg { Name = "bad name" });
        Assert.Equal(ClanProtocol.Name, (await b.ExpectErrorAsync()).Code);

        await b.SendAsync(MsgType.ClanCreateRequest, new ClanCreateRequestMsg { Name = "Fine" });
        await b.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
        await b.ExpectAsync<PledgeStatusChangedMsg>(MsgType.PledgeStatusChanged);
        await b.ExpectAsync<PledgeMemberListMsg>(MsgType.PledgeMemberList);
        await b.ExpectAsync<UserInfoMsg>(MsgType.UserInfo);
        await a.ExpectAsync<CharInfoMsg>(MsgType.CharInfo);
        await c.ExpectAsync<CharInfoMsg>(MsgType.CharInfo);

        await c.SendAsync(MsgType.ClanCreateRequest, new ClanCreateRequestMsg { Name = "fine" });
        Assert.Equal(ClanProtocol.NameTaken, (await c.ExpectErrorAsync()).Code);
    }

    [Fact]
    public async Task Invite_requires_a_visible_target_and_a_leader()
    {
        await using var fx = await ClanFixture.Open();
        var leader = fx.LevelTen("leader", "Leader", HumanClasses.Fighter);
        var member = fx.LevelTen("member", "Member", HumanClasses.Mystic);
        var world = new ZoneWorld(new RangeAoi(50), accounts: fx.Store, clock: fx.Clock.Read);
        await using var host = fx.Host(world);
        await using var left = await ClanPeer.ConnectAsync(host.Port);
        await using var right = await ClanPeer.ConnectAsync(host.Port);
        await left.AuthAsync(leader);
        await right.AuthAsync(member);
        await left.EnterAsync(npcs: 0, others: 0);
        await right.EnterAsync(npcs: 0, others: 0);

        await left.SendAsync(MsgType.ClanCreateRequest, new ClanCreateRequestMsg { Name = "Fine" });
        await left.ReadCreate();
        await left.SendAsync(MsgType.ClanInviteRequest, new ClanInviteRequestMsg { TargetObjectId = member.CharacterId });
        Assert.Equal(ClanProtocol.Target, (await left.ExpectErrorAsync()).Code);
        await left.SendAsync(MsgType.ClanInviteRequest, new ClanInviteRequestMsg { TargetObjectId = leader.CharacterId });
        Assert.Equal(ClanProtocol.Self, (await left.ExpectErrorAsync()).Code);
        await left.SendAsync(MsgType.ClanInviteRequest, new ClanInviteRequestMsg
        {
            TargetObjectId = member.CharacterId,
            PledgeType = -1
        });
        Assert.Equal(ClanProtocol.Pledge, (await left.ExpectErrorAsync()).Code);
    }

    [Fact]
    public async Task Decline_and_timeout_do_not_join()
    {
        await using var fx = await ClanFixture.Open();
        var leader = fx.LevelTen("leader", "Leader", HumanClasses.Fighter);
        var member = fx.LevelTen("member", "Member", HumanClasses.Mystic);
        await using var host = fx.Host();
        await using var left = await ClanPeer.ConnectAsync(host.Port);
        await using var right = await ClanPeer.ConnectAsync(host.Port);
        await left.AuthAsync(leader);
        await right.AuthAsync(member);
        await left.EnterAsync();
        await right.EnterAsync(others: 1);
        await left.ExpectAsync<CharInfoMsg>(MsgType.CharInfo);
        await left.SendAsync(MsgType.ClanCreateRequest, new ClanCreateRequestMsg { Name = "Fine" });
        await left.ReadCreate();
        await right.ExpectAsync<CharInfoMsg>(MsgType.CharInfo);

        await left.SendAsync(MsgType.ClanInviteRequest, new ClanInviteRequestMsg { TargetObjectId = member.CharacterId });
        await right.ExpectAsync<ClanAskJoinMsg>(MsgType.ClanAskJoin);
        await right.SendAsync(MsgType.ClanAnswerJoinRequest, new ClanAnswerJoinRequestMsg
        {
            RequestorObjectId = leader.CharacterId,
            Response = 0
        });
        Assert.Equal(ClanProtocol.InviteDeclinedSelf, (await right.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage)).Id);
        Assert.Equal(ClanProtocol.InviteDeclinedRequestor, (await left.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage)).Id);
        Assert.Equal(0, (await left.ExpectAsync<ClanJoinResultMsg>(MsgType.ClanJoinResult)).Response);
        Assert.Equal(0, fx.Store.FindById(member.Account, member.CharacterId)!.ClanId);

        await left.SendAsync(MsgType.ClanInviteRequest, new ClanInviteRequestMsg { TargetObjectId = member.CharacterId });
        await right.ExpectAsync<ClanAskJoinMsg>(MsgType.ClanAskJoin);
        await left.SendAsync(MsgType.ClanInviteRequest, new ClanInviteRequestMsg { TargetObjectId = member.CharacterId });
        Assert.Equal(ClanProtocol.Busy, (await left.ExpectErrorAsync()).Code);

        fx.Clock.Advance(TimeSpan.FromSeconds(ClanProtocol.InviteTimeoutSeconds));
        await right.SendAsync(MsgType.ClanAnswerJoinRequest, new ClanAnswerJoinRequestMsg
        {
            RequestorObjectId = leader.CharacterId,
            Response = 1
        });
        Assert.Equal(ClanProtocol.Invite, (await right.ExpectErrorAsync()).Code);
    }

    [Fact]
    public async Task Leave_sets_join_penalty_and_kick_sets_recruit_penalty()
    {
        await using var fx = await ClanFixture.Open();
        var leader = fx.LevelTen("leader", "Leader", HumanClasses.Fighter);
        var member = fx.LevelTen("member", "Member", HumanClasses.Mystic);
        var third = fx.LevelTen("third", "Third", HumanClasses.Fighter);
        await using var host = fx.Host();
        await using var left = await ClanPeer.ConnectAsync(host.Port);
        await using var right = await ClanPeer.ConnectAsync(host.Port);
        await using var extra = await ClanPeer.ConnectAsync(host.Port);
        await left.AuthAsync(leader);
        await right.AuthAsync(member);
        await extra.AuthAsync(third);
        await left.EnterAsync();
        await right.EnterAsync(others: 1);
        await left.ExpectAsync<CharInfoMsg>(MsgType.CharInfo);
        await extra.EnterAsync(others: 2);
        await left.ExpectAsync<CharInfoMsg>(MsgType.CharInfo);
        await right.ExpectAsync<CharInfoMsg>(MsgType.CharInfo);

        await left.SendAsync(MsgType.ClanCreateRequest, new ClanCreateRequestMsg { Name = "Fine" });
        await left.ReadCreate();
        await right.ExpectAsync<CharInfoMsg>(MsgType.CharInfo);
        await extra.ExpectAsync<CharInfoMsg>(MsgType.CharInfo);
        await InviteAccept(left, right, leader.CharacterId);
        await extra.ExpectAsync<CharInfoMsg>(MsgType.CharInfo);

        await left.SendAsync(MsgType.ClanLeaveRequest, new ClanLeaveRequestMsg());
        Assert.Equal(ClanProtocol.LeaderLeave, (await left.ExpectErrorAsync()).Code);
        await right.SendAsync(MsgType.ClanKickRequest, new ClanKickRequestMsg { TargetObjectId = leader.CharacterId });
        Assert.Equal(ClanProtocol.Leader, (await right.ExpectErrorAsync()).Code);
        await left.SendAsync(MsgType.ClanKickRequest, new ClanKickRequestMsg { TargetObjectId = leader.CharacterId });
        Assert.Equal(ClanProtocol.DismissSelf, (await left.ExpectErrorAsync()).Code);

        await right.SendAsync(MsgType.ClanLeaveRequest, new ClanLeaveRequestMsg());
        Assert.Equal(ClanProtocol.Withdrawn, (await right.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage)).Id);
        Assert.Equal(ClanProtocol.JoinWait, (await right.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage)).Id);
        Assert.Equal(0, (await right.ExpectAsync<PledgeStatusChangedMsg>(MsgType.PledgeStatusChanged)).ClanId);
        Assert.Empty((await right.ExpectAsync<PledgeMemberListMsg>(MsgType.PledgeMemberList)).Members);
        Assert.Equal(0, (await right.ExpectAsync<UserInfoMsg>(MsgType.UserInfo)).ClanId);

        Assert.Equal(ClanProtocol.MemberWithdrawn, (await left.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage)).Id);
        Assert.Equal(1, (await left.ExpectAsync<PledgeStatusChangedMsg>(MsgType.PledgeStatusChanged)).MemberCount);
        await left.ExpectAsync<PledgeMemberListMsg>(MsgType.PledgeMemberList);
        Assert.Equal(0, (await left.ExpectAsync<CharInfoMsg>(MsgType.CharInfo)).ClanId);
        await extra.ExpectAsync<CharInfoMsg>(MsgType.CharInfo);

        long joinExpiry = fx.Store.FindById(member.Account, member.CharacterId)!.ClanJoinExpiryUnixMs;
        Assert.True(joinExpiry > fx.Clock.Now.ToUnixTimeMilliseconds());

        await left.SendAsync(MsgType.ClanInviteRequest, new ClanInviteRequestMsg { TargetObjectId = third.CharacterId });
        await extra.ExpectAsync<ClanAskJoinMsg>(MsgType.ClanAskJoin);
        await left.SendAsync(MsgType.ClanInviteRequest, new ClanInviteRequestMsg { TargetObjectId = member.CharacterId });
        Assert.Equal(ClanProtocol.Waiting, (await left.ExpectErrorAsync()).Code);

        fx.Clock.Advance(TimeSpan.FromSeconds(ClanProtocol.InviteTimeoutSeconds));
        await left.SendAsync(MsgType.ClanInviteRequest, new ClanInviteRequestMsg { TargetObjectId = member.CharacterId });
        Assert.Equal(ClanProtocol.JoinPenalty, (await left.ExpectErrorAsync()).Code);

        await using var kicked = await ClanFixture.Open();
        var kLeader = kicked.LevelTen("kleader", "KLeader", HumanClasses.Fighter);
        var kMember = kicked.LevelTen("kmember", "KMember", HumanClasses.Mystic);
        var kThird = kicked.LevelTen("kthird", "KThird", HumanClasses.Fighter);
        await using var kHost = kicked.Host();
        await using var ka = await ClanPeer.ConnectAsync(kHost.Port);
        await using var kb = await ClanPeer.ConnectAsync(kHost.Port);
        await using var kc = await ClanPeer.ConnectAsync(kHost.Port);
        await ka.AuthAsync(kLeader);
        await kb.AuthAsync(kMember);
        await kc.AuthAsync(kThird);
        await ka.EnterAsync();
        await kb.EnterAsync(others: 1);
        await ka.ExpectAsync<CharInfoMsg>(MsgType.CharInfo);
        await kc.EnterAsync(others: 2);
        await ka.ExpectAsync<CharInfoMsg>(MsgType.CharInfo);
        await kb.ExpectAsync<CharInfoMsg>(MsgType.CharInfo);
        await ka.SendAsync(MsgType.ClanCreateRequest, new ClanCreateRequestMsg { Name = "Kick" });
        await ka.ReadCreate();
        await kb.ExpectAsync<CharInfoMsg>(MsgType.CharInfo);
        await kc.ExpectAsync<CharInfoMsg>(MsgType.CharInfo);
        await InviteAccept(ka, kb, kLeader.CharacterId);

        await ka.SendAsync(MsgType.ClanKickRequest, new ClanKickRequestMsg { TargetObjectId = kMember.CharacterId });
        Assert.Equal(ClanProtocol.Dismissed, (await kb.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage)).Id);
        await kb.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
        Assert.Equal(0, (await kb.ExpectAsync<PledgeStatusChangedMsg>(MsgType.PledgeStatusChanged)).ClanId);
        await kb.ExpectAsync<PledgeMemberListMsg>(MsgType.PledgeMemberList);
        Assert.Equal(0, (await kb.ExpectAsync<UserInfoMsg>(MsgType.UserInfo)).ClanId);
        Assert.Equal(ClanProtocol.Expelled, (await ka.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage)).Id);
        Assert.Equal(ClanProtocol.ExpelledLeader, (await ka.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage)).Id);
        Assert.Equal(ClanProtocol.RecruitWait, (await ka.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage)).Id);
        var afterKick = await ka.ExpectAsync<PledgeStatusChangedMsg>(MsgType.PledgeStatusChanged);
        Assert.True(afterKick.CharPenaltyExpiryUnixMs > kicked.Clock.Now.ToUnixTimeMilliseconds());
        await ka.ExpectAsync<PledgeMemberListMsg>(MsgType.PledgeMemberList);
        await ka.ExpectAsync<CharInfoMsg>(MsgType.CharInfo);

        await ka.SendAsync(MsgType.ClanInviteRequest, new ClanInviteRequestMsg { TargetObjectId = kThird.CharacterId });
        Assert.Equal(ClanProtocol.RecruitPenalty, (await ka.ExpectErrorAsync()).Code);
    }

    [Fact]
    public async Task Combat_blocks_leave_and_dismiss_until_the_stance_ends()
    {
        await using var fx = await ClanFixture.Open();
        var leader = fx.LevelTen("leader", "Leader", HumanClasses.Fighter);
        var member = fx.LevelTen("member", "Member", HumanClasses.Mystic);
        var gremlin = TalkingIsland.Gremlins[0];
        Assert.True(fx.Store.TrySavePresence(member.Account, member.CharacterId, gremlin.X, gremlin.Y, gremlin.Z, null));
        Assert.True(fx.Store.TrySavePresence(leader.Account, leader.CharacterId, gremlin.X, gremlin.Y, gremlin.Z, null));
        await using var host = fx.Host();
        await using var left = await ClanPeer.ConnectAsync(host.Port);
        await using var right = await ClanPeer.ConnectAsync(host.Port);
        await left.AuthAsync(leader);
        await right.AuthAsync(member);
        await left.EnterAsync();
        await right.EnterAsync(others: 1);
        await left.ExpectAsync<CharInfoMsg>(MsgType.CharInfo);
        await left.SendAsync(MsgType.ClanCreateRequest, new ClanCreateRequestMsg { Name = "Fine" });
        await left.ReadCreate();
        await right.ExpectAsync<CharInfoMsg>(MsgType.CharInfo);
        await InviteAccept(left, right, leader.CharacterId);

        await right.SendAsync(MsgType.AttackRequest, new AttackRequestMsg { TargetObjectId = gremlin.ObjectId });
        await right.ExpectAsync<AttackMsg>(MsgType.Attack);
        await right.ExpectAsync<StatusUpdateMsg>(MsgType.StatusUpdate);
        await left.ExpectAsync<AttackMsg>(MsgType.Attack);
        await left.ExpectAsync<StatusUpdateMsg>(MsgType.StatusUpdate);

        await right.SendAsync(MsgType.ClanLeaveRequest, new ClanLeaveRequestMsg());
        Assert.Equal(ClanProtocol.Combat, (await right.ExpectErrorAsync()).Code);
        await left.SendAsync(MsgType.ClanKickRequest, new ClanKickRequestMsg { TargetObjectId = member.CharacterId });
        Assert.Equal(ClanProtocol.Combat, (await left.ExpectErrorAsync()).Code);

        fx.Clock.Advance(TimeSpan.FromMilliseconds(ClanProtocol.CombatStanceMillis));
        await right.SendAsync(MsgType.ClanLeaveRequest, new ClanLeaveRequestMsg());
        Assert.Equal(ClanProtocol.Withdrawn, (await right.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage)).Id);
        await right.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
        await right.ExpectAsync<PledgeStatusChangedMsg>(MsgType.PledgeStatusChanged);
        await right.ExpectAsync<PledgeMemberListMsg>(MsgType.PledgeMemberList);
        await right.ExpectAsync<UserInfoMsg>(MsgType.UserInfo);
        await left.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
        await left.ExpectAsync<PledgeStatusChangedMsg>(MsgType.PledgeStatusChanged);
        await left.ExpectAsync<PledgeMemberListMsg>(MsgType.PledgeMemberList);
        await left.ExpectAsync<CharInfoMsg>(MsgType.CharInfo);
    }

    [Fact]
    public async Task Dissolve_waits_seven_days_and_cancel_stops_it()
    {
        await using var fx = await ClanFixture.Open();
        var leader = fx.LevelTen("leader", "Leader", HumanClasses.Fighter);
        var member = fx.LevelTen("member", "Member", HumanClasses.Mystic);
        var world = new ZoneWorld(accounts: fx.Store, clock: fx.Clock.Read);
        await using var host = fx.Host(world);
        await using var left = await ClanPeer.ConnectAsync(host.Port);
        await using var right = await ClanPeer.ConnectAsync(host.Port);
        await left.AuthAsync(leader);
        await right.AuthAsync(member);
        await left.EnterAsync();
        await right.EnterAsync(others: 1);
        await left.ExpectAsync<CharInfoMsg>(MsgType.CharInfo);
        await left.SendAsync(MsgType.ClanCreateRequest, new ClanCreateRequestMsg { Name = "Fine" });
        var created = await left.ReadCreate();
        await right.ExpectAsync<CharInfoMsg>(MsgType.CharInfo);
        await InviteAccept(left, right, leader.CharacterId);

        await right.SendAsync(MsgType.ClanDissolveRequest, new ClanDissolveRequestMsg());
        Assert.Equal(ClanProtocol.Leader, (await right.ExpectErrorAsync()).Code);

        long requested = fx.Clock.Now.ToUnixTimeMilliseconds();
        await left.SendAsync(MsgType.ClanDissolveRequest, new ClanDissolveRequestMsg());
        Assert.Equal(ClanProtocol.DissolveRequested, (await left.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage)).Id);
        var status = await left.ExpectAsync<PledgeStatusChangedMsg>(MsgType.PledgeStatusChanged);
        Assert.Equal(ClanRules.DissolveExpiry(requested), status.DissolvingExpiryUnixMs);
        Assert.Equal(ClanProtocol.DissolveRequested, (await right.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage)).Id);
        await right.ExpectAsync<PledgeStatusChangedMsg>(MsgType.PledgeStatusChanged);

        await left.SendAsync(MsgType.ClanDissolveRequest, new ClanDissolveRequestMsg());
        Assert.Equal(ClanProtocol.Dissolving, (await left.ExpectErrorAsync()).Code);

        fx.Clock.Advance(TimeSpan.FromMilliseconds(ClanProtocol.DissolveDays * ClanProtocol.DayMillis - 1));
        await world.CompleteDueDissolutionsAsync(fx.Clock.Now, CancellationToken.None);
        Assert.NotNull(fx.Store.FindClan(created.ClanId));

        await left.SendAsync(MsgType.ClanCancelDissolveRequest, new ClanCancelDissolveRequestMsg());
        Assert.Equal(ClanProtocol.DissolveCancelled, (await left.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage)).Id);
        Assert.Equal(0, (await left.ExpectAsync<PledgeStatusChangedMsg>(MsgType.PledgeStatusChanged)).DissolvingExpiryUnixMs);
        Assert.Equal(ClanProtocol.DissolveCancelled, (await right.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage)).Id);
        await right.ExpectAsync<PledgeStatusChangedMsg>(MsgType.PledgeStatusChanged);

        fx.Clock.Advance(TimeSpan.FromMilliseconds(ClanProtocol.DayMillis));
        await world.CompleteDueDissolutionsAsync(fx.Clock.Now, CancellationToken.None);
        Assert.NotNull(fx.Store.FindClan(created.ClanId));

        long again = fx.Clock.Now.ToUnixTimeMilliseconds();
        await left.SendAsync(MsgType.ClanDissolveRequest, new ClanDissolveRequestMsg());
        await left.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
        await left.ExpectAsync<PledgeStatusChangedMsg>(MsgType.PledgeStatusChanged);
        await right.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
        await right.ExpectAsync<PledgeStatusChangedMsg>(MsgType.PledgeStatusChanged);

        fx.Clock.Now = DateTimeOffset.FromUnixTimeMilliseconds(ClanRules.DissolveExpiry(again));
        await world.CompleteDueDissolutionsAsync(fx.Clock.Now, CancellationToken.None);
        Assert.Equal(ClanProtocol.Dispersed, (await left.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage)).Id);
        Assert.Equal(0, (await left.ExpectAsync<PledgeStatusChangedMsg>(MsgType.PledgeStatusChanged)).ClanId);
        await left.ExpectAsync<PledgeMemberListMsg>(MsgType.PledgeMemberList);
        Assert.Equal(0, (await left.ExpectAsync<UserInfoMsg>(MsgType.UserInfo)).ClanId);
        Assert.Equal(ClanProtocol.Dispersed, (await right.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage)).Id);
        Assert.Equal(0, (await right.ExpectAsync<PledgeStatusChangedMsg>(MsgType.PledgeStatusChanged)).ClanId);
        await right.ExpectAsync<PledgeMemberListMsg>(MsgType.PledgeMemberList);
        Assert.Equal(0, (await right.ExpectAsync<UserInfoMsg>(MsgType.UserInfo)).ClanId);
        await right.ExpectAsync<CharInfoMsg>(MsgType.CharInfo);
        await left.ExpectAsync<CharInfoMsg>(MsgType.CharInfo);
        Assert.Null(fx.Store.FindClan(created.ClanId));

        await left.SendAsync(MsgType.ClanCreateRequest, new ClanCreateRequestMsg { Name = "Again" });
        Assert.Equal(ClanProtocol.CreatePenalty, (await left.ExpectErrorAsync()).Code);

        await right.SendAsync(MsgType.ClanCreateRequest, new ClanCreateRequestMsg { Name = "Next" });
        var next = await right.ReadCreate();
        Assert.True(next.ClanId > 0);
        Assert.Equal(0, fx.Store.FindById(member.Account, member.CharacterId)!.ClanJoinExpiryUnixMs);
    }

    [Fact]
    public async Task Level_0_clan_rejects_an_eleventh_member()
    {
        await using var fx = await ClanFixture.Open();
        var tickets = new List<HandoffTicket>();
        var members = new List<ClanMemberRecord>();
        for (int i = 0; i < 10; i++)
        {
            var ticket = fx.LevelTen($"seat{i}", $"Seat{i}", HumanClasses.Fighter);
            tickets.Add(ticket);
            members.Add(new ClanMemberRecord(ticket.CharacterId, ticket.Account, ticket.CharacterName, ticket.ClassId, 10, 0));
        }

        var recruit = fx.LevelTen("recruit", "Recruit", HumanClasses.Mystic);
        var clan = fx.Store.InsertClan(new ClanRecord(
            0, "Full", 0, tickets[0].CharacterId, 0, 0, 0, members),
            new CharacterClanState(tickets[0].Account, tickets[0].CharacterId, 0, 0, 0));
        Assert.NotNull(clan);
        Assert.Equal(10, clan!.Members.Count);

        await using var host = fx.Host();
        await using var leader = await ClanPeer.ConnectAsync(host.Port);
        await using var target = await ClanPeer.ConnectAsync(host.Port);
        await leader.AuthAsync(tickets[0]);
        await target.AuthAsync(recruit);
        await leader.EnterAsync(inClan: true);
        await target.EnterAsync(others: 1);
        await leader.ExpectAsync<CharInfoMsg>(MsgType.CharInfo);

        await leader.SendAsync(MsgType.ClanInviteRequest, new ClanInviteRequestMsg { TargetObjectId = recruit.CharacterId });
        Assert.Equal(ClanProtocol.Full, (await leader.ExpectErrorAsync()).Code);
    }

    [Fact]
    public async Task Membership_survives_a_new_zone_world()
    {
        await using var fx = await ClanFixture.Open();
        var leader = fx.LevelTen("leader", "Leader", HumanClasses.Fighter);
        await using (var host = fx.Host())
        {
            await using var peer = await ClanPeer.ConnectAsync(host.Port);
            await peer.AuthAsync(leader);
            await peer.EnterAsync();
            await peer.SendAsync(MsgType.ClanCreateRequest, new ClanCreateRequestMsg { Name = "Fine" });
            await peer.ReadCreate();
        }

        await using var again = fx.Host();
        await using var returned = await ClanPeer.ConnectAsync(again.Port);
        await returned.AuthAsync(leader);
        var user = await returned.EnterAsync(inClan: true);
        Assert.True(user.ClanId > 0);
        Assert.Equal(0, user.ClanCrestId);
        Assert.NotNull(returned.EnterStatus);
        Assert.Equal("Fine", returned.EnterStatus!.Name);
        Assert.Equal(user.ClanId, returned.EnterStatus.ClanId);
    }

    static async Task InviteAccept(ClanPeer leader, ClanPeer member, int leaderId)
    {
        await leader.SendAsync(MsgType.ClanInviteRequest, new ClanInviteRequestMsg { TargetObjectId = memberId(member) });
        await member.ExpectAsync<ClanAskJoinMsg>(MsgType.ClanAskJoin);
        await member.SendAsync(MsgType.ClanAnswerJoinRequest, new ClanAnswerJoinRequestMsg
        {
            RequestorObjectId = leaderId,
            Response = 1
        });
        await leader.ExpectAsync<ClanJoinResultMsg>(MsgType.ClanJoinResult);
        await leader.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
        await leader.ExpectAsync<PledgeStatusChangedMsg>(MsgType.PledgeStatusChanged);
        await leader.ExpectAsync<PledgeMemberListMsg>(MsgType.PledgeMemberList);
        await leader.ExpectAsync<CharInfoMsg>(MsgType.CharInfo);
        await member.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
        await member.ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
        await member.ExpectAsync<PledgeStatusChangedMsg>(MsgType.PledgeStatusChanged);
        await member.ExpectAsync<PledgeMemberListMsg>(MsgType.PledgeMemberList);
        await member.ExpectAsync<UserInfoMsg>(MsgType.UserInfo);
    }

    static int memberId(ClanPeer peer) => peer.CharacterId;

    sealed class ManualClock
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset Read() => Now;
        public void Advance(TimeSpan by) => Now += by;
    }

    sealed class ClanFixture : IAsyncDisposable
    {
        public required JsonAccountStore Store { get; init; }
        public required HandoffTokens Tokens { get; init; }
        public required LoginService Login { get; init; }
        public required ManualClock Clock { get; init; }
        public string? Path { get; init; }

        public static Task<ClanFixture> Open()
        {
            var clock = new ManualClock();
            var store = JsonAccountStore.InMemory();
            var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
            return Task.FromResult(new ClanFixture
            {
                Store = store,
                Tokens = tokens,
                Login = new LoginService(store, tokens),
                Clock = clock
            });
        }

        public ZoneHost Host(ZoneWorld? world = null) =>
            ZoneHost.Start(Tokens, world ?? new ZoneWorld(accounts: Store, clock: Clock.Read), Clock.Read);

        public HandoffTicket LevelTen(string account, string name, int classId)
        {
            Assert.True(Login.Register(account, "secret1").Ok);
            Assert.Equal(CreateCharacterStatus.Ok, Store.TryAddCharacter(
                account, name, classId, 0, 0, out var created, level: 10));
            return Tokens.Issue(created!, DateTimeOffset.UtcNow);
        }

        public HandoffTicket Play(string account, string name)
        {
            var ticket = Login.Play(account, "secret1", name, HumanClasses.Fighter);
            Assert.True(ticket.Ok && ticket.Value != null);
            return ticket.Value!;
        }

        public ValueTask DisposeAsync()
        {
            if (Path != null && File.Exists(Path))
                File.Delete(Path);
            return ValueTask.CompletedTask;
        }
    }

    sealed class ClanPeer : IAsyncDisposable
    {
        readonly TcpClient _client;
        readonly NetworkStream _stream;
        public int CharacterId { get; private set; }

        ClanPeer(TcpClient client, NetworkStream stream)
        {
            _client = client;
            _stream = stream;
        }

        public static async Task<ClanPeer> ConnectAsync(int port)
        {
            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            return new ClanPeer(client, client.GetStream());
        }

        public async Task AuthAsync(HandoffTicket ticket)
        {
            CharacterId = ticket.CharacterId;
            await SendAsync(MsgType.Hello, new HelloMsg());
            await ExpectAsync<HelloAckMsg>(MsgType.HelloAck);
            await SendAsync(MsgType.Auth, ticket.ToZoneAuth());
            await ExpectAsync<AuthOkMsg>(MsgType.AuthOk);
        }

        public PledgeStatusChangedMsg? EnterStatus { get; private set; }

        public async Task<UserInfoMsg> EnterAsync(int? npcs = null, int others = 0, bool inClan = false)
        {
            int npcCount = npcs ?? TalkingIsland.Gremlins.Length;
            await SendAsync(MsgType.EnterWorld, new EnterWorldMsg());
            var user = await ExpectAsync<UserInfoMsg>(MsgType.UserInfo);
            await ExpectAsync<ItemListMsg>(MsgType.ItemList);
            await ExpectAsync<PlayerSpawnedMsg>(MsgType.PlayerSpawned);
            for (int i = 0; i < npcCount; i++)
                await ExpectAsync<NpcInfoMsg>(MsgType.NpcInfo);
            for (int i = 0; i < others; i++)
                await ExpectAsync<CharInfoMsg>(MsgType.CharInfo);
            if (inClan)
            {
                EnterStatus = await ExpectAsync<PledgeStatusChangedMsg>(MsgType.PledgeStatusChanged);
                await ExpectAsync<PledgeMemberListMsg>(MsgType.PledgeMemberList);
            }

            await ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
            return user;
        }

        public async Task<PledgeStatusChangedMsg> ReadCreate()
        {
            await ExpectAsync<SystemMessageMsg>(MsgType.SystemMessage);
            var status = await ExpectAsync<PledgeStatusChangedMsg>(MsgType.PledgeStatusChanged);
            await ExpectAsync<PledgeMemberListMsg>(MsgType.PledgeMemberList);
            await ExpectAsync<UserInfoMsg>(MsgType.UserInfo);
            return status;
        }

        public async Task SendAsync<T>(MsgType type, T message)
        {
            byte[] body = MessagePackSerializer.Serialize(message);
            await FrameIO.SendAsync(_stream, (ushort)type, body);
        }

        public async Task<T> ExpectAsync<T>(MsgType expected)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var frame = await FrameIO.ReadAsync(_stream, cts.Token);
            Assert.NotNull(frame);
            if (frame!.Value.Type != (ushort)expected)
            {
                string detail = "";
                if (frame.Value.Type == (ushort)MsgType.Error)
                {
                    var err = MessagePackSerializer.Deserialize<ErrorMsg>(frame.Value.Body);
                    detail = $" {err.Code} {err.Message}";
                }

                Assert.Fail($"expected {expected}, got {(MsgType)frame.Value.Type}{detail}");
            }

            return MessagePackSerializer.Deserialize<T>(frame.Value.Body);
        }

        public async Task<ErrorMsg> ExpectErrorAsync() => await ExpectAsync<ErrorMsg>(MsgType.Error);

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await Task.CompletedTask;
        }
    }
}
