using System.Net.Sockets;
using Fineage.Login.Net;
using Fineage.Net.Contract;
using Fineage.Net.Host;
using Xunit;

namespace Fineage.Login.Tests;

/// <summary>
/// Read-loop guards the Net lobby inherited from the retired V2 listener: an idle client and a flooding
/// client both lose their slot instead of holding it forever.
/// </summary>
public sealed class NetLoginGuardTests
{
    [Fact]
    public async Task Flooding_past_the_per_second_cap_is_rejected_with_rate_limit()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var link = await LoginLink.OpenAsync(maxMessagesPerSecond: 3, idleSeconds: 1800);

        await link.HelloAsync(timeout.Token);

        // Hello already took one slot in the three-per-second window.
        PacketEnvelope? last = null;
        for (int i = 0; i < 8; i++)
        {
            if (!await link.SendAsync(NetOpcode.ListCharacters, [], timeout.Token))
                break;
            last = await link.ReadAsync(timeout.Token);
            if (last is { Opcode: NetOpcode.Reject } reject &&
                NetCodec.DecodeReject(reject.Payload).Code == "rate_limit")
                break;
        }

        Assert.NotNull(last);
        Assert.Equal(NetOpcode.Reject, last!.Opcode);
        Assert.Equal("rate_limit", NetCodec.DecodeReject(last.Payload).Code);
        Assert.Null(await link.ReadAsync(timeout.Token, allowEndOfStream: true));
    }

    [Fact]
    public async Task Silent_client_is_disconnected_after_the_idle_timeout()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var link = await LoginLink.OpenAsync(maxMessagesPerSecond: 120, idleSeconds: 1);

        await link.HelloAsync(timeout.Token);

        // A read deadline shorter than the test budget: a hung watchdog reports a timeout, not a clean EOF.
        using var readDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.Null(await link.ReadAsync(readDeadline.Token, allowEndOfStream: true));
    }

    /// <summary>Loopback Net lobby link whose guards are tuned per test instead of read from the environment.</summary>
    sealed class LoginLink : IAsyncDisposable
    {
        readonly CancellationTokenSource _cts;
        readonly TcpClient _client;
        readonly NetworkStream _stream;
        readonly Task _serve;
        uint _sequence;

        LoginLink(CancellationTokenSource cts, TcpClient client, NetworkStream stream, Task serve)
        {
            _cts = cts;
            _client = client;
            _stream = stream;
            _serve = serve;
        }

        public static async Task<LoginLink> OpenAsync(int maxMessagesPerSecond, int idleSeconds)
        {
            var cts = new CancellationTokenSource();
            var service = new LoginService(JsonAccountStore.InMemory(), new HandoffTokens(HandoffTokens.DefaultSecret));
            var lobby = new NetLoginServer(
                service,
                maxMessagesPerSecond: maxMessagesPerSecond,
                idleTimeout: TimeSpan.FromSeconds(idleSeconds));
            var server = new NetTcpServer();

            var serve = Task.Run(
                () => server.RunAsync("127.0.0.1", 0, lobby.HandleAsync, cts.Token, maxConnections: 4, inboxCapacity: 64),
                cts.Token);
            int port = 0;
            for (int i = 0; i < 200 && port == 0; i++)
            {
                await Task.Delay(10, cts.Token);
                port = server.BoundPort;
            }

            Assert.True(port > 0, "login net server did not bind");

            var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port, cts.Token);
            return new LoginLink(cts, client, client.GetStream(), serve);
        }

        public async Task HelloAsync(CancellationToken ct)
        {
            await SendAsync(NetOpcode.Hello, NetCodec.EncodeHello(new HelloMsg { ClientName = "guard" }), ct);
            PacketEnvelope ack = Assert.IsType<PacketEnvelope>(await ReadAsync(ct));
            Assert.Equal(NetOpcode.HelloAck, ack.Opcode);
        }

        /// <summary>False once the listener has closed the socket, so a flooding loop can stop cleanly.</summary>
        public async Task<bool> SendAsync(ushort opcode, byte[] payload, CancellationToken ct)
        {
            try
            {
                await NetFraming.WriteAsync(_stream, new PacketEnvelope
                {
                    Opcode = opcode,
                    RequestSequence = unchecked(++_sequence),
                    Payload = payload
                }, ct);
                return true;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                return false;
            }
        }

        public async Task<PacketEnvelope?> ReadAsync(CancellationToken ct, bool allowEndOfStream = false)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
            try
            {
                return await NetFraming.ReadAsync(_stream, linked.Token);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested && !_cts.IsCancellationRequested)
            {
                throw new TimeoutException("no frame within the read deadline");
            }
            catch (Exception ex) when (allowEndOfStream && ex is IOException or OperationCanceledException)
            {
                return null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try { await _cts.CancelAsync(); } catch { /* ignore */ }
            try { await _serve.WaitAsync(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
            try { _client.Dispose(); } catch { /* ignore */ }
            _cts.Dispose();
        }
    }
}
