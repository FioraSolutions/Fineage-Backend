using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net.Sockets;
using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Protocol.Telemetry;
using Xunit;

namespace Fineage.Net.Tests;

/// <summary>
/// V2's <c>PipelineConnection</c> dropped frames for a stalled client and counted
/// <c>fineage.outbound.dropped</c>; Net had no outbound queue at all, so that dashboard would have gone dark
/// the moment the V2 listener stopped. Guard the drop path and that it never blocks the caller.
/// </summary>
public sealed class NetOutboundQueueTests
{
    [Fact]
    public async Task TrySend_drops_and_counts_when_the_outbound_queue_is_full()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var dropped = new ConcurrentQueue<(long Value, string? Endpoint)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == FineageTelemetry.MeterName && instrument.Name == "fineage.outbound.dropped")
                    l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            string? endpoint = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "endpoint")
                    endpoint = tag.Value?.ToString();
            }

            dropped.Enqueue((value, endpoint));
        });
        listener.Start();

        var server = new NetTcpServer { Endpoint = "outbound-test" };
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = Task.Run(
            () => server.RunAsync("127.0.0.1", 0, async (conn, ct) =>
            {
                // Small queue on purpose: the client never reads, so the socket buffer fills and the pump
                // stops draining, leaving TrySend to hit the cap.
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }, cts.Token, outboundCapacity: 8),
            cts.Token);

        int port = 0;
        for (int i = 0; i < 200 && port == 0; i++)
        {
            await Task.Delay(10, cts.Token);
            port = server.BoundPort;
        }

        Assert.True(port > 0, "net server did not bind");

        // A raw socket that never reads: fanout frames back up in the connection's outbound channel.
        using var client = new TcpClient();
        client.ReceiveBufferSize = 4096;
        await client.ConnectAsync("127.0.0.1", port, cts.Token);
        await started.Task.WaitAsync(cts.Token);

        // Reach the connection through a second NetConnection-shaped probe: send a burst of large envelopes
        // from the server side and require TrySend to start refusing without waiting.
        int accepted = 0, refused = 0;
        await using var probe = new NetConnection(client, inboxCapacity: 8, endpoint: "outbound-test", outboundCapacity: 8);
        byte[] payload = new byte[PacketEnvelope.MaxPayloadBytes - 1024];
        for (int i = 0; i < 512; i++)
        {
            if (probe.TrySend(NetOpcode.SystemMessage, payload))
                accepted++;
            else
                refused++;
        }

        Assert.True(accepted > 0, "no frame fit the outbound queue");
        Assert.True(refused > 0, "TrySend never refused, so a stalled client could grow memory without limit");
        Assert.Contains(dropped, d => d.Endpoint == "outbound-test" && d.Value >= 1);

        await cts.CancelAsync();
        try
        {
            await host.WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
        }
    }

    [Fact]
    public async Task TrySend_never_blocks_the_caller_when_the_queue_is_saturated()
    {
        using var client = new TcpClient();
        var server = new TcpListener(System.Net.IPAddress.Loopback, 0);
        server.Start();
        int port = ((System.Net.IPEndPoint)server.LocalEndpoint).Port;
        var accept = server.AcceptTcpClientAsync();
        await client.ConnectAsync("127.0.0.1", port);
        using var _ = await accept;

        await using var conn = new NetConnection(client, inboxCapacity: 4, endpoint: "fanout-test", outboundCapacity: 4);
        byte[] payload = new byte[32 * 1024];

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 256; i++)
            conn.TrySend(NetOpcode.SystemMessage, payload);
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < 1000,
            $"TrySend blocked for {stopwatch.ElapsedMilliseconds} ms; fanout must not stall the tick");
        server.Stop();
    }
}
