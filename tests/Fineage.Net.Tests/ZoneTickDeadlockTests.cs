using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Protocol.Telemetry;
using Xunit;

namespace Fineage.Net.Tests;

/// <summary>
/// The zone tick deadlocked when TickSpawns blocked in stripe WriteAsync().GetResult while stripe workers
/// sat in OutboundMailbox.Wait. A slow client plus a spawn burst must return, and a second client must
/// still receive frames.
/// </summary>
public sealed class ZoneTickDeadlockTests
{
    [Fact(Timeout = 15_000)]
    public async Task Slow_client_spawn_burst_returns_and_other_client_receives()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        // One worker per connection. A shared stripe of depth 2 would refuse the healthy client's
        // frame while the slow burst still sits in that queue; the deadlock is the slow worker, not that.
        await using var stripes = new NetFanoutStripes(stripeCount: 2, queueCapacity: 2);
        var slow = await PairAsync(cts.Token, read: false);
        var healthy = await PairAsync(cts.Token, read: true);
        using var _slowClient = slow.Client;
        using var _healthyClient = healthy.Client;
        await using var _slow = slow.Connection;
        await using var _healthy = healthy.Connection;
        Assert.NotEqual(
            slow.Connection.ConnectionId % 2,
            healthy.Connection.ConnectionId % 2);

        slow.Connection.BindPresence(1, 1);
        healthy.Connection.BindPresence(2, 1);
        var slowHandle = new ConnectionHandle(1, 1, slow.Connection);
        var healthyHandle = new ConnectionHandle(2, 1, healthy.Connection);

        stripes.EnqueueTrySend(healthyHandle, NetOpcode.SystemMessage, [9]);
        await WaitForCountAsync(healthy.Frames, 1, cts.Token);

        var burst = new List<OutboundFrame>(400);
        byte[] spawn = new byte[32];
        for (int i = 0; i < burst.Capacity; i++)
            burst.Add(new OutboundFrame(NetOpcode.EntitySpawn, spawn));

        var elapsed = Stopwatch.StartNew();
        for (int n = 0; n < 24; n++)
            stripes.EnqueueSnapshot(slowHandle, burst);
        stripes.EnqueueTrySend(healthyHandle, NetOpcode.SystemMessage, [8, 8]);
        elapsed.Stop();

        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2),
            $"spawn burst blocked the caller for {elapsed.Elapsed.TotalMilliseconds:0} ms");

        await WaitForCountAsync(healthy.Frames, 2, cts.Token);
        lock (healthy.Frames)
        {
            Assert.Equal(2, healthy.Frames.Count);
            Assert.All(healthy.Frames, frame => Assert.Equal(NetOpcode.SystemMessage, frame.Opcode));
        }
    }

    [Fact(Timeout = 15_000)]
    public async Task Reliable_overflow_closes_the_slow_client_without_blocking_the_other()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        await using var stripes = new NetFanoutStripes(stripeCount: 2, queueCapacity: 2);
        var slow = await PairAsync(cts.Token, read: false);
        var healthy = await PairAsync(cts.Token, read: true);
        using var _slowClient = slow.Client;
        using var _healthyClient = healthy.Client;
        await using var _slow = slow.Connection;
        await using var _healthy = healthy.Connection;
        Assert.NotEqual(
            slow.Connection.ConnectionId % 2,
            healthy.Connection.ConnectionId % 2);

        slow.Connection.BindPresence(3, 1);
        healthy.Connection.BindPresence(4, 1);
        var slowHandle = new ConnectionHandle(3, 1, slow.Connection);
        var healthyHandle = new ConnectionHandle(4, 1, healthy.Connection);
        byte[] heavy = new byte[40_000];

        var elapsed = Stopwatch.StartNew();
        for (int n = 0; n < 8; n++)
            stripes.EnqueueCritical(slowHandle, NetOpcode.SystemMessage, heavy);
        stripes.EnqueueTrySend(healthyHandle, NetOpcode.SystemMessage, [7]);
        elapsed.Stop();

        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2),
            $"reliable overflow blocked the caller for {elapsed.Elapsed.TotalMilliseconds:0} ms");
        await WaitForCountAsync(healthy.Frames, 1, cts.Token);

        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(async () =>
        {
            try
            {
                var buffer = new byte[1];
                int n = await slow.Client.GetStream().ReadAsync(buffer, cts.Token);
                if (n == 0)
                    closed.TrySetResult();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
            {
                closed.TrySetResult();
            }
        }, cts.Token);

        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5), cts.Token);
    }

    [Fact]
    public async Task Movement_coalesce_counts_the_movement_lane()
    {
        var count = new long[1];
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == FineageTelemetry.MeterName
                && instrument.Name == "fineage.outbound.lane.coalesced")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            foreach (KeyValuePair<string, object?> tag in tags)
            {
                if (tag.Key == "lane" && tag.Value is "movement")
                    Interlocked.Add(ref count[0], value);
            }
        });
        listener.Start();
        long start = Volatile.Read(ref count[0]);

        using var listenerTcp = new TcpListener(IPAddress.Loopback, 0);
        listenerTcp.Start();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listenerTcp.LocalEndpoint).Port);
        using var server = await listenerTcp.AcceptTcpClientAsync();
        listenerTcp.Stop();
        await using var conn = new NetConnection(server, inboxCapacity: 4, endpoint: "lane-coalesce", outboundCapacity: 8);
        Assert.True(conn.TrySend(NetOpcode.MoveState, Move(1)));
        Assert.True(conn.TrySend(NetOpcode.MoveState, Move(2)));

        Assert.True(Volatile.Read(ref count[0]) >= start + 1);
    }

    static byte[] Move(int destX) => NetCodec.EncodeMoveState(new MoveStateMsg
    {
        EntityValue = 0xBEEF,
        EntityGeneration = 1,
        DestX = destX,
        DestY = 2,
        DestZ = 3
    });

    static async Task<Pair> PairAsync(CancellationToken ct, bool read)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var accept = listener.AcceptTcpClientAsync(ct).AsTask();
        var client = new TcpClient { NoDelay = true, ReceiveBufferSize = read ? 64 * 1024 : 512 };
        await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint!, ct);
        var serverSide = await accept;
        listener.Stop();
        if (!read)
            serverSide.SendBufferSize = 512;
        var conn = new NetConnection(serverSide, inboxCapacity: 8, endpoint: read ? "deadlock-healthy" : "deadlock-slow", outboundCapacity: 8);
        var frames = new List<PacketEnvelope>();
        if (read)
        {
            _ = Task.Run(async () =>
            {
                var stream = client.GetStream();
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        PacketEnvelope? frame = await NetFraming.ReadAsync(stream, ct);
                        if (frame == null)
                            break;
                        lock (frames)
                            frames.Add(frame);
                    }
                }
                catch (OperationCanceledException)
                {
                }
            }, ct);
        }

        return new Pair(client, conn, frames);
    }

    static async Task WaitForCountAsync(List<PacketEnvelope> frames, int count, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            lock (frames)
            {
                if (frames.Count >= count)
                    return;
            }

            await Task.Delay(10, ct);
        }

        lock (frames)
            Assert.Fail($"expected at least {count} frames, saw {frames.Count}");
    }

    sealed record Pair(TcpClient Client, NetConnection Connection, List<PacketEnvelope> Frames);
}
