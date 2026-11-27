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
        var (slow, healthy) = await DifferentStripesAsync(cts.Token);
        using var _slowClient = slow.Client;
        using var _healthyClient = healthy.Client;
        await using var _slow = slow.Connection;
        await using var _healthy = healthy.Connection;

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
        var (slow, healthy) = await DifferentStripesAsync(cts.Token);
        using var _slowClient = slow.Client;
        using var _healthyClient = healthy.Client;
        await using var _slow = slow.Connection;
        await using var _healthy = healthy.Connection;

        slow.Connection.BindPresence(3, 1);
        healthy.Connection.BindPresence(4, 1);
        var slowHandle = new ConnectionHandle(3, 1, slow.Connection);
        var healthyHandle = new ConnectionHandle(4, 1, healthy.Connection);
        byte[] heavy = new byte[40_000];
        using var metrics = new MetricsCollector();
        long disconnectsBefore = metrics.Snapshot().OutboundSlowClientDisconnect;

        var elapsed = Stopwatch.StartNew();
        // 40 x 40 KB is past the 1 MiB hard limit even if the stripe worker keeps up with the producer.
        for (int n = 0; n < 40; n++)
            stripes.EnqueueCritical(slowHandle, NetOpcode.SystemMessage, heavy);
        stripes.EnqueueTrySend(healthyHandle, NetOpcode.SystemMessage, [7]);
        elapsed.Stop();

        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2),
            $"reliable overflow blocked the caller for {elapsed.Elapsed.TotalMilliseconds:0} ms");
        await WaitForCountAsync(healthy.Frames, 1, cts.Token);

        // The pump may already have pushed the first write into the kernel when overflow closes
        // the socket. One Read returning those bytes is not "still open": keep reading until EOF
        // or the reset. Starting this read earlier would drain the window and hide the overflow.
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(async () =>
        {
            try
            {
                var buffer = new byte[8192];
                while (true)
                {
                    int n = await slow.Client.GetStream().ReadAsync(buffer, cts.Token);
                    if (n == 0)
                    {
                        closed.TrySetResult();
                        return;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
            {
                closed.TrySetResult();
            }
        }, cts.Token);

        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5), cts.Token);
        // #992: the slow-client alert reads this counter; the overflow close itself must move it.
        Assert.True(metrics.Snapshot().OutboundSlowClientDisconnect - disconnectsBefore >= 1);
    }

    [Fact]
    public void Second_reliable_batch_rejects_while_the_first_is_queued_or_in_flight()
    {
        // Soft == hard: the second 40 KB reliable frame has nowhere to go while the first is queued or in flight.
        var mailbox = new OutboundMailbox(maxFrames: 4096, maxBytes: 64 * 1024, connectionId: 1, endpoint: "inflight", hardBytes: 64 * 1024);
        Assert.Equal(MailboxAdmit.Accepted, mailbox.TryAdmitBatch([Heavy()], forceWholeBatch: false));
        Assert.Equal(MailboxAdmit.Rejected, mailbox.TryAdmitBatch([Heavy()], forceWholeBatch: false));

        var drained = new List<PacketEnvelope>();
        MailboxTaken taken = mailbox.Take(drained);
        Assert.Equal(1, taken.Frames);
        Assert.Equal(MailboxAdmit.Rejected, mailbox.TryAdmitBatch([Heavy()], forceWholeBatch: false));

        mailbox.ReleaseInflight(taken.Frames, taken.Bytes);
        Assert.Equal(MailboxAdmit.Accepted, mailbox.TryAdmitBatch([Heavy()], forceWholeBatch: false));
    }

    [Fact]
    public void One_caller_batch_over_the_byte_cap_is_kept_when_nothing_is_held()
    {
        var mailbox = new OutboundMailbox(maxFrames: 4096, maxBytes: 64 * 1024, connectionId: 2, endpoint: "one-burst", hardBytes: 64 * 1024);
        var batch = new[] { Heavy(), Heavy(), Heavy() };
        Assert.Equal(MailboxAdmit.Accepted, mailbox.TryAdmitBatch(batch, forceWholeBatch: false));
        var drained = new List<PacketEnvelope>();
        Assert.Equal(3, mailbox.Take(drained).Frames);
    }

    [Fact]
    public void Movement_coalesce_counts_the_movement_lane()
    {
        // Drive OutboundMailbox directly: NetConnection's pump can drain the first MoveState before the
        // second arrives, so a TCP TrySend pair races and flakes when the coalesce metric stays at 0.
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

        var mailbox = new OutboundMailbox(
            maxFrames: 64, maxBytes: 64 * 1024, connectionId: 3, endpoint: "lane-coalesce", hardBytes: 64 * 1024);
        Assert.Equal(MailboxAdmit.Accepted, mailbox.TryAppend(MoveEnvelope(1)));
        Assert.Equal(MailboxAdmit.Accepted, mailbox.TryAppend(MoveEnvelope(2)));

        Assert.True(Volatile.Read(ref count[0]) >= start + 1);
    }

    static PacketEnvelope MoveEnvelope(int destX) => new()
    {
        Opcode = NetOpcode.MoveState,
        Flags = (ushort)PacketFlags.Reliable,
        Payload = Move(destX)
    };

    static PacketEnvelope Heavy() => new()
    {
        Opcode = NetOpcode.SystemMessage,
        Flags = (ushort)PacketFlags.Reliable,
        Payload = new byte[40_000]
    };

    static byte[] Move(int destX) => NetCodec.EncodeMoveState(new MoveStateMsg
    {
        EntityValue = 0xBEEF,
        EntityGeneration = 1,
        DestX = destX,
        DestY = 2,
        DestZ = 3
    });

    /// <summary>
    /// Connection ids are process-wide. Another test can take an id between these two constructors,
    /// which puts both sockets on the same stripe. Keep opening the healthy side until it lands
    /// on the other stripe. A shared stripe is a different failure (the healthy frame is rejected
    /// because the queue is full), not the slow-client overflow this test locks.
    /// </summary>
    static async Task<(Pair Slow, Pair Healthy)> DifferentStripesAsync(CancellationToken ct)
    {
        var slow = await PairAsync(ct, read: false);
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var healthy = await PairAsync(ct, read: true);
            if (slow.Connection.ConnectionId % 2 != healthy.Connection.ConnectionId % 2)
                return (slow, healthy);
            healthy.Client.Dispose();
            await healthy.Connection.DisposeAsync();
        }

        throw new InvalidOperationException("could not place the two clients on different fan-out stripes");
    }

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
