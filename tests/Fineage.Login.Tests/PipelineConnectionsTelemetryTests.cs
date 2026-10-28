using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Protocol.Net;
using Fineage.Protocol.Telemetry;
using MessagePack;
using Xunit;
using ProtocolMsgType = Fineage.Protocol.MsgType;

namespace Fineage.Login.Tests;

public sealed class PipelineConnectionsTelemetryTests
{
    [Fact]
    public async Task Pipeline_tcp_session_updates_connections_gauge()
    {
        long connections = 0;
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == FineageTelemetry.MeterName && instrument.Name == "fineage.connections")
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((_, measurement, tags, _) =>
        {
            foreach (KeyValuePair<string, object?> tag in tags)
            {
                if (tag.Key == "endpoint" && tag.Value?.ToString() == "login-test")
                    Interlocked.Add(ref connections, measurement);
            }
        });
        meterListener.Start();

        var service = new LoginService(JsonAccountStore.InMemory(), HandoffTokens.FromEnvironment());
        var server = new LoginServer(service);
        await using var host = await PipelineLoginHost.StartAsync(server);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, host.Port);
        await using var stream = client.GetStream();
        await FrameIO.SendAsync(stream, (ushort)ProtocolMsgType.Hello,
            MessagePackSerializer.Serialize(new HelloMsg()));
        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)ProtocolMsgType.HelloAck, frame!.Value.Type);
        Assert.True(connections >= 1, $"expected fineage.connections +1 for login-test, got {connections}");

        client.Close();
        for (int i = 0; i < 50 && connections != 0; i++)
            await Task.Delay(20);

        Assert.Equal(0, connections);
    }

    sealed class PipelineLoginHost : IAsyncDisposable
    {
        readonly CancellationTokenSource _cts;
        readonly Task _run;

        PipelineLoginHost(int port, CancellationTokenSource cts, Task run)
        {
            Port = port;
            _cts = cts;
            _run = run;
        }

        public int Port { get; }

        public static async Task<PipelineLoginHost> StartAsync(LoginServer server)
        {
            var cts = new CancellationTokenSource();
            var tcp = new TcpServer();
            var run = Task.Run(() => tcp.RunAsync("127.0.0.1", 0, 4, 8, "login-test", server.HandleAsync, cts.Token));

            for (int i = 0; i < 100; i++)
            {
                if (tcp.BoundPort != 0)
                    return new PipelineLoginHost(tcp.BoundPort, cts, run);

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
