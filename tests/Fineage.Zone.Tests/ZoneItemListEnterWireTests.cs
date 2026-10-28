using System.Net;
using System.Net.Sockets;
using Fineage.Login;
using Fineage.Protocol;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Slice A1 — enter-world inventory snapshot on the zone wire.</summary>
public sealed class ZoneItemListEnterWireTests
{
    [Fact]
    public async Task EnterWorld_sends_UserInfo_then_ItemList_then_SkillList_then_PlayerSpawned_with_starter_kit()
    {
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var server = new ZoneServer(tokens);
        var login = new LoginService(JsonAccountStore.InMemory(), tokens);
        var ticket = login.Play("kit", "pass1234", "Starter", HumanClasses.Fighter);
        Assert.True(ticket.Ok && ticket.Value != null);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var cts = new CancellationTokenSource();
        var acceptTask = Task.Run(async () =>
        {
            using var accepted = await listener.AcceptTcpClientAsync(cts.Token);
            await using var stream = accepted.GetStream();
            await server.HandleAsync(stream, cts.Token);
        });

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await using var stream = client.GetStream();

        await FrameIO.SendAsync(stream, (ushort)MsgType.Hello, MessagePackSerializer.Serialize(new HelloMsg()));
        await ReadAsync(stream, MsgType.HelloAck);
        await FrameIO.SendAsync(stream, (ushort)MsgType.Auth,
            MessagePackSerializer.Serialize(ticket.Value!.ToZoneAuth()));
        await ReadAsync(stream, MsgType.AuthOk);
        await FrameIO.SendAsync(stream, (ushort)MsgType.EnterWorld, MessagePackSerializer.Serialize(new EnterWorldMsg()));

        var userFrame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(userFrame);
        Assert.Equal((ushort)MsgType.UserInfo, userFrame!.Value.Type);
        var user = MessagePackSerializer.Deserialize<UserInfoMsg>(userFrame.Value.Body);
        Assert.Equal(ticket.Value.CharacterId, user.ObjectId);

        var listFrame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(listFrame);
        Assert.Equal((ushort)MsgType.ItemList, listFrame!.Value.Type);
        var list = MessagePackSerializer.Deserialize<ItemListMsg>(listFrame.Value.Body);
        var sword = Assert.Single(list.Items, row => row.ItemId == 2369);
        Assert.Equal(7, sword.EquippedSlot);
        Assert.Equal("rhand", sword.BodySlot);
        Assert.True(sword.ObjectId >= 100_000);
        Assert.Equal(1014, (int)MsgType.ItemList);

        var skillFrame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(skillFrame);
        Assert.Equal((ushort)MsgType.SkillList, skillFrame!.Value.Type);
        var skills = MessagePackSerializer.Deserialize<SkillListMsg>(skillFrame.Value.Body);
        Assert.Contains(skills.Skills, row => row.SkillId == 194 && row.Level == 1);

        var spawnFrame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(spawnFrame);
        Assert.Equal((ushort)MsgType.PlayerSpawned, spawnFrame!.Value.Type);

        listener.Stop();
        await cts.CancelAsync();
        try
        {
            await acceptTask;
        }
        catch (OperationCanceledException)
        {
        }
    }

    static async Task ReadAsync(NetworkStream stream, MsgType type)
    {
        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)type, frame!.Value.Type);
    }
}
