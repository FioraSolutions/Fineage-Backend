using System.Net;
using System.Net.Sockets;
using Fineage.Login;
using Fineage.Protocol;
using MessagePack;
using Xunit;

namespace Fineage.Login.Tests;

public sealed class LoginPingTests
{
    [Fact]
    public async Task LoginServer_replies_to_ping_over_tcp()
    {
        var tokens = HandoffTokens.FromEnvironment();
        var service = new LoginService(JsonAccountStore.InMemory(), tokens);
        var server = new LoginServer(service, null, () => DateTimeOffset.FromUnixTimeMilliseconds(1000));

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
        await FrameIO.SendAsync(stream, (ushort)MsgType.Ping, MessagePackSerializer.Serialize(new PingMsg { ClientUnixMs = 42 }));
        var frame = await FrameIO.ReadAsync(stream);
        listener.Stop();

        Assert.NotNull(frame);
        Assert.Equal((ushort)MsgType.Pong, frame!.Value.Type);
        var pong = MessagePackSerializer.Deserialize<PongMsg>(frame.Value.Body);
        Assert.Equal(42, pong.ClientUnixMs);
        Assert.Equal(1000, pong.ServerUnixMs);

        await cts.CancelAsync();
        try
        {
            await acceptTask;
        }
        catch (OperationCanceledException)
        {
        }
    }
}
