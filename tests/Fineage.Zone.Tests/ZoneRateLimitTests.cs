using System.Net;
using System.Net.Sockets;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Protocol.Net;
using Fineage.Zone.World;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZoneRateLimitTests
{
    [Fact]
    public async Task Pipeline_session_closes_after_burst_over_limit()
    {
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var world = new ZoneWorld();
        var server = new ZoneServer(tokens, world, clock: null, new ZoneServerOptions { MaxMessagesPerSecond = 2 });

        await using var host = await PipelineZoneHost.StartAsync(server).ConfigureAwait(false);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, host.Port).ConfigureAwait(false);

        await using var stream = client.GetStream();
        for (int i = 0; i < 3; i++)
        {
            await FrameIO.SendAsync(stream, (ushort)MsgType.Hello, MessagePackSerializer.Serialize(new HelloMsg()), CancellationToken.None);
        }

        for (int i = 0; i < 2; i++)
        {
            var ack = await FrameIO.ReadAsync(stream, CancellationToken.None);
            Assert.NotNull(ack);
            Assert.Equal((ushort)MsgType.HelloAck, ack.Value.Type);
        }

        var frame = await FrameIO.ReadAsync(stream, CancellationToken.None);
        Assert.NotNull(frame);
        Assert.Equal((ushort)MsgType.Error, frame.Value.Type);
        var err = MessagePackSerializer.Deserialize<ErrorMsg>(frame.Value.Body);
        Assert.Equal("rate_limit", err.Code);

        Assert.Null(await FrameIO.ReadAsync(stream, CancellationToken.None).ConfigureAwait(false));
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

                await Task.Delay(20).ConfigureAwait(false);
            }

            await cts.CancelAsync().ConfigureAwait(false);
            throw new InvalidOperationException("TcpServer did not bind.");
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync().ConfigureAwait(false);
            try
            {
                await _run.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
            }

            _cts.Dispose();
        }
    }
}
