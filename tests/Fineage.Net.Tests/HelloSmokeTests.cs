using System.Net.Sockets;
using Fineage.Net.Contract;
using Fineage.Net.Host;
using Xunit;

namespace Fineage.Net.Tests;

public class HelloSmokeTests
{
    [Fact]
    public async Task Tcp_hello_roundtrip()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var server = new NetTcpServer();
        var ready = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var host = Task.Run(async () =>
        {
            await server.RunAsync(
                "127.0.0.1",
                0,
                async (conn, ct) =>
                {
                    ready.TrySetResult(server.BoundPort);
                    await foreach (var envelope in conn.Inbox.ReadAllAsync(ct))
                    {
                        if (envelope.Opcode != NetOpcode.Hello)
                            continue;
                        var hello = NetCodec.DecodeHello(envelope.Payload);
                        Assert.Equal("test", hello.ClientName);
                        await conn.SendAsync(
                            NetOpcode.HelloAck,
                            NetCodec.EncodeHelloAck(new HelloAckMsg
                            {
                                FeatureFlags = ProtocolCapabilities.FeatureSession,
                                ServerName = "test"
                            }),
                            ct: ct);
                        return;
                    }
                },
                cts.Token,
                maxConnections: 4,
                inboxCapacity: 32);
        }, cts.Token);

        // Accept loop binds asynchronously — poll BoundPort
        int port = 0;
        for (int i = 0; i < 50 && port == 0; i++)
        {
            await Task.Delay(20, cts.Token);
            port = server.BoundPort;
        }

        Assert.True(port > 0, "server did not bind");

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port, cts.Token);
        var stream = client.GetStream();
        await NetFraming.WriteAsync(stream, new PacketEnvelope
        {
            Opcode = NetOpcode.Hello,
            RequestSequence = 1,
            Payload = NetCodec.EncodeHello(new HelloMsg { ClientName = "test", FeatureFlags = ProtocolCapabilities.FeatureSession })
        }, cts.Token);

        var ack = await NetFraming.ReadAsync(stream, cts.Token);
        Assert.NotNull(ack);
        Assert.Equal(NetOpcode.HelloAck, ack!.Opcode);
        var decoded = NetCodec.DecodeHelloAck(ack.Payload);
        Assert.Equal("test", decoded.ServerName);

        cts.Cancel();
        try { await host; } catch (OperationCanceledException) { /* expected */ }
    }
}
