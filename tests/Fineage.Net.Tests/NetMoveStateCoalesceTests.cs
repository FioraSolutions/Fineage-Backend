using System.Net.Sockets;
using Fineage.Net.Contract;
using Fineage.Net.Host;
using Xunit;

namespace Fineage.Net.Tests;

public sealed class NetMoveStateCoalesceTests
{
    [Fact]
    public async Task TrySend_keeps_latest_move_state_when_outbound_is_full()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(180));

        var server = new TcpListener(System.Net.IPAddress.Loopback, 0);
        server.Start();
        int port = ((System.Net.IPEndPoint)server.LocalEndpoint).Port;

        var accept = server.AcceptTcpClientAsync(cts.Token);
        using var client = new TcpClient();
        client.NoDelay = true;
        await client.ConnectAsync("127.0.0.1", port, cts.Token);
        using var serverSide = await accept;

        // Capacity is floored at 8 inside NetConnection; saturate it so MoveState must coalesce.
        await using var conn = new NetConnection(serverSide, inboxCapacity: 8, endpoint: "move-coalesce", outboundCapacity: 8);

        var stream = client.GetStream();
        var destXs = new List<int>();
        var latestSeen = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Drain the wire concurrently so the outbound pump can flush coalesced MoveState after fillers.
        Task reader = Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    PacketEnvelope? frame = await NetFraming.ReadAsync(stream, cts.Token);
                    if (frame == null)
                        break;
                    if (frame.Opcode != NetOpcode.MoveState)
                        continue;
                    MoveStateMsg move = NetCodec.DecodeMoveState(frame.Payload);
                    Assert.Equal(0xBEEFul, move.EntityValue);
                    lock (destXs)
                        destXs.Add(move.DestX);
                    if (move.DestX >= 1031)
                        latestSeen.TrySetResult(move.DestX);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, cts.Token);

        byte[] filler = new byte[256];
        int filled = 0;
        while (conn.TrySend(NetOpcode.SystemMessage, filler) && filled < 64)
            filled++;
        Assert.True(filled >= 8, $"expected saturated outbound, filled={filled}");

        const ulong entity = 0xBEEF;
        const uint gen = 1;
        int accepted = 0;
        for (int i = 0; i < 32; i++)
        {
            byte[] move = NetCodec.EncodeMoveState(new MoveStateMsg
            {
                EntityValue = entity,
                EntityGeneration = gen,
                DestX = 1000 + i,
                DestY = 2000,
                DestZ = -3000
            });
            if (conn.TrySend(NetOpcode.MoveState, move))
                accepted++;
        }

        // Without coalesce, TrySend would start returning false once the queue is full.
        Assert.Equal(32, accepted);

        // Early DestX=1000 on the wire is fine; the contract is that the latest coalesced value arrives.
        // Pump writes stashed MoveState straight to the socket after each filler — no poke storm needed
        // (poking SystemMessage used to re-fill the channel and starve the stash on loaded CI hosts).
        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        waitCts.CancelAfter(TimeSpan.FromSeconds(30));
        int latest = await latestSeen.Task.WaitAsync(waitCts.Token);
        Assert.Equal(1031, latest);

        cts.Cancel();
        try { await reader; } catch { /* cancelled */ }

        lock (destXs)
        {
            Assert.NotEmpty(destXs);
            Assert.Equal(1031, destXs.Max());
        }

        server.Stop();
    }
}
