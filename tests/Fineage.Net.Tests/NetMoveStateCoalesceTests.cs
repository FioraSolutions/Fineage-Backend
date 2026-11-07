using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net.Sockets;
using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Protocol.Telemetry;
using Xunit;

namespace Fineage.Net.Tests;

public sealed class NetMoveStateCoalesceTests
{
    [Fact]
    public async Task TrySend_keeps_latest_move_state_when_outbound_is_full()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var dropped = new ConcurrentQueue<long>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == FineageTelemetry.MeterName && instrument.Name == "fineage.outbound.dropped")
                    l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => dropped.Enqueue(value));
        listener.Start();

        var server = new TcpListener(System.Net.IPAddress.Loopback, 0);
        server.Start();
        int port = ((System.Net.IPEndPoint)server.LocalEndpoint).Port;

        var accept = server.AcceptTcpClientAsync(cts.Token);
        using var client = new TcpClient();
        client.NoDelay = true;
        await client.ConnectAsync("127.0.0.1", port, cts.Token);
        using var serverSide = await accept;

        await using var conn = new NetConnection(serverSide, inboxCapacity: 8, endpoint: "move-coalesce", outboundCapacity: 4);

        byte[] filler = new byte[256];
        for (int i = 0; i < 8; i++)
            Assert.True(conn.TrySend(NetOpcode.SystemMessage, filler));

        // Snapshot after fillers: fineage.outbound.dropped is process-global and other
        // Net.* tests may emit drops in parallel on CI.
        int dropsBeforeMoves = dropped.Count;

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

        Assert.Equal(32, accepted);
        Assert.Equal(dropsBeforeMoves, dropped.Count);

        await Task.Delay(200, cts.Token);

        var stream = client.GetStream();
        MoveStateMsg? lastMove = null;
        int moveFrames = 0;
        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        readCts.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            while (moveFrames < 16)
            {
                PacketEnvelope? frame = await NetFraming.ReadAsync(stream, readCts.Token);
                if (frame == null)
                    break;
                if (frame.Opcode == NetOpcode.MoveState)
                {
                    moveFrames++;
                    lastMove = NetCodec.DecodeMoveState(frame.Payload);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }

        Assert.NotNull(lastMove);
        Assert.Equal(entity, lastMove.EntityValue);
        Assert.Equal(1031, lastMove.DestX);

        server.Stop();
    }
}
