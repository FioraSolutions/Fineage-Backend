using System.Net;
using System.Net.Sockets;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Protocol.Net;
using Fineage.Zone.World;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

[CollectionDefinition("Zone tcp idle environment", DisableParallelization = true)]
public sealed class ZoneTcpIdleEnvironmentCollection;

[Collection("Zone tcp idle environment")]
public sealed class ZoneIdleTimeoutTests
{
    [Fact]
    public async Task Pipeline_idle_timeout_returns_error()
    {
        string? priorIdle = Environment.GetEnvironmentVariable("FINEAGE_TCP_IDLE_SECONDS");
        try
        {
            Environment.SetEnvironmentVariable("FINEAGE_TCP_IDLE_SECONDS", "2");
            var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
            var server = new ZoneServer(tokens, new ZoneWorld());
            await using var host = await PipelineZoneHost.StartAsync(server);

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, host.Port);
            await using var stream = client.GetStream();
            await FrameIO.SendAsync(stream, (ushort)MsgType.Hello, MessagePackSerializer.Serialize(new HelloMsg()));
            var ack = await FrameIO.ReadAsync(stream);
            Assert.NotNull(ack);
            Assert.Equal((ushort)MsgType.HelloAck, ack!.Value.Type);

            await Task.Delay(TimeSpan.FromSeconds(2.5));

            var frame = await FrameIO.ReadAsync(stream);
            Assert.NotNull(frame);
            Assert.Equal((ushort)MsgType.Error, frame!.Value.Type);
            var err = MessagePackSerializer.Deserialize<ErrorMsg>(frame.Value.Body);
            Assert.Equal("idle_timeout", err.Code);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FINEAGE_TCP_IDLE_SECONDS", priorIdle);
        }
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
