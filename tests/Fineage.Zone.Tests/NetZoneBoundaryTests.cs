using System.Net.Sockets;
using Fineage.Login;
using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Wire-boundary behaviour that used to be asserted only through the V2 MessagePack listener: which requests
/// are refused before the world sees them, and what a malformed body does. Net has to answer the same codes,
/// because the client only speaks Net.
/// </summary>
public sealed class NetZoneBoundaryTests
{
    [Fact]
    public async Task EnterWorld_before_auth_is_refused_with_auth_required()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var link = await NetZoneLink.OpenAsync();
        await link.HelloAsync(timeout.Token);

        string code = await link.RejectCodeAsync(
            NetOpcode.EnterWorld,
            NetCodec.EncodeEnterWorld(new EnterWorldMsg()),
            timeout.Token);

        Assert.Equal("auth_required", code);
    }

    [Fact]
    public async Task Move_before_auth_is_refused_with_not_in_world()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var link = await NetZoneLink.OpenAsync();
        await link.HelloAsync(timeout.Token);

        string code = await link.RejectCodeAsync(
            NetOpcode.MoveInput,
            NetCodec.EncodeMoveInput(new MoveInputMsg { DestX = 1, DestY = 1, DestZ = 0 }),
            timeout.Token);

        Assert.Equal("not_in_world", code);
    }

    [Fact]
    public async Task A_truncated_body_is_refused_without_taking_the_connection_down()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var link = await NetZoneLink.OpenAsync();
        await link.HelloAsync(timeout.Token);
        await link.AuthAsync("shape", "Shape", timeout.Token);
        await link.EnterWorldAsync(timeout.Token);

        // One stray byte where ValidatePosition expects four int32s: decoding throws after the auth/enter
        // gates pass, so the error comes from the handler rather than from the guard in front of it.
        string code = await link.RejectCodeAsync(NetOpcode.ValidatePosition, [0xC0], timeout.Token);
        Assert.Equal("handler_error", code);

        // The handler caught it, so the session survives and the next malformed body is answered too
        // instead of the socket closing.
        Assert.Equal("handler_error", await link.RejectCodeAsync(NetOpcode.ValidatePosition, [0xC0], timeout.Token));
    }

    [Fact]
    public async Task An_unknown_opcode_is_refused_with_unknown_opcode()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var link = await NetZoneLink.OpenAsync();
        await link.HelloAsync(timeout.Token);

        Assert.Equal("unknown_opcode", await link.RejectCodeAsync(0x0FFE, [], timeout.Token));
    }

    /// <summary>Loopback Net zone session for boundary assertions that need no world traffic.</summary>
    sealed class NetZoneLink : IAsyncDisposable
    {
        readonly CancellationTokenSource _cts;
        readonly TcpClient _client;
        readonly NetworkStream _stream;
        readonly Task _serve;
        readonly MemoryAccountStore _store;
        uint _sequence;

        NetZoneLink(CancellationTokenSource cts, TcpClient client, NetworkStream stream, Task serve, MemoryAccountStore store)
        {
            _cts = cts;
            _client = client;
            _stream = stream;
            _serve = serve;
            _store = store;
        }

        public static async Task<NetZoneLink> OpenAsync()
        {
            var cts = new CancellationTokenSource();
            var store = MemoryAccountStore.InMemory();
            var zone = new NetZoneServer(new HandoffTokens(HandoffTokens.DefaultSecret), new ZoneWorld(accounts: store), store);
            var server = new NetTcpServer();
            var serve = Task.Run(
                () => server.RunAsync("127.0.0.1", 0, zone.HandleAsync, cts.Token, maxConnections: 4, inboxCapacity: 64),
                cts.Token);

            int port = 0;
            for (int i = 0; i < 200 && port == 0; i++)
            {
                await Task.Delay(10, cts.Token);
                port = server.BoundPort;
            }

            Assert.True(port > 0, "zone net server did not bind");

            var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port, cts.Token);
            return new NetZoneLink(cts, client, client.GetStream(), serve, store);
        }

        public async Task HelloAsync(CancellationToken ct)
        {
            PacketEnvelope ack = await ExchangeAsync(
                NetOpcode.Hello,
                NetCodec.EncodeHello(new HelloMsg { ClientName = "boundary" }),
                ct);
            Assert.Equal(NetOpcode.HelloAck, ack.Opcode);
        }

        /// <summary>Handoff auth for a throwaway in-memory character, so handler-level paths are reachable.</summary>
        public async Task AuthAsync(string account, string name, CancellationToken ct)
        {
            var login = new LoginService(_store, new HandoffTokens(HandoffTokens.DefaultSecret));
            Assert.True(login.Register(account, "pass1234").Ok);
            var created = login.CreateHuman(account, name, HumanClasses.Fighter);
            Assert.True(created.Ok);
            var ticket = login.Play(account, "pass1234", created.Value!.Name, HumanClasses.Fighter);
            Assert.True(ticket.Ok && ticket.Value != null);

            PacketEnvelope reply = await ExchangeAsync(
                NetOpcode.ZoneAuth,
                NetCodec.EncodeZoneAuth(new ZoneAuthMsg { Account = account, Token = ticket.Value!.Token }),
                ct);
            Assert.Equal(NetOpcode.ZoneAuthResult, reply.Opcode);
            Assert.True(NetCodec.DecodeZoneAuthResult(reply.Payload).Ok);
        }

        public async Task<string> RejectCodeAsync(ushort opcode, byte[] payload, CancellationToken ct)
        {
            PacketEnvelope reply = await ExchangeAsync(opcode, payload, ct);
            Assert.Equal(NetOpcode.Reject, reply.Opcode);
            return NetCodec.DecodeReject(reply.Payload).Code;
        }

        async Task<PacketEnvelope> ExchangeAsync(ushort opcode, byte[] payload, CancellationToken ct)
        {
            await NetFraming.WriteAsync(_stream, new PacketEnvelope
            {
                Opcode = opcode,
                RequestSequence = unchecked(++_sequence),
                Payload = payload
            }, ct).ConfigureAwait(false);

            return Assert.IsType<PacketEnvelope>(await NetFraming.ReadAsync(_stream, ct).ConfigureAwait(false));
        }

        /// <summary>
        /// Drives EnterWorld to its end so later calls reach the handler body. The enter burst (sheets,
        /// shortcuts, welcome, knownlist) has no terminator, so it closes on a quiet window.
        /// </summary>
        public async Task EnterWorldAsync(CancellationToken ct)
        {
            await NetFraming.WriteAsync(_stream, new PacketEnvelope
            {
                Opcode = NetOpcode.EnterWorld,
                RequestSequence = unchecked(++_sequence),
                Payload = NetCodec.EncodeEnterWorld(new EnterWorldMsg())
            }, ct).ConfigureAwait(false);

            bool sawWelcome = false;
            while (true)
            {
                using var quiet = CancellationTokenSource.CreateLinkedTokenSource(ct);
                if (sawWelcome)
                    quiet.CancelAfter(TimeSpan.FromMilliseconds(300));

                PacketEnvelope? envelope;
                try
                {
                    envelope = await NetFraming.ReadAsync(_stream, quiet.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (sawWelcome && !ct.IsCancellationRequested)
                {
                    return;
                }

                if (envelope == null)
                    throw new InvalidOperationException("zone closed during the enter burst");
                if (envelope.Opcode == NetOpcode.Reject)
                    throw new InvalidOperationException(
                        $"enter refused: {NetCodec.DecodeReject(envelope.Payload).Code}");
                if (envelope.Opcode == NetOpcode.SystemMessage)
                    sawWelcome = true;
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
