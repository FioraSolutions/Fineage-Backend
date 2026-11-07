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
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var server = new TcpListener(System.Net.IPAddress.Loopback, 0);
        server.Start();
        int port = ((System.Net.IPEndPoint)server.LocalEndpoint).Port;

        var accept = server.AcceptTcpClientAsync(cts.Token);
        using var client = new TcpClient();
        client.NoDelay = true;
        await client.ConnectAsync("127.0.0.1", port, cts.Token);
        using var serverSide = await accept;

        await using var conn = new NetConnection(serverSide, inboxCapacity: 8, endpoint: "move-coalesce", outboundCapacity: 4);

        // Fill the outbound queue with non-MoveState frames so further MoveState sends
        // must coalesce rather than enqueue. The pump may still drain a slot between the
        // filler loop and the move burst, so an early DestX=1000 on the wire is allowed —
        // the contract under test is that the *latest* coalesced DestX still arrives.
        byte[] filler = new byte[256];
        for (int i = 0; i < 8; i++)
            Assert.True(conn.TrySend(NetOpcode.SystemMessage, filler));

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

        await Task.Delay(300, cts.Token);

        var stream = client.GetStream();
        var destXs = new List<int>();
        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        readCts.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            while (true)
            {
                PacketEnvelope? frame = await NetFraming.ReadAsync(stream, readCts.Token);
                if (frame == null)
                    break;
                if (frame.Opcode == NetOpcode.MoveState)
                {
                    MoveStateMsg move = NetCodec.DecodeMoveState(frame.Payload);
                    Assert.Equal(entity, move.EntityValue);
                    destXs.Add(move.DestX);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }

        Assert.NotEmpty(destXs);
        Assert.Equal(1031, destXs.Max());

        server.Stop();
    }
}
