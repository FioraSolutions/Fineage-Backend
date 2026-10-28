using System.Net;
using System.Net.Sockets;
using Fineage.Login;
using Fineage.Protocol;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZoneCombatStubTests
{
    [Fact]
    public async Task EnterWorld_spawns_player_and_npcs()
    {
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var login = new LoginService(JsonAccountStore.InMemory(), tokens);
        var ticket = login.Play("hero", "pass1234", "Fighter", HumanClasses.Fighter);
        Assert.True(ticket.Ok && ticket.Value != null);

        var server = new ZoneServer(tokens);
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
        await ReadExpectAsync(stream, MsgType.HelloAck);

        await FrameIO.SendAsync(stream, (ushort)MsgType.Auth,
            MessagePackSerializer.Serialize(ticket.Value!.ToZoneAuth()));
        await ReadExpectAsync(stream, MsgType.AuthOk);

        await FrameIO.SendAsync(stream, (ushort)MsgType.EnterWorld, MessagePackSerializer.Serialize(new EnterWorldMsg()));
        await ReadExpectAsync(stream, MsgType.UserInfo);
        await ReadExpectAsync(stream, MsgType.ItemList);
        await ReadExpectAsync(stream, MsgType.PlayerSpawned);
        for (int i = 0; i < TalkingIsland.NpcCount; i++)
            await ReadExpectAsync(stream, MsgType.NpcInfo);

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

    static async Task ReadExpectAsync(NetworkStream stream, MsgType type)
    {
        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)type, frame!.Value.Type);
    }
}
