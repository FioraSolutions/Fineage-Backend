using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Fineage.Login.Net;
using Fineage.Net.Contract;
using Fineage.Net.Host;
using Xunit;

namespace Fineage.Login.Tests;

/// <summary>
/// Loopback client for the Net login listener. Drives the real <see cref="NetLoginServer"/> over a
/// socket so a test asserts the envelopes the Unity client actually receives, not a helper's idea of
/// them. Every request carries a fresh <c>RequestSequence</c> because the server dedupes on it.
/// </summary>
sealed class NetLink : IAsyncDisposable
{
    readonly TcpListener _listener;
    readonly CancellationTokenSource _cts;
    readonly Task _accept;
    readonly TcpClient _client;
    readonly NetworkStream _stream;
    uint _sequence;

    NetLink(TcpListener listener, CancellationTokenSource cts, Task accept, TcpClient client, NetworkStream stream)
    {
        _listener = listener;
        _cts = cts;
        _accept = accept;
        _client = client;
        _stream = stream;
    }

    public static async Task<NetLink> OpenAsync(NetLoginServer server)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var cts = new CancellationTokenSource();
        var accept = Task.Run(async () =>
        {
            using var accepted = await listener.AcceptTcpClientAsync(cts.Token);
            await using var conn = new NetConnection(accepted, 256);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            Task read = conn.RunReadLoopAsync(linked.Token);
            try
            {
                await server.HandleAsync(conn, linked.Token);
            }
            finally
            {
                linked.Cancel();
                try { await read; } catch { /* shutdown */ }
            }
        });
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        return new NetLink(listener, cts, accept, client, client.GetStream());
    }

    /// <summary>Hello + Auth in one call; the lobby rejects every other opcode until both land.</summary>
    public async Task AutoAuthAsync(string account, string password)
    {
        await SendAsync(NetOpcode.Hello, NetCodec.EncodeHello(new HelloMsg
        {
            Major = 1,
            Minor = 0,
            ClientName = "tests",
            FeatureFlags = ProtocolCapabilities.FeatureSession | ProtocolCapabilities.FeatureLobby
        }));
        Assert.Equal(NetOpcode.HelloAck, (await ReadAsync()).Opcode);

        await SendAsync(NetOpcode.Auth, NetCodec.EncodeAuth(new AuthMsg { Account = account, Password = password }));
        PacketEnvelope reply = await ReadAsync();
        Assert.Equal(NetOpcode.AuthResult, reply.Opcode);
        Assert.True(NetCodec.DecodeAuthResult(reply.Payload).Ok);
    }

    public Task SendAsync(ushort opcode, byte[] payload) => SendAsync(opcode, payload, unchecked(++_sequence));

    async Task SendAsync(ushort opcode, byte[] payload, uint sequence)
    {
        byte[] frame = NetFraming.Encode(new PacketEnvelope
        {
            Major = 1,
            Minor = 0,
            Opcode = opcode,
            Flags = (ushort)PacketFlags.Reliable,
            RequestSequence = sequence,
            Payload = payload
        });
        await _stream.WriteAsync(frame, _cts.Token);
        await _stream.FlushAsync(_cts.Token);
    }

    public async Task<PacketEnvelope> ExpectAsync(ushort expected)
    {
        PacketEnvelope envelope = await ReadAsync();
        Assert.Equal(expected, envelope.Opcode);
        return envelope;
    }

    public async Task<byte[]> ExpectPayloadAsync(ushort expected) => (await ExpectAsync(expected)).Payload;

    async Task<PacketEnvelope> ReadAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        var header = new byte[4];
        await _stream.ReadExactlyAsync(header, timeout.Token);
        int total = (int)BinaryPrimitives.ReadUInt32LittleEndian(header);
        var body = new byte[total];
        await _stream.ReadExactlyAsync(body, timeout.Token);

        Assert.True(NetFraming.TryDecode(body, out PacketEnvelope envelope, out string? error),
            $"frame did not decode: {error}");
        return envelope!;
    }

    public async ValueTask DisposeAsync()
    {
        try { await _cts.CancelAsync(); } catch { /* ignore */ }
        try { await _accept.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or IOException)
        {
        }

        try { _client.Dispose(); } catch { /* ignore */ }
        _listener.Stop();
        _cts.Dispose();
    }
}
