using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Party lifecycle over Net: invite, accept, the roster fanout, kick, and the empty list that closes the last
/// member's window. Every case drives the public <c>TryParty*</c> entry points and asserts the bridge fanout.
/// </summary>
public sealed class NetPartyTests
{
    static (NetTestKit Kit, NetBridgeRecorder Rec) World()
    {
        var kit = new NetTestKit();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return (kit, rec);
    }

    static Fineage.Protocol.PartyMemberListMsg LastList(NetBridgeRecorder rec) =>
        Assert.IsType<Fineage.Protocol.PartyMemberListMsg>(rec.Of(nameof(IWorldEventPublisher.FanoutPartyMemberList)).Last()[1]);

    static async Task<(ZoneSession Leader, ZoneSession Member)> Joined(NetTestKit kit)
    {
        var (leader, _) = await kit.EnterAsync();
        var (member, memberEntity) = await kit.EnterAsync();
        Assert.Null(await kit.World.TryPartyInviteAsync(leader, memberEntity.ObjectId, 0, default));
        Assert.Null(await kit.World.TryPartyAnswerAsync(member, leader.ObjectId, 1, default));
        return (leader, member);
    }

    [Fact]
    public async Task Invite_asks_the_target_and_accept_publishes_the_roster_to_both()
    {
        var (kit, rec) = World();
        var (leader, leaderEntity) = await kit.EnterAsync();
        var (member, memberEntity) = await kit.EnterAsync();

        Assert.Null(await kit.World.TryPartyInviteAsync(leader, memberEntity.ObjectId, 0, default));

        var ask = Assert.IsType<Fineage.Protocol.PartyAskJoinMsg>(
            Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutPartyAskJoin)))[1]);
        Assert.Equal(leaderEntity.ObjectId, ask.RequestorObjectId);
        Assert.Equal(leaderEntity.Name, ask.RequestorName);

        Assert.Null(await kit.World.TryPartyAnswerAsync(member, leaderEntity.ObjectId, 1, default));

        Assert.Equal(1, Assert.IsType<Fineage.Protocol.PartyJoinResultMsg>(
            Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutPartyJoinResult)))[1]).Response);

        var lists = rec.Of(nameof(IWorldEventPublisher.FanoutPartyMemberList));
        Assert.NotEmpty(lists);
        var roster = LastList(rec);
        Assert.Equal(leaderEntity.ObjectId, roster.LeaderObjectId);
        Assert.Equal(2, roster.Members.Length);
        Assert.Contains(roster.Members, row => row.ObjectId == memberEntity.ObjectId);
    }

    [Fact]
    public async Task A_declined_invite_reports_zero_and_leaves_no_party()
    {
        var (kit, rec) = World();
        var (leader, leaderEntity) = await kit.EnterAsync();
        var (member, memberEntity) = await kit.EnterAsync();

        Assert.Null(await kit.World.TryPartyInviteAsync(leader, memberEntity.ObjectId, 0, default));
        Assert.Null(await kit.World.TryPartyAnswerAsync(member, leaderEntity.ObjectId, 0, default));

        Assert.Equal(0, Assert.IsType<Fineage.Protocol.PartyJoinResultMsg>(
            Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutPartyJoinResult)))[1]).Response);
        Assert.Empty(rec.Of(nameof(IWorldEventPublisher.FanoutPartyMemberList)));
    }

    [Fact]
    public async Task The_last_member_leaving_gets_an_empty_list_that_closes_the_window()
    {
        var (kit, rec) = World();
        var (leader, _) = await Joined(kit);

        // Two members in, so the first leave leaves a party of one; that last member is the one the empty
        // list is addressed to (Mobius sends PartySmallWindowDeleteAll).
        Assert.Null(await kit.World.TryPartyLeaveAsync(leader, default));
        var closed = LastList(rec);
        Assert.Equal(0, closed.PartyId);
        Assert.Equal(0, closed.LeaderObjectId);
        Assert.Empty(closed.Members);
        Assert.Contains(rec.Of(nameof(IWorldEventPublisher.FanoutPartyMemberList)),
            call => ((IReadOnlyList<int>)call[0]).Count == 1);
    }

    [Fact]
    public async Task Only_the_leader_can_kick_and_kicking_needs_a_party_member()
    {
        var (kit, _) = World();
        var (leader, member) = await Joined(kit);
        var (outsider, outsiderEntity) = await kit.EnterAsync();

        Assert.Equal("party_leader", await kit.World.TryPartyKickAsync(member, leader.ObjectId, default));
        Assert.Equal("target", await kit.World.TryPartyKickAsync(leader, outsiderEntity.ObjectId, default));

        Assert.Null(await kit.World.TryPartyKickAsync(leader, member.ObjectId, default));
        _ = outsider;
    }

    [Fact]
    public async Task Inviting_a_member_of_another_party_is_refused_as_busy()
    {
        var (kit, _) = World();
        var (leader, member) = await Joined(kit);
        var (other, otherEntity) = await kit.EnterAsync();

        Assert.Equal("party_busy", await kit.World.TryPartyInviteAsync(other, member.ObjectId, 0, default));

        // Two invites to the same target in a row: the second is busy on the pending one.
        Assert.Null(await kit.World.TryPartyInviteAsync(leader, otherEntity.ObjectId, 0, default));
        Assert.Equal("party_busy", await kit.World.TryPartyInviteAsync(leader, otherEntity.ObjectId, 0, default));
    }
}
