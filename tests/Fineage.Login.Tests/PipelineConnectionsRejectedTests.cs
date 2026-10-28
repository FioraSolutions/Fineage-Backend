using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Protocol.Net;
using Fineage.Protocol.Telemetry;
using MessagePack;
using Xunit;
using ProtocolMsgType = Fineage.Protocol.MsgType;

namespace Fineage.Login.Tests;

[Collection("Pipeline OTEL telemetry")]
public sealed class PipelineConnectionsRejectedTests
{
    [Fact]
    public async Task Connection_cap_increments_connections_rejected_and_returns_server_busy()
    {
        long rejected = 0;
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == FineageTelemetry.MeterName && instrument.Name == "fineage.connections.rejected")
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((_, measurement, tags, _) =>
        {
            foreach (KeyValuePair<string, object?> tag in tags)
            {
                if (tag.Key == "endpoint" && tag.Value?.ToString() == "login-test")
                    Interlocked.Add(ref rejected, measurement);
            }
        });
        meterListener.Start();

        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();
        var tcp = new TcpServer();
        var run = Task.Run(() => tcp.RunAsync(
            "127.0.0.1",
            0,
            maxConnections: 1,
            outboundQueue: 8,
            "login-test",
            (_, ct) => hold.Task.WaitAsync(ct),
            cts.Token,
            RejectOverloadAsync));

        for (int i = 0; i < 100 && tcp.BoundPort == 0; i++)
            await Task.Delay(20);

        Assert.NotEqual(0, tcp.BoundPort);

        using var first = new TcpClient();
        await first.ConnectAsync(IPAddress.Loopback, tcp.BoundPort);

        using var second = new TcpClient();
        await second.ConnectAsync(IPAddress.Loopback, tcp.BoundPort);
        await using var overloadStream = second.GetStream();
        var frame = await FrameIO.ReadAsync(overloadStream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)ProtocolMsgType.Error, frame!.Value.Type);
        var err = MessagePackSerializer.Deserialize<ErrorMsg>(frame.Value.Body);
        Assert.Equal("server_busy", err.Code);

        for (int i = 0; i < 100 && rejected < 1; i++)
            await Task.Delay(20);

        Assert.True(rejected >= 1, $"expected fineage.connections.rejected for login-test, got {rejected}");

        hold.TrySetResult();
        await cts.CancelAsync();
        try
        {
            await run.WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (TimeoutException)
        {
        }
    }

    static async ValueTask RejectOverloadAsync(Stream stream)
    {
        byte[] body = MessagePackSerializer.Serialize(new ErrorMsg
        {
            Code = "server_busy",
            Message = "Login is at capacity."
        });
        await FrameIO.SendAsync(stream, (ushort)ProtocolMsgType.Error, body, CancellationToken.None);
    }
}
