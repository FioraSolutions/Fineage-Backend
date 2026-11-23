using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Protocol.Telemetry;
using Xunit;
using Xunit.Abstractions;

namespace Fineage.Net.Tests;

/// <summary>
/// Debug measurements for issue #867 PR3. A sealed-batch delivery scope must put one socket write
/// behind a burst of frames, and a full per-frame batch cap must not drop reliable frames.
/// </summary>
public sealed class ConnectionBatchWriteProbe
{
    readonly ITestOutputHelper _output;

    public ConnectionBatchWriteProbe(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Sealed_batch_scope_writes_the_burst_once()
    {
        const int frames = 24;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var (writes, delivered, dropped) = await MeasureAsync(frames, outboundCapacity: 64, scope: true, cts.Token);
        _output.WriteLine($"scope socket_writes={writes} frames_delivered={delivered} dropped={dropped}");
        Assert.Equal(0, dropped);
        Assert.Equal(frames, delivered);
        Assert.True(writes * 3 <= delivered, $"expected >=3x fewer socket writes than frames, writes={writes} frames={delivered}");
    }

    [Fact]
    public async Task Direct_enqueue_does_not_drop_reliable_frames_when_the_batch_bound_is_small()
    {
        const int frames = 40;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var (writes, delivered, dropped) = await MeasureAsync(frames, outboundCapacity: 8, scope: false, cts.Token);
        _output.WriteLine($"direct socket_writes={writes} frames_delivered={delivered} dropped={dropped}");
        Assert.Equal(0, dropped);
        Assert.Equal(frames, delivered);
    }

    static async Task<(int Writes, int Delivered, int Dropped)> MeasureAsync(
        int frames,
        int outboundCapacity,
        bool scope,
        CancellationToken ct)
    {
        int writes = 0;
        int dropped = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name != FineageTelemetry.MeterName)
                return;
            if (instrument.Name is "fineage.socket_write.frames" or "fineage.outbound.dropped" or "fineage.fanout.dropped")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<int>((instrument, _, tags, _) =>
        {
            if (instrument.Name == "fineage.socket_write.frames" && IsProbe(tags))
                Interlocked.Increment(ref writes);
        });
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            if (instrument.Name == "fineage.outbound.dropped" && IsProbe(tags))
                Interlocked.Add(ref dropped, (int)value);
        });
        listener.Start();

        var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        var accept = tcp.AcceptTcpClientAsync(ct).AsTask();
        var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync((IPEndPoint)tcp.LocalEndpoint!, ct);
        var serverSide = await accept;
        tcp.Stop();

        var conn = new NetConnection(serverSide, inboxCapacity: 64, endpoint: "batch-probe", outboundCapacity);
        conn.BindPresence(1, 1);
        var received = new List<PacketEnvelope>();
        var reader = Task.Run(async () =>
        {
            var stream = client.GetStream();
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    PacketEnvelope? frame = await NetFraming.ReadAsync(stream, ct);
                    if (frame == null)
                        break;
                    lock (received)
                        received.Add(frame);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, ct);

        await using var stripes = new NetFanoutStripes(stripeCount: 1, queueCapacity: 256);
        await using (conn)
        {
            var handle = new ConnectionHandle(1, 1, conn);
            if (scope)
            {
                using var delivery = FanoutDeliveryScope.Begin();
                for (int i = 0; i < frames; i++)
                {
                    Assert.True(FanoutDeliveryScope.TryBuffer(
                        stripes, handle, NetOpcode.AttackState, [(byte)i, 2, 3, 4]));
                }

                Assert.Equal(1, delivery.Flush());
            }
            else
            {
                for (int i = 0; i < frames; i++)
                    stripes.EnqueueTrySend(handle, NetOpcode.AttackState, [(byte)i, 2, 3, 4]);
            }

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
            while (DateTime.UtcNow < deadline)
            {
                lock (received)
                {
                    if (received.Count >= frames)
                        break;
                }

                await Task.Delay(20, ct);
            }
        }

        try { await reader; } catch { /* peer closed */ }
        client.Dispose();
        listener.Dispose();
        int delivered;
        lock (received)
            delivered = received.Count;
        return (Volatile.Read(ref writes), delivered, Volatile.Read(ref dropped));
    }

    static bool IsProbe(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        foreach (KeyValuePair<string, object?> tag in tags)
        {
            if (tag.Key == "endpoint" && tag.Value is string endpoint && endpoint == "batch-probe")
                return true;
        }

        return false;
    }
}
