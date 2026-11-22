using System.Linq;
using Fineage.Protocol;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class NetFriendChatTests
{
    static (NetTestKit Kit, NetBridgeRecorder Rec) World()
    {
        var kit = new NetTestKit();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return (kit, rec);
    }

    static List<(IReadOnlyList<int> Watchers, CreatureSayMsg Line)> Says(NetBridgeRecorder rec) =>
        rec.Of(nameof(IWorldEventPublisher.FanoutCreatureSay))
            .Select(args => ((IReadOnlyList<int>)args[0], (CreatureSayMsg)args[1]))
            .ToList();

    [Fact]
    public async Task Friend_chat_delivers_type_12_to_both_peers()
    {
        var (kit, rec) = World();
        var a = kit.Create();
        var b = kit.Create();
        var (sessionA, _) = await kit.EnterAsync(a);
        var (sessionB, _) = await kit.EnterAsync(b);

        Assert.Null(await kit.World.TryFriendInviteAsync(sessionA, new FriendInviteMsg { Name = b.Name }, default));
        Assert.Null(await kit.World.TryFriendAnswerAsync(sessionB, new FriendAnswerMsg { Response = 1 }, default));
        rec.Calls.Clear();

        Assert.Null(await kit.World.TrySayAsync(sessionA, "hi friend", ChatProtocol.Friend, DateTimeOffset.UtcNow,
            CancellationToken.None, b.Name));

        var lines = Says(rec);
        Assert.Equal(2, lines.Count);
        Assert.All(lines, s => Assert.Equal(ChatProtocol.Friend, s.Line.ChatType));
        Assert.Contains(lines, s => s.Line.Text == "hi friend" && s.Line.Name != null && s.Line.Name.StartsWith("->"));
    }

    [Fact]
    public async Task Friend_chat_rejects_non_friends()
    {
        var (kit, rec) = World();
        var a = kit.Create();
        var b = kit.Create();
        var (sessionA, _) = await kit.EnterAsync(a);
        await kit.EnterAsync(b);
        rec.Calls.Clear();

        Assert.Null(await kit.World.TrySayAsync(sessionA, "nope", ChatProtocol.Friend, DateTimeOffset.UtcNow,
            CancellationToken.None, b.Name));
        Assert.Empty(Says(rec));
        var notice = Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)));
        var msg = (SystemMessageMsg)notice[1]!;
        Assert.Equal("That player is not on your friend list.", msg.Text);
    }
}