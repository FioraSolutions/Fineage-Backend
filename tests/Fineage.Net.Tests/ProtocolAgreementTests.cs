using System.Buffers;
using System.Net.Sockets;
using Fineage.Net.Contract;
using Fineage.Net.Host;
using Xunit;

namespace Fineage.Net.Tests;

/// <summary>
/// Enforces the version-agreement rules in docs/adr/0001-custom-net-wire.md.
/// </summary>
public sealed class ProtocolAgreementTests
{
    [Fact]
    public void Frame_major_must_match_ProtocolMajor()
    {
        var ok = new PacketEnvelope { Major = PacketEnvelope.ProtocolMajor, Opcode = NetOpcode.Hello };
        Assert.True(NetProtocol.AcceptsFrame(ok));

        var wrong = new PacketEnvelope { Major = (byte)(PacketEnvelope.ProtocolMajor + 1), Opcode = NetOpcode.Hello };
        Assert.False(NetProtocol.AcceptsFrame(wrong));
    }

    [Fact]
    public void Framing_still_decodes_a_well_formed_wrong_major_frame()
    {
        // Version checks happen after decode, not inside NetFraming.
        byte[] frame = NetFraming.Encode(new PacketEnvelope
        {
            Major = 2,
            Minor = 0,
            Opcode = NetOpcode.Hello,
            Payload = NetCodec.EncodeHello(new HelloMsg { Major = 2, ClientName = "x" })
        });
        Assert.True(NetFraming.TryReadFrame(new ReadOnlySequence<byte>(frame), out PacketEnvelope? envelope, out string? error, out _));
        Assert.Null(error);
        Assert.Equal(2, envelope!.Major);
        Assert.False(NetProtocol.AcceptsFrame(envelope));
    }

    [Fact]
    public void Hello_payload_major_mismatch_is_protocol_mismatch_and_does_not_grant_flags()
    {
        uint granted = ProtocolCapabilities.FeatureSession | ProtocolCapabilities.FeatureLobby;
        var hello = new HelloMsg
        {
            Major = 2,
            Minor = PacketEnvelope.ProtocolMinor,
            FeatureFlags = granted,
            ClientName = "x"
        };
        Assert.False(NetProtocol.TryAcceptHello(hello, granted, out uint flags, out RejectMsg? reject));
        Assert.Equal(0u, flags);
        Assert.NotNull(reject);
        Assert.Equal(NetProtocol.MismatchCode, reject!.Code);
        Assert.Equal(NetProtocol.MismatchMessage, reject.Message);
    }

    [Fact]
    public void Hello_intersects_feature_flags_and_fills_in_a_zero_client_mask()
    {
        uint granted = ProtocolCapabilities.FeatureSession | ProtocolCapabilities.FeatureLobby;
        Assert.True(NetProtocol.TryAcceptHello(
            new HelloMsg { FeatureFlags = ProtocolCapabilities.FeatureSession },
            granted,
            out uint flags,
            out RejectMsg? reject));
        Assert.Null(reject);
        Assert.Equal(ProtocolCapabilities.FeatureSession, flags);

        Assert.True(NetProtocol.TryAcceptHello(new HelloMsg { FeatureFlags = 0 }, granted, out flags, out reject));
        Assert.Null(reject);
        Assert.Equal(granted, flags);
    }

    [Fact]
    public async Task Wrong_frame_major_closes_the_connection_and_is_not_delivered()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var delivered = new List<PacketEnvelope>();
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        (NetTcpServer server, Task host, int port) = await StartHost(async (conn, ct) =>
        {
            try
            {
                await foreach (var envelope in conn.Inbox.ReadAllAsync(ct))
                    delivered.Add(envelope);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                closed.TrySetResult();
            }
        }, cts.Token);

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port, cts.Token);
        var stream = client.GetStream();
        await NetFraming.WriteAsync(stream, new PacketEnvelope
        {
            Major = 2,
            Opcode = NetOpcode.Hello,
            RequestSequence = 1,
            Payload = NetCodec.EncodeHello(new HelloMsg { Major = 2, ClientName = "x" })
        }, cts.Token);

        PacketEnvelope? reply = await NetFraming.ReadAsync(stream, cts.Token);
        Assert.Null(reply);
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5), cts.Token);
        Assert.Empty(delivered);

        cts.Cancel();
        try { await host; } catch (OperationCanceledException) { /* expected */ }
        GC.KeepAlive(server);
    }

    [Fact]
    public async Task Hello_payload_major_mismatch_is_rejected_and_hello_is_not_completed()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var helloCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        (NetTcpServer server, Task host, int port) = await StartHost(async (conn, ct) =>
        {
            await foreach (var envelope in conn.Inbox.ReadAllAsync(ct))
            {
                if (envelope.Opcode != NetOpcode.Hello)
                    continue;
                var hello = NetCodec.DecodeHello(envelope.Payload);
                uint granted = ProtocolCapabilities.FeatureSession | ProtocolCapabilities.FeatureLobby;
                if (!NetProtocol.TryAcceptHello(hello, granted, out uint featureFlags, out RejectMsg? reject))
                {
                    await conn.SendAsync(NetOpcode.Reject, NetCodec.EncodeReject(reject!), ct: ct);
                    helloCompleted.TrySetResult(conn.HelloCompleted);
                    return;
                }

                conn.HelloCompleted = true;
                conn.FeatureFlags = featureFlags;
                await conn.SendAsync(
                    NetOpcode.HelloAck,
                    NetCodec.EncodeHelloAck(new HelloAckMsg { FeatureFlags = featureFlags, ServerName = "test" }),
                    ct: ct);
                helloCompleted.TrySetResult(conn.HelloCompleted);
                return;
            }
        }, cts.Token);

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port, cts.Token);
        var stream = client.GetStream();
        // Header major is current (so the host delivers the frame); the Hello payload is not.
        await NetFraming.WriteAsync(stream, new PacketEnvelope
        {
            Opcode = NetOpcode.Hello,
            RequestSequence = 1,
            Payload = NetCodec.EncodeHello(new HelloMsg
            {
                Major = 2,
                Minor = PacketEnvelope.ProtocolMinor,
                ClientName = "x",
                FeatureFlags = ProtocolCapabilities.FeatureSession
            })
        }, cts.Token);

        PacketEnvelope? reply = await NetFraming.ReadAsync(stream, cts.Token);
        Assert.NotNull(reply);
        Assert.Equal(NetOpcode.Reject, reply!.Opcode);
        var rejected = NetCodec.DecodeReject(reply.Payload);
        Assert.Equal(NetProtocol.MismatchCode, rejected.Code);
        Assert.False(await helloCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5), cts.Token));

        cts.Cancel();
        try { await host; } catch (OperationCanceledException) { /* expected */ }
        GC.KeepAlive(server);
    }

    static async Task<(NetTcpServer Server, Task Host, int Port)> StartHost(
        Func<NetConnection, CancellationToken, Task> handler,
        CancellationToken ct)
    {
        var server = new NetTcpServer();
        var host = Task.Run(() => server.RunAsync("127.0.0.1", 0, handler, ct, maxConnections: 4, inboxCapacity: 32), ct);
        int port = 0;
        for (int i = 0; i < 50 && port == 0; i++)
        {
            await Task.Delay(20, ct);
            port = server.BoundPort;
        }

        Assert.True(port > 0, "server did not bind");
        return (server, host, port);
    }
}
