using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net.Sockets;
using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Protocol.Telemetry;
using Xunit;

namespace Fineage.Net.Tests;

/// <summary>
/// The retired V2 pipeline was the only thing publishing frames/bytes/connections, so Net sessions were invisible
/// on the dashboards that read <c>fineage.frames.*</c>. Guard that a Net connection reports itself.
/// </summary>
public sealed class NetTelemetryTests
{
    [Fact]
    public async Task A_net_round_trip_reports_frames_connections_and_rejects()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var seen = new ConcurrentBag<(string Instrument, long Value, string? Endpoint)>();

        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == FineageTelemetry.MeterName)
                    l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            string? endpoint = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "endpoint")
                    endpoint = tag.Value?.ToString();
            }

            seen.Add((instrument.Name, value, endpoint));
        });
        listener.Start();

        var server = new NetTcpServer { Endpoint = "unittest" };
        var host = Task.Run(
            () => server.RunAsync("127.0.0.1", 0, async (conn, ct) =>
            {
                await foreach (var envelope in conn.Inbox.ReadAllAsync(ct))
                {
                    if (envelope.Opcode == NetOpcode.Hello)
                    {
                        // Health probe so the test finishes without guessing packet counts.
                        await conn.SendAsync(NetOpcode.HelloAck,
                            NetCodec.EncodeHelloAck(new HelloAckMsg { ServerName = "telemetry" }), ct: ct);
                        await conn.SendAsync(NetOpcode.Reject,
                            NetCodec.EncodeReject(new RejectMsg { Code = "probe", Message = "probe" }), ct: ct);
                        return;
                    }
                }
            }, cts.Token),
            cts.Token);

        int port = 0;
        for (int i = 0; i < 200 && port == 0; i++)
        {
            await Task.Delay(10, cts.Token);
            port = server.BoundPort;
        }

        Assert.True(port > 0, "net server did not bind");

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port, cts.Token);
        await using var stream = client.GetStream();

        await NetFraming.WriteAsync(stream, new PacketEnvelope
        {
            Opcode = NetOpcode.Hello,
            RequestSequence = 1,
            Payload = NetCodec.EncodeHello(new HelloMsg { ClientName = "telemetry" })
        }, cts.Token);

        Assert.Equal(NetOpcode.HelloAck, Assert.IsType<PacketEnvelope>(await NetFraming.ReadAsync(stream, cts.Token)).Opcode);
        Assert.Equal(NetOpcode.Reject, Assert.IsType<PacketEnvelope>(await NetFraming.ReadAsync(stream, cts.Token)).Opcode);
        await Task.Delay(150, cts.Token);

        Assert.Contains(seen, m => m.Instrument == "fineage.connections" && m.Value == 1 && m.Endpoint == "unittest");
        Assert.Contains(seen, m => m.Instrument == "fineage.frames.in" && m.Endpoint == "unittest");
        Assert.Contains(seen, m => m.Instrument == "fineage.frames.out" && m.Endpoint == "unittest");
        Assert.Contains(seen, m => m.Instrument == "fineage.bytes.in" && m.Endpoint == "unittest");
        Assert.Contains(seen, m => m.Instrument == "fineage.bytes.out" && m.Endpoint == "unittest");
    }
}
