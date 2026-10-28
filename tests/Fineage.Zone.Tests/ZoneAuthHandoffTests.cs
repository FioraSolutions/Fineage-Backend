using System.Net;
using System.Net.Sockets;
using Fineage.Login;
using Fineage.Protocol;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZoneAuthHandoffTests
{
    [Fact]
    public async Task Second_auth_on_same_session_returns_already_authenticated()
    {
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var server = new ZoneServer(tokens);
        var login = new LoginService(JsonAccountStore.InMemory(), tokens);
        login.Register("hero", "pass1234");
        var first = login.Play("hero", "pass1234", "HeroOne", HumanClasses.Fighter);
        var second = login.Play("hero", "pass1234", "HeroTwo", HumanClasses.Mystic);
        Assert.True(first.Ok && first.Value != null);
        Assert.True(second.Ok && second.Value != null);

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
            MessagePackSerializer.Serialize(first.Value!.ToZoneAuth()));
        await ReadAsync(stream, MsgType.AuthOk);

        await FrameIO.SendAsync(stream, (ushort)MsgType.Auth,
            MessagePackSerializer.Serialize(second.Value!.ToZoneAuth()));
        var errFrame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(errFrame);
        Assert.Equal((ushort)MsgType.Error, errFrame!.Value.Type);
        var err = MessagePackSerializer.Deserialize<ErrorMsg>(errFrame.Value.Body);
        Assert.Equal("already_authenticated", err.Code);

        await FrameIO.SendAsync(stream, (ushort)MsgType.EnterWorld, MessagePackSerializer.Serialize(new EnterWorldMsg()));
        await ReadAsync(stream, MsgType.PlayerSpawned);

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

    [Fact]
    public async Task Auth_with_empty_account_returns_bad_request()
    {
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var server = new ZoneServer(tokens);
        var login = new LoginService(JsonAccountStore.InMemory(), tokens);
        login.Register("hero", "pass1234");
        var ticket = login.Play("hero", "pass1234", "HeroOne", HumanClasses.Fighter);
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
            MessagePackSerializer.Serialize(new AuthMsg { Account = "   ", Password = ticket.Value!.Token }));

        var errFrame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(errFrame);
        Assert.Equal((ushort)MsgType.Error, errFrame!.Value.Type);
        var err = MessagePackSerializer.Deserialize<ErrorMsg>(errFrame.Value.Body);
        Assert.Equal("bad_request", err.Code);

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

    static async Task ReadAsync(NetworkStream stream, MsgType expected)
    {
        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)expected, frame!.Value.Type);
    }
}
