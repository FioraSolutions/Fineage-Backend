using Fineage.Protocol;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Command channel (MPCC): invite second party, oust, leave.</summary>
public sealed class NetCommandChannelTests
{
    static (NetTestKit Kit, NetBridgeRecorder Rec) World()
    {
        var kit = new NetTestKit();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return (kit, rec);
    }

    static async Task<(ZoneSession Leader, ZoneEntity LeaderEntity, ZoneSession Member, ZoneEntity MemberEntity)> PartyOfTwo(NetTestKit kit)
    {
        var (leader, leaderEntity) = await kit.EnterAsync(level: 40);
        var (member, memberEntity) = await kit.EnterAsync(level: 20);
        Assert.Null(await kit.World.TryPartyInviteAsync(leader, memberEntity.ObjectId, 0, default));
        Assert.Null(await kit.World.TryPartyAnswerAsync(member, leaderEntity.ObjectId, 1, default));
        return (leader, leaderEntity, member, memberEntity);
    }

    static async Task ClanLeaderLevel5(NetTestKit kit, ZoneSession leader, ZoneEntity leaderEntity)
    {
        Assert.Null(await kit.World.TryClanCreateAsync(leader, new ClanCreateRequestMsg { Name = "MpccClan" + leader.ObjectId }, kit.Now, default));
        Assert.True(kit.World.SetClanLevelForTest(leaderEntity.ClanId, CommandChannelProtocol.MinClanLevel));
    }

    [Fact]
    public async Task Formation_gate_requires_clan_leader_level5_or_alternate_paths()
    {
        var (kit, _) = World();
        var (inviter, inviterEntity, _, _) = await PartyOfTwo(kit);
        var (_, _, _, otherEntity) = await PartyOfTwo(kit);

        Assert.Equal("command_channel_formation", await kit.World.TryCommandChannelAskAsync(inviter, otherEntity.Name, default));

        await ClanLeaderLevel5(kit, inviter, inviterEntity);
        Assert.Null(await kit.World.TryCommandChannelAskAsync(inviter, otherEntity.Name, default));
    }

    [Fact]
    public async Task Accept_creates_channel_and_fanouts_open_and_info()
    {
        var (kit, rec) = World();
        var (inviter, inviterEntity, _, _) = await PartyOfTwo(kit);
        var (acceptorLeader, _, _, acceptorEntity) = await PartyOfTwo(kit);
        await ClanLeaderLevel5(kit, inviter, inviterEntity);

        Assert.Null(await kit.World.TryCommandChannelAskAsync(inviter, acceptorEntity.Name, default));
        Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutCommandChannelAskJoin)));

        rec.Calls.Clear();
        Assert.Null(await kit.World.TryCommandChannelAcceptAsync(acceptorLeader, 1, default));

        Assert.NotEmpty(rec.Of(nameof(IWorldEventPublisher.FanoutCommandChannelOpen)));
        var info = Assert.IsType<CommandChannelInfoMsg>(
            rec.Of(nameof(IWorldEventPublisher.FanoutCommandChannelInfo)).Last()[1]);
        Assert.Equal(2, info.Parties.Length);
        Assert.Equal(4, info.MemberCount);
    }

    [Fact]
    public async Task Oust_removes_second_party_and_closes_window()
    {
        var (kit, rec) = World();
        var (inviter, inviterEntity, _, _) = await PartyOfTwo(kit);
        var (acceptorLeader, _, _, acceptorEntity) = await PartyOfTwo(kit);
        await ClanLeaderLevel5(kit, inviter, inviterEntity);

        Assert.Null(await kit.World.TryCommandChannelAskAsync(inviter, acceptorEntity.Name, default));
        Assert.Null(await kit.World.TryCommandChannelAcceptAsync(acceptorLeader, 1, default));
        rec.Calls.Clear();

        Assert.Null(await kit.World.TryCommandChannelOustAsync(inviter, acceptorLeader.Name, default));
        Assert.NotEmpty(rec.Of(nameof(IWorldEventPublisher.FanoutCommandChannelClose)));
    }

    [Fact]
    public async Task Leaving_second_party_dissolves_command_channel()
    {
        var (kit, rec) = World();
        var (inviter, inviterEntity, _, _) = await PartyOfTwo(kit);
        var (acceptorLeader, _, _, acceptorEntity) = await PartyOfTwo(kit);
        await ClanLeaderLevel5(kit, inviter, inviterEntity);

        Assert.Null(await kit.World.TryCommandChannelAskAsync(inviter, acceptorEntity.Name, default));
        Assert.Null(await kit.World.TryCommandChannelAcceptAsync(acceptorLeader, 1, default));
        rec.Calls.Clear();

        Assert.Null(await kit.World.TryPartyLeaveAsync(acceptorLeader, default));
        Assert.NotEmpty(rec.Of(nameof(IWorldEventPublisher.FanoutCommandChannelClose)));
    }

    [Fact]
    public async Task Commander_chat_reaches_command_channel_members()
    {
        var (kit, rec) = World();
        var (inviter, inviterEntity, _, _) = await PartyOfTwo(kit);
        var (acceptorLeader, _, _, acceptorEntity) = await PartyOfTwo(kit);
        await ClanLeaderLevel5(kit, inviter, inviterEntity);

        Assert.Null(await kit.World.TryCommandChannelAskAsync(inviter, acceptorEntity.Name, default));
        Assert.Null(await kit.World.TryCommandChannelAcceptAsync(acceptorLeader, 1, default));
        rec.Calls.Clear();

        Assert.Null(await kit.World.TrySayAsync(inviter, "cc orders", ChatProtocol.PartyRoomCommander, kit.Now, default));
        Assert.NotEmpty(rec.Of(nameof(IWorldEventPublisher.FanoutCreatureSay)));
    }
}