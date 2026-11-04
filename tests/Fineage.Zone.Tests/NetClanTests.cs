using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Clan;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Clan lifecycle over Net: create, invite, accept, leave, kick, dissolve, and cancel dissolve all run through
/// the same V2 world code, so these tests assert the Net projection (status, roster, invite, join result) that
/// <c>PublishClanNet</c> replays from the V2 outbound list.
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
}
