using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using Fineage.Protocol.Net;
using Fineage.Protocol.Telemetry;
using Xunit;

namespace Fineage.Login.Tests;

[Collection("Pipeline OTEL telemetry")]
public sealed class PipelineOutboundDroppedTests
{
    [Fact]
    public async Task Full_outbound_queue_increments_outbound_dropped()
    {
        long dropped = 0;
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == FineageTelemetry.MeterName && instrument.Name == "fineage.outbound.dropped")
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((_, measurement, tags, _) =>
        {
            foreach (KeyValuePair<string, object?> tag in tags)
            {
                if (tag.Key == "endpoint" && tag.Value?.ToString() == "login-test")
                    Interlocked.Add(ref dropped, measurement);
            }
        });
        meterListener.Start();

        var acceptListener = new TcpListener(IPAddress.Loopback, 0);
        acceptListener.Start();
        int port = ((IPEndPoint)acceptListener.LocalEndpoint).Port;
        using var cts = new CancellationTokenSource();

        var serverTask = Task.Run(async () =>
        {
            using var accepted = await acceptListener.AcceptTcpClientAsync(cts.Token);
            accepted.Client.NoDelay = true;
            await using var connection = new PipelineConnection(
                1, "127.0.0.1", accepted.GetStream(), outboundCapacity: 8, "login-test", cts.Token);

            byte[] frame = [0x01, 0x00, 0x00, 0x00];
            int rejections = 0;
            for (int i = 0; i < 24; i++)
            {
                if (!connection.TrySend(frame))
                    rejections++;
            }

            Assert.True(rejections >= 1, "expected at least one TrySend to fail when outbound queue is full");
            await connection.DisposeAsync();
        });

        using var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync(IPAddress.Loopback, port);
        await serverTask;

        acceptListener.Stop();
        for (int i = 0; i < 100 && dropped < 1; i++)
            await Task.Delay(20);

        Assert.True(dropped >= 1, $"expected fineage.outbound.dropped for login-test, got {dropped}");
    }
}
