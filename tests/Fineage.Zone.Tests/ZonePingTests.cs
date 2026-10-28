using System.Net;
using System.Net.Sockets;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Protocol.Net;
using Fineage.Zone.World;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZonePingTests
{
    [Fact]
    public async Task ZoneServer_replies_to_ping_over_tcp()
    {
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var server = new ZoneServer(tokens, new ZoneWorld(), () => DateTimeOffset.FromUnixTimeMilliseconds(2000));

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
        await FrameIO.SendAsync(stream, (ushort)MsgType.Ping, MessagePackSerializer.Serialize(new PingMsg { ClientUnixMs = 55 }));
        var frame = await FrameIO.ReadAsync(stream);
        listener.Stop();

        Assert.NotNull(frame);
        Assert.Equal((ushort)MsgType.Pong, frame!.Value.Type);
        var pong = MessagePackSerializer.Deserialize<PongMsg>(frame.Value.Body);
        Assert.Equal(55, pong.ClientUnixMs);
        Assert.Equal(2000, pong.ServerUnixMs);

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
    public async Task ZoneServer_replies_to_ping_over_pipeline()
    {
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var server = new ZoneServer(tokens, new ZoneWorld(), () => DateTimeOffset.FromUnixTimeMilliseconds(3000));
        await using var host = await PipelineZoneHost.StartAsync(server);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, host.Port);
        await using var stream = client.GetStream();
        await FrameIO.SendAsync(stream, (ushort)MsgType.Ping, MessagePackSerializer.Serialize(new PingMsg { ClientUnixMs = 77 }));
        var frame = await FrameIO.ReadAsync(stream);

        Assert.NotNull(frame);
        Assert.Equal((ushort)MsgType.Pong, frame!.Value.Type);
        var pong = MessagePackSerializer.Deserialize<PongMsg>(frame.Value.Body);
        Assert.Equal(77, pong.ClientUnixMs);
        Assert.Equal(3000, pong.ServerUnixMs);
    }

    sealed class PipelineZoneHost : IAsyncDisposable
    {
        readonly CancellationTokenSource _cts;
        readonly Task _run;

        PipelineZoneHost(int port, CancellationTokenSource cts, Task run)
        {
            Port = port;
            _cts = cts;
            _run = run;
        }

        public int Port { get; }

        public static async Task<PipelineZoneHost> StartAsync(ZoneServer server)
        {
            var cts = new CancellationTokenSource();
            var tcp = new TcpServer();
            var run = Task.Run(() => tcp.RunAsync("127.0.0.1", 0, 4, 8, "zone-test", server.HandleAsync, cts.Token));

            for (int i = 0; i < 100; i++)
            {
                if (tcp.BoundPort != 0)
                    return new PipelineZoneHost(tcp.BoundPort, cts, run);

                await Task.Delay(20);
            }

            await cts.CancelAsync();
            throw new InvalidOperationException("TcpServer did not bind.");
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            try
            {
                await _run.WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch (TimeoutException)
            {
            }

            _cts.Dispose();
        }
    }
}
