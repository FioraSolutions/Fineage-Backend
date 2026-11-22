using System.Net;
using System.Net.Sockets;
using Fineage.Net.Contract;
using Fineage.Net.Host;
using Xunit;

namespace Fineage.Net.Tests;

/// <summary>
/// PR stripe batch must not hold non-replaceable combat/presence frames until an arbitrary batch size or foreign wake.
/// </summary>
public sealed class NetStripeBatchDeliveryTests
{
    static async Task<(TcpClient Client, NetConnection Server, List<PacketEnvelope> Frames)> WirePairAsync(
        CancellationToken ct,
        int outboundCapacity = 256)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var accept = listener.AcceptTcpClientAsync(ct).AsTask();
        var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint!, ct);
        var serverSide = await accept;
        listener.Stop();
        var conn = new NetConnection(serverSide, inboxCapacity: 64, endpoint: "stripe-batch", outboundCapacity);
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

    static byte[] SampleAttackState(int damage) => NetCodec.EncodeAttackState(new AttackStateMsg
    {
        AttackerValue = 0x100,
        AttackerGeneration = 1,
        TargetValue = 0x200,
        TargetGeneration = 1,
        Damage = damage,
        Flags = 1,
        AttackerX = 1,
        AttackerY = 2,
        AttackerZ = 3,
        TargetX = 4,
        TargetY = 5,
        TargetZ = 6
    });

    static byte[] SampleMagicSkillUse(int skillId) => NetCodec.EncodeMagicSkillUse(new MagicSkillUseNetMsg
    {
        CasterObjectId = 10,
        TargetObjectId = 20,
        SkillId = skillId,
        SkillLevel = 1,
        HitTimeMs = 0,
        ReuseDelayMs = 0
    });

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public async Task TrySend_non_replaceable_batch_reaches_wire_without_eighth_frame(int count)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (_, conn, frames) = await WirePairAsync(cts.Token);
        await using var _ = conn;

        for (int i = 0; i < count; i++)
            Assert.True(conn.TrySend(NetOpcode.AttackState, SampleAttackState(100 + i)));

        await WaitForFrameCountAsync(frames, count, cts.Token);

        lock (frames)
        {
            Assert.Equal(count, frames.Count);
            for (int i = 0; i < count; i++)
            {
                Assert.Equal(NetOpcode.AttackState, frames[i].Opcode);
                var atk = NetCodec.DecodeAttackState(frames[i].Payload);
                Assert.Equal(100 + i, atk.Damage);
            }
        }
    }

    [Fact]
    public async Task NetFanoutStripes_delivers_single_AttackState_without_other_traffic()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (_, conn, frames) = await WirePairAsync(cts.Token);
        await using var stripes = new NetFanoutStripes(stripeCount: 4, queueCapacity: 64);
        await using (conn)
        {
            var handle = new ConnectionHandle(1, 1, conn);
            stripes.EnqueueTrySend(handle, NetOpcode.AttackState, SampleAttackState(42));
            await WaitForFrameCountAsync(frames, 1, cts.Token);
        }

        lock (frames)
        {
            Assert.Single(frames);
            Assert.Equal(NetOpcode.AttackState, frames[0].Opcode);
            Assert.Equal(42, NetCodec.DecodeAttackState(frames[0].Payload).Damage);
        }
    }

    [Fact]
    public async Task NetFanoutStripes_delivers_MagicSkillUse_same_path_as_AttackState()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (_, conn, frames) = await WirePairAsync(cts.Token);
        await using var stripes = new NetFanoutStripes(stripeCount: 4, queueCapacity: 64);
        await using (conn)
        {
            var handle = new ConnectionHandle(2, 1, conn);
            stripes.EnqueueTrySend(handle, NetOpcode.MagicSkillUse, SampleMagicSkillUse(1234));
            await WaitForFrameCountAsync(frames, 1, cts.Token);
        }

        lock (frames)
        {
            Assert.Single(frames);
            Assert.Equal(NetOpcode.MagicSkillUse, frames[0].Opcode);
            Assert.Equal(1234, NetCodec.DecodeMagicSkillUse(frames[0].Payload).SkillId);
        }
    }

    [Fact]
    public async Task Mailbox_barrier_flushes_pending_stripe_batch_before_critical_frame()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (_, conn, frames) = await WirePairAsync(cts.Token);
        await using var stripes = new NetFanoutStripes(stripeCount: 1, queueCapacity: 64);
        await using (conn)
        {
            var handle = new ConnectionHandle(3, 1, conn);
            stripes.EnqueueTrySend(handle, NetOpcode.AttackState, SampleAttackState(7));
            stripes.EnqueueCritical(handle, NetOpcode.EntitySpawn, new byte[] { 1, 2, 3, 4 });
            await WaitForFrameCountAsync(frames, 2, cts.Token);
        }

        lock (frames)
        {
            Assert.Equal(2, frames.Count);
            Assert.Equal(NetOpcode.AttackState, frames[0].Opcode);
            Assert.Equal(NetOpcode.EntitySpawn, frames[1].Opcode);
        }
    }

    static async Task WaitForFrameCountAsync(
        List<PacketEnvelope> frames,
        int count,
        CancellationToken ct,
        TimeSpan? maxWait = null)
    {
        var deadline = DateTime.UtcNow + (maxWait ?? TimeSpan.FromMilliseconds(800));
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
            Assert.Fail($"expected at least {count} frames on the wire, saw {frames.Count}");
    }
}