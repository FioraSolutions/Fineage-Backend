using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Protocol.Telemetry;
using Xunit;

namespace Fineage.Net.Tests;

/// <summary>
/// Issue #867 PR4: one mailbox per connection. Replaceable frames collapse until a lifecycle barrier,
/// a coalesced-only stream still reaches the socket, and a stale generation is not written.
/// </summary>
public sealed class NetOutboundMailboxTests
{
    [Fact]
    public async Task Move_then_despawn_then_spawn_keeps_lifecycle_order_and_collapses_moves()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        int writes = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == FineageTelemetry.MeterName && instrument.Name == "fineage.socket_write.frames")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<int>((instrument, _, tags, _) =>
        {
            if (instrument.Name == "fineage.socket_write.frames" && Tag(tags, "endpoint") == "mailbox-order")
                Interlocked.Increment(ref writes);
        });
        listener.Start();

        var (client, conn, frames) = await WirePairAsync(cts.Token, "mailbox-order");
        using (client)
        await using (conn)
        {
            var batch = new List<OutboundFrame>();
            for (int i = 1; i <= 16; i++)
                batch.Add(new OutboundFrame(NetOpcode.MoveState, Move(i)));
            batch.Add(new OutboundFrame(NetOpcode.EntityDespawn, new byte[] { 9 }));
            batch.Add(new OutboundFrame(NetOpcode.EntitySpawn, new byte[] { 8 }));
            batch.Add(new OutboundFrame(NetOpcode.MoveState, Move(17)));
            conn.SubmitOrderedBatch(batch);
            await WaitForFrameCountAsync(frames, 4, cts.Token);
        }

        Assert.Equal(1, writes);

        lock (frames)
        {
            Assert.Equal(4, frames.Count);
            Assert.Equal(NetOpcode.MoveState, frames[0].Opcode);
            Assert.Equal(16, NetCodec.DecodeMoveState(frames[0].Payload).DestX);
            Assert.Equal(NetOpcode.EntityDespawn, frames[1].Opcode);
            Assert.Equal(NetOpcode.EntitySpawn, frames[2].Opcode);
            Assert.Equal(NetOpcode.MoveState, frames[3].Opcode);
            Assert.Equal(17, NetCodec.DecodeMoveState(frames[3].Payload).DestX);
        }
    }

    [Fact]
    public async Task Coalesced_only_move_stream_does_not_stall()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (client, conn, frames) = await WirePairAsync(cts.Token);
        using (client)
        await using (conn)
        {
            for (int i = 0; i < 20; i++)
                Assert.True(conn.TrySend(NetOpcode.MoveState, Move(1000 + i)));
            await WaitForMoveAsync(frames, 1019, cts.Token);
        }
    }

    [Fact]
    public async Task Stale_generation_is_not_written_after_rebind()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (client, conn, frames) = await WirePairAsync(cts.Token);
        await using var stripes = new NetFanoutStripes(stripeCount: 1, queueCapacity: 32);
        using (client)
        await using (conn)
        {
            conn.BindPresence(7, 1);
            var stale = new ConnectionHandle(7, 1, conn);
            stripes.EnqueueTrySend(stale, NetOpcode.MoveState, Move(4));
            await WaitForMoveAsync(frames, 4, cts.Token);

            conn.UnbindPresence(7, 1);
            stripes.EnqueueCritical(stale, NetOpcode.EntityDespawn, new byte[] { 1 });
            conn.BindPresence(7, 2);
            var live = new ConnectionHandle(7, 2, conn);
            stripes.EnqueueCritical(live, NetOpcode.EntitySpawn, new byte[] { 2 });
            await WaitForOpcodeAsync(frames, NetOpcode.EntitySpawn, cts.Token);
            await Task.Delay(50, cts.Token);
        }

        lock (frames)
        {
            Assert.DoesNotContain(frames, frame => frame.Opcode == NetOpcode.EntityDespawn);
            Assert.Contains(frames, frame => frame.Opcode == NetOpcode.EntitySpawn);
        }
    }

    [Fact]
    public async Task Byte_bound_waits_instead_of_dropping()
    {
        const int payloadBytes = 40_000;
        const int framesToSend = 3;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        int writes = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == FineageTelemetry.MeterName && instrument.Name == "fineage.socket_write.frames")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<int>((instrument, _, tags, _) =>
        {
            if (instrument.Name == "fineage.socket_write.frames" && Tag(tags, "endpoint") == "mailbox-bytes")
                Interlocked.Increment(ref writes);
        });
        listener.Start();

        var (client, conn, frames) = await WirePairAsync(cts.Token, "mailbox-bytes");
        using (client)
        await using (conn)
        {
            var batch = new List<OutboundFrame>(framesToSend);
            for (int i = 0; i < framesToSend; i++)
                batch.Add(new OutboundFrame(NetOpcode.SystemMessage, new byte[payloadBytes]));
            conn.SubmitOrderedBatch(batch);
            await WaitForFrameCountAsync(frames, framesToSend, cts.Token);
        }

        Assert.Equal(framesToSend, frames.Count);
        Assert.True(writes >= 2, $"a {payloadBytes}-byte frame does not share a 64KiB write, writes={writes}");
    }

    static byte[] Move(int destX) => NetCodec.EncodeMoveState(new MoveStateMsg
    {
        EntityValue = 0xBEEF,
        EntityGeneration = 1,
        DestX = destX,
        DestY = 2,
        DestZ = 3
    });

    static async Task<(TcpClient Client, NetConnection Server, List<PacketEnvelope> Frames)> WirePairAsync(
        CancellationToken ct,
        string endpoint = "mailbox")
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var accept = listener.AcceptTcpClientAsync(ct).AsTask();
        var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint!, ct);
        var serverSide = await accept;
        listener.Stop();
        var conn = new NetConnection(serverSide, inboxCapacity: 8, endpoint, outboundCapacity: 8);
        conn.BindPresence(1, 1);
        var frames = new List<PacketEnvelope>();
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
        return (client, conn, frames);
    }

    static async Task WaitForFrameCountAsync(List<PacketEnvelope> frames, int count, CancellationToken ct)
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

    static async Task WaitForMoveAsync(List<PacketEnvelope> frames, int destX, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            lock (frames)
            {
                foreach (PacketEnvelope frame in frames)
                {
                    if (frame.Opcode == NetOpcode.MoveState && NetCodec.DecodeMoveState(frame.Payload).DestX == destX)
                        return;
                }
            }

            await Task.Delay(10, ct);
        }

        Assert.Fail($"move destX={destX} did not arrive");
    }

    static async Task WaitForOpcodeAsync(List<PacketEnvelope> frames, ushort opcode, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            lock (frames)
            {
                if (frames.Exists(frame => frame.Opcode == opcode))
                    return;
            }

            await Task.Delay(10, ct);
        }

        Assert.Fail($"opcode {opcode} did not arrive");
    }

    static string? Tag(ReadOnlySpan<KeyValuePair<string, object?>> tags, string key)
    {
        foreach (KeyValuePair<string, object?> tag in tags)
        {
            if (tag.Key == key)
                return tag.Value?.ToString();
        }

        return null;
    }
}
