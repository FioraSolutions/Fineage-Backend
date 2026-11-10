using Fineage.Protocol;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Friends + block (#285): invite → accept → list → block → delete.</summary>
public sealed class NetFriendTests
{
    static (NetTestKit Kit, NetBridgeRecorder Rec) World()
    {
        var kit = new NetTestKit();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return (kit, rec);
    }

    static object? Last(NetBridgeRecorder rec, string method) =>
        rec.Of(method).Count == 0 ? null : rec.Of(method).Last()[1];

    [Fact]
    public async Task Invite_accept_lists_and_persists()
    {
        var (kit, rec) = World();
        var a = kit.Create();
        var b = kit.Create();
        var (sessionA, playerA) = await kit.EnterAsync(a);
        var (sessionB, playerB) = await kit.EnterAsync(b);

        Assert.Null(await kit.World.TryFriendInviteAsync(sessionA, new FriendInviteMsg { Name = b.Name }, default));
        var ask = Assert.IsType<Fineage.Protocol.FriendInviteAskMsg>(
            Last(rec, nameof(IWorldEventPublisher.FanoutFriendInviteAsk)));
        Assert.Equal(a.Name, ask.RequestorName);

        Assert.Null(await kit.World.TryFriendAnswerAsync(sessionB, new FriendAnswerMsg { Response = 1 }, default));
        Assert.Contains(playerB.ObjectId, playerA.Friends.Keys);
        Assert.Contains(playerA.ObjectId, playerB.Friends.Keys);

        Assert.Null(await kit.World.TryFriendListAsync(sessionA, default));
        var list = Assert.IsType<Fineage.Protocol.FriendListMsg>(
            Last(rec, nameof(IWorldEventPublisher.FanoutFriendList)));
        Assert.Single(list.Friends);
        Assert.Equal(playerB.ObjectId, list.Friends[0].ObjectId);
        Assert.Equal(1, list.Friends[0].Online);

        var storedA = kit.Store.FindById(a.Account, a.CharacterId);
        Assert.NotNull(storedA?.Friends);
        Assert.Contains(storedA!.Friends!, f => f.ObjectId == b.CharacterId);
    }

    [Fact]
    public async Task Block_prevents_invite_and_lists()
    {
        var (kit, _) = World();
        var a = kit.Create();
        var b = kit.Create();
        var (sessionA, playerA) = await kit.EnterAsync(a);
        var (sessionB, _) = await kit.EnterAsync(b);

        Assert.Null(await kit.World.TryBlockRequestAsync(sessionA, new BlockRequestMsg
        {
            Type = FriendProtocol.BlockAdd,
            Name = b.Name
        }, default));
        Assert.Contains(b.CharacterId, playerA.Blocked.Keys);

        Assert.Equal(FriendProtocol.Blocked,
            await kit.World.TryFriendInviteAsync(sessionB, new FriendInviteMsg { Name = a.Name }, default));

        Assert.Null(await kit.World.TryBlockRequestAsync(sessionA, new BlockRequestMsg
        {
            Type = FriendProtocol.BlockList
        }, default));
    }

    [Fact]
    public async Task Delete_removes_both_sides()
    {
        var (kit, _) = World();
        var a = kit.Create();
        var b = kit.Create();
        var (sessionA, playerA) = await kit.EnterAsync(a);
        var (sessionB, playerB) = await kit.EnterAsync(b);

        Assert.Null(await kit.World.TryFriendInviteAsync(sessionA, new FriendInviteMsg { Name = b.Name }, default));
        Assert.Null(await kit.World.TryFriendAnswerAsync(sessionB, new FriendAnswerMsg { Response = 1 }, default));

        Assert.Null(await kit.World.TryFriendDeleteAsync(sessionA, new FriendDeleteMsg { Name = b.Name }, default));
        Assert.Empty(playerA.Friends);
        Assert.Empty(playerB.Friends);
    }

    [Fact]
    public async Task Invite_self_is_refused()
    {
        var (kit, _) = World();
        var (session, player) = await kit.EnterAsync();
        Assert.Equal(FriendProtocol.Self,
            await kit.World.TryFriendInviteAsync(session, new FriendInviteMsg { Name = player.Name }, default));
    }
}
