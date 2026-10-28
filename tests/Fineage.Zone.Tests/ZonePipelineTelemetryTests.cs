using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Protocol.Net;
using Fineage.Protocol.Telemetry;
using Fineage.Zone.World;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

[CollectionDefinition("Zone pipeline OTEL telemetry", DisableParallelization = true)]
public sealed class ZonePipelineOtelTelemetryCollection;

[Collection("Zone pipeline OTEL telemetry")]
public sealed class ZonePipelineTelemetryTests
{
    [Fact]
    public async Task Pipeline_hello_increments_frames_in()
    {
        long framesIn = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == FineageTelemetry.MeterName && instrument.Name == "fineage.frames.in")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, tags, _) =>
        {
            foreach (KeyValuePair<string, object?> tag in tags)
            {
                if (tag.Key == "endpoint" && tag.Value?.ToString() == "zone")
                    Interlocked.Add(ref framesIn, measurement);
            }
        });
        listener.Start();

        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var server = new ZoneServer(tokens, new ZoneWorld());
        await using var host = await PipelineZoneHost.StartAsync(server);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, host.Port);
        await using var stream = client.GetStream();
        await FrameIO.SendAsync(stream, (ushort)MsgType.Hello, MessagePackSerializer.Serialize(new HelloMsg()));

        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)MsgType.HelloAck, frame!.Value.Type);
        Assert.True(framesIn >= 1, $"expected fineage.frames.in for zone, got {framesIn}");
    }

    [Fact]
    public async Task Pipeline_hello_ack_increments_frames_out()
    {
        long framesOut = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == FineageTelemetry.MeterName && instrument.Name == "fineage.frames.out")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, tags, _) =>
        {
            foreach (KeyValuePair<string, object?> tag in tags)
            {
                if (tag.Key == "endpoint" && tag.Value?.ToString() == "zone-test")
                    Interlocked.Add(ref framesOut, measurement);
            }
        });
        listener.Start();

        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var server = new ZoneServer(tokens, new ZoneWorld());
        await using var host = await PipelineZoneHost.StartAsync(server);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, host.Port);
        await using var stream = client.GetStream();
        await FrameIO.SendAsync(stream, (ushort)MsgType.Hello, MessagePackSerializer.Serialize(new HelloMsg()));

        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)MsgType.HelloAck, frame!.Value.Type);
        Assert.True(framesOut >= 1, $"expected fineage.frames.out for zone-test, got {framesOut}");
    }

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
                if (tag.Key == "endpoint" && tag.Value?.ToString() == "zone-test")
                    Interlocked.Add(ref connections, measurement);
            }
        });
        meterListener.Start();

        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var server = new ZoneServer(tokens, new ZoneWorld());
        await using var host = await PipelineZoneHost.StartAsync(server);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, host.Port);
        await using var stream = client.GetStream();
        await FrameIO.SendAsync(stream, (ushort)MsgType.Hello, MessagePackSerializer.Serialize(new HelloMsg()));
        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)MsgType.HelloAck, frame!.Value.Type);

        for (int i = 0; i < 100 && connections < 1; i++)
            await Task.Delay(20);

        Assert.True(connections >= 1, $"expected fineage.connections +1 for zone-test, got {connections}");
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
