using Fineage.Protocol;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Interlude (Mobius) rules for the player-to-player actions that ride on RequestActionUse, trade and party:
/// when a social gesture is allowed, what sitting does to gait and pending orders, trade teardown on death,
/// party leader hand-over and the roster refresh that keeps party HP/MP bars live.
/// </summary>
public sealed class NetInteractionRulesTests
{
    static (NetTestKit Kit, NetBridgeRecorder Rec) World()
    {
        var kit = new NetTestKit();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return (kit, rec);
    }

    static Task<string?> Act(NetTestKit kit, ZoneSession s, int action) =>
        kit.World.TryCharacterActionAsync(s, action, 0, default);

    [Fact]
    public async Task Social_gesture_is_refused_while_sitting_running_or_trading_and_fans_when_idle()
    {
        var (kit, rec) = World();
        var (a, aEntity) = await kit.EnterAsync();
        var (b, bEntity) = await kit.EnterAsync();
        string social = nameof(IWorldEventPublisher.FanoutSocialAction);

        Assert.Null(await Act(kit, a, ActionProtocol.SitStand));
        Assert.Equal(ActionProtocol.Blocked, await Act(kit, a, ActionProtocol.SocialGreeting));
        Assert.Null(await Act(kit, a, ActionProtocol.SitStand));

        Assert.Null(await kit.World.TryMoveAsync(a, aEntity.X + 500, aEntity.Y, aEntity.Z, default));
        Assert.True(aEntity.MoveActive);
        Assert.Equal(ActionProtocol.Blocked, await Act(kit, a, ActionProtocol.SocialVictory));
        kit.World.StopMove(a);

        Assert.Null(await kit.World.TryTradeRequestAsync(a, new TradeRequestMsg { TargetObjectId = bEntity.ObjectId }, default));
        Assert.Equal(ActionProtocol.Blocked, await Act(kit, b, ActionProtocol.SocialAdvance));
        Assert.Null(await kit.World.TryTradeAnswerAsync(b, new TradeAnswerRequestMsg { RequestorObjectId = aEntity.ObjectId, Response = 0 }, default));
        Assert.Empty(rec.Of(social));

        Assert.Null(await Act(kit, b, ActionProtocol.SocialAdvance));
        var call = Assert.Single(rec.Of(social));
        var watchers = Assert.IsAssignableFrom<IReadOnlyList<int>>(call[0]);
        Assert.Contains(aEntity.ObjectId, watchers);   // the peer in view sees the emote
        Assert.Contains(bEntity.ObjectId, watchers);
    }

    [Fact]
    public async Task Sitting_keeps_the_run_gait_and_fans_only_the_wait_type()
    {
        var (kit, rec) = World();
        var (a, aEntity) = await kit.EnterAsync();
        Assert.True(aEntity.Running);

        Assert.Null(await Act(kit, a, ActionProtocol.SitStand));
        Assert.True(aEntity.Sitting);
        Assert.True(aEntity.Running);
        Assert.Empty(rec.Of(nameof(IWorldEventPublisher.FanoutChangeMoveType)));
        Assert.Equal(ActionProtocol.Sitting, await kit.World.TryMoveAsync(a, aEntity.X + 100, aEntity.Y, aEntity.Z, default));

        Assert.Null(await Act(kit, a, ActionProtocol.SitStand));
        Assert.False(aEntity.Sitting);
        Assert.True(aEntity.Running);
    }

    [Fact]
    public async Task A_new_move_order_cancels_sit_on_arrival()
    {
        var (kit, _) = World();
        var (a, aEntity) = await kit.EnterAsync();
        Assert.Null(await kit.World.TryMoveAsync(a, aEntity.X + 500, aEntity.Y, aEntity.Z, default));
        Assert.Null(await Act(kit, a, ActionProtocol.SitStand));
        Assert.True(a.SitOnArrive);
        Assert.False(aEntity.Sitting);

        Assert.Null(await kit.World.TryMoveAsync(a, aEntity.X, aEntity.Y + 500, aEntity.Z, default));
        Assert.False(a.SitOnArrive);
    }

    [Fact]
    public async Task Death_closes_an_open_trade_on_both_sides_without_moving_items()
    {
        var (kit, rec) = World();
        var (a, aEntity) = await kit.EnterAsync(classId: 0);
        var (b, bEntity) = await kit.EnterAsync(classId: 1);
        var offered = aEntity.Inventory!.Items.First(i => !i.IsEquipped && i.Template.CanDropTradeStore && i.Count >= 1);

        Assert.Null(await kit.World.TryTradeRequestAsync(a, new TradeRequestMsg { TargetObjectId = bEntity.ObjectId }, default));
        Assert.Null(await kit.World.TryTradeAnswerAsync(b, new TradeAnswerRequestMsg { RequestorObjectId = aEntity.ObjectId, Response = 1 }, default));
        Assert.Null(await kit.World.TryTradeAddItemAsync(a, new TradeAddItemRequestMsg { InventoryObjectId = offered.ObjectId, Count = 1 }, default));

        Assert.Null(await kit.World.TryAdminKillPlayerAsync(b, default));

        var finished = rec.Of(nameof(IWorldEventPublisher.FanoutTradeFinished));
        Assert.Equal(2, finished.Count);
        Assert.All(finished, call => Assert.Equal(0, Assert.IsType<TradeFinishedMsg>(call[1]).Response));
        var owners = finished.SelectMany(call => Assert.IsAssignableFrom<IReadOnlyList<int>>(call[0])).ToHashSet();
        Assert.Equal(new HashSet<int> { aEntity.ObjectId, bEntity.ObjectId }, owners);
        Assert.Contains(aEntity.Inventory.Items, i => i.ObjectId == offered.ObjectId);
        Assert.NotNull(await kit.World.TryTradeDoneAsync(a, new TradeDoneRequestMsg { Response = 1 }, default));
    }

    static async Task<(ZoneSession Leader, ZoneEntity LeaderEntity, ZoneSession Member, ZoneEntity MemberEntity)> Party(NetTestKit kit)
    {
        var (leader, leaderEntity) = await kit.EnterAsync();
        var (member, memberEntity) = await kit.EnterAsync();
        Assert.Null(await kit.World.TryPartyInviteAsync(leader, memberEntity.ObjectId, 0, default));
        Assert.Null(await kit.World.TryPartyAnswerAsync(member, leader.ObjectId, 1, default));
        return (leader, leaderEntity, member, memberEntity);
    }

    [Fact]
    public async Task Leader_hands_over_the_party_and_everyone_gets_the_new_roster()
    {
        var (kit, rec) = World();
        var (leader, leaderEntity, member, memberEntity) = await Party(kit);

        Assert.Equal("party_leader", await kit.World.TryPartyChangeLeaderAsync(member, leaderEntity.ObjectId, default));
        Assert.Equal("target", await kit.World.TryPartyChangeLeaderAsync(leader, leaderEntity.ObjectId, default));
        int before = rec.Of(nameof(IWorldEventPublisher.FanoutPartyMemberList)).Count;

        Assert.Null(await kit.World.TryPartyChangeLeaderAsync(leader, memberEntity.ObjectId, default));

        var lists = rec.Of(nameof(IWorldEventPublisher.FanoutPartyMemberList));
        Assert.Equal(before + 1, lists.Count);
        var roster = Assert.IsType<PartyMemberListMsg>(lists[^1][1]);
        Assert.Equal(memberEntity.ObjectId, roster.LeaderObjectId);
        Assert.Equal(memberEntity.ObjectId, roster.Members[0].ObjectId);
        Assert.Equal(
            new HashSet<int> { leaderEntity.ObjectId, memberEntity.ObjectId },
            Assert.IsAssignableFrom<IReadOnlyList<int>>(lists[^1][0]).ToHashSet());

        // Only the new leader may kick now.
        Assert.Equal("party_leader", await kit.World.TryPartyKickAsync(leader, memberEntity.ObjectId, default));
        Assert.Null(await kit.World.TryPartyKickAsync(member, leaderEntity.ObjectId, default));
    }

    [Fact]
    public async Task A_members_hp_change_refreshes_the_party_roster_with_the_new_bars()
    {
        var (kit, rec) = World();
        var (leader, leaderEntity, member, memberEntity) = await Party(kit);
        Assert.Null(await kit.World.TryAdminKillPlayerAsync(member, default));
        int before = rec.Of(nameof(IWorldEventPublisher.FanoutPartyMemberList)).Count;

        Assert.Null(await kit.World.TryReviveAsync(member, DeathProtocol.ReviveToVillage, default));

        var lists = rec.Of(nameof(IWorldEventPublisher.FanoutPartyMemberList));
        Assert.True(lists.Count > before);
        var roster = Assert.IsType<PartyMemberListMsg>(lists[^1][1]);
        var row = Assert.Single(roster.Members, m => m.ObjectId == memberEntity.ObjectId);
        Assert.Equal(memberEntity.CurHp, row.CurHp);
        Assert.True(row.CurHp > 0);
        Assert.Contains(leaderEntity.ObjectId, Assert.IsAssignableFrom<IReadOnlyList<int>>(lists[^1][0]));
    }

    [Fact]
    public void Party_change_leader_request_round_trips_on_its_own_opcode()
    {
        Assert.Equal((ushort)0x03A8, Fineage.Net.Contract.NetOpcode.PartyChangeLeaderRequest);
        var payload = Fineage.Net.Contract.NetCodec.EncodePartyChangeLeaderRequest(
            new Fineage.Net.Contract.PartyChangeLeaderRequestNetMsg { TargetObjectId = 4242 });
        Assert.Equal(4242, Fineage.Net.Contract.NetCodec.DecodePartyChangeLeaderRequest(payload).TargetObjectId);
    }

    [Fact]
    public async Task Disconnect_removes_the_member_and_closes_a_two_man_party()
    {
        var (kit, rec) = World();
        var (leader, leaderEntity, member, _) = await Party(kit);
        await kit.World.LeaveAsync(member, default);

        var roster = Assert.IsType<PartyMemberListMsg>(rec.Of(nameof(IWorldEventPublisher.FanoutPartyMemberList))[^1][1]);
        Assert.Empty(roster.Members);
        Assert.Equal("party", await kit.World.TryPartyLeaveAsync(leader, default));
        Assert.Equal(leaderEntity.ObjectId, Assert.IsAssignableFrom<IReadOnlyList<int>>(
            rec.Of(nameof(IWorldEventPublisher.FanoutPartyMemberList))[^1][0]).Single());
    }
}
