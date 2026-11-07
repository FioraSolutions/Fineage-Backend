using System.Net.Sockets;
using Fineage.Accounts;
using Fineage.Login;
using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// A loopback Net zone listener on one <see cref="NetZoneServer"/>. Fanout between sessions only happens
/// inside one server (it owns the presence hub), so tests that need two players share the host and open
/// sessions against it.
/// </summary>
internal sealed class NetZoneHost : IAsyncDisposable
{
    readonly CancellationTokenSource _cts;
    readonly Task _serve;

    NetZoneHost(CancellationTokenSource cts, Task serve, int port, MemoryAccountStore store, HandoffTokens tokens, ZoneWorld world)
    {
        _cts = cts;
        _serve = serve;
        Port = port;
        Store = store;
        Tokens = tokens;
        World = world;
    }

    public int Port { get; }
    public MemoryAccountStore Store { get; }
    public HandoffTokens Tokens { get; }
    public ZoneWorld World { get; }

    public static async Task<NetZoneHost> StartAsync(
        ZoneWorld? world = null,
        MemoryAccountStore? store = null,
        HandoffTokens? tokens = null,
        Func<DateTimeOffset>? clock = null,
        int maxMessagesPerSecond = 120,
        int idleSeconds = 1800)
    {
        tokens ??= new HandoffTokens(HandoffTokens.DefaultSecret);
        store ??= MemoryAccountStore.InMemory();
        world ??= new ZoneWorld(accounts: store);
        var cts = new CancellationTokenSource();
        var zone = new NetZoneServer(
            tokens,
            world,
            store,
            clock,
            maxMessagesPerSecond: maxMessagesPerSecond,
            idleTimeout: TimeSpan.FromSeconds(idleSeconds));
        var server = new NetTcpServer();
        var serve = Task.Run(
            () => server.RunAsync("127.0.0.1", 0, zone.HandleAsync, cts.Token, maxConnections: 8, inboxCapacity: 64),
            cts.Token);

        int port = 0;
        for (int i = 0; i < 200 && port == 0; i++)
        {
            await Task.Delay(10, cts.Token);
            port = server.BoundPort;
        }

        Assert.True(port > 0, "zone net server did not bind");
        return new NetZoneHost(cts, serve, port, store, tokens, world);
    }

    public async Task<NetZoneSession> ConnectAsync()
    {
        var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", Port, _cts.Token);
        return new NetZoneSession(client, this);
    }

    /// <summary>Runs <paramref name="body"/> with a single session on a fresh world.</summary>
    public static async Task WithSessionAsync(Func<NetZoneSession, Task> body, int maxMessagesPerSecond = 120, int idleSeconds = 1800)
    {
        await using var host = await StartAsync(maxMessagesPerSecond: maxMessagesPerSecond, idleSeconds: idleSeconds);
        await using var session = await host.ConnectAsync();
        await body(session);
    }

    public async ValueTask DisposeAsync()
    {
        try { await _cts.CancelAsync(); } catch { /* ignore */ }
        try { await _serve.WaitAsync(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
        _cts.Dispose();
    }
}

/// <summary>
/// One Net zone session on a shared <see cref="NetZoneHost"/>. <see cref="EnteredName"/> is the name the
/// last enter burst spawned as self, so a refused re-auth can be told apart from a silent character swap.
/// </summary>
internal sealed class NetZoneSession : IAsyncDisposable
{
    readonly TcpClient _client;
    readonly NetworkStream _stream;
    uint _sequence;
    bool _registered;

    internal NetZoneSession(TcpClient client, NetZoneHost host)
    {
        _client = client;
        _stream = client.GetStream();
        Host = host;
    }

    public NetZoneHost Host { get; }
    public ZoneWorld World => Host.World;

    /// <summary>Raw socket, for assertions that intentionally read past the helper's expect loop.</summary>
    public NetworkStream Stream => _stream;
    public MemoryAccountStore Store => Host.Store;
    public HandoffTokens Tokens => Host.Tokens;

    /// <summary>Name carried by the self <c>EntitySpawn</c> of the last enter burst.</summary>
    public string? EnteredName { get; private set; }

    public async Task HelloAsync(CancellationToken ct)
    {
        PacketEnvelope ack = await ExchangeAsync(
            NetOpcode.Hello,
            NetCodec.EncodeHello(new HelloMsg { ClientName = "zone-session" }),
            ct);
        Assert.Equal(NetOpcode.HelloAck, ack.Opcode);
    }

    /// <summary>Handoff ticket for a throwaway in-memory human on the host's store.</summary>
    public HandoffTicket Ticket(string account, string name, Func<DateTimeOffset>? clock = null)
    {
        var login = new LoginService(Store, Tokens, clock);
        if (!_registered)
        {
            login.Register(account, "pass1234");
            login.CreateHuman(account, name, HumanClasses.Fighter);
            _registered = true;
        }

        var play = new LoginService(Store, Tokens, clock).Play(account, "pass1234", name, HumanClasses.Fighter);
        Assert.True(play.Ok && play.Value != null);
        return play.Value!;
    }

    public async Task AuthAsync(string account, string name, CancellationToken ct)
    {
        HandoffTicket ticket = Ticket(account, name);
        PacketEnvelope reply = await ExchangeAsync(
            NetOpcode.ZoneAuth,
            NetCodec.EncodeZoneAuth(new ZoneAuthMsg { Account = account, Token = ticket.Token }),
            ct);
        Assert.Equal(NetOpcode.ZoneAuthResult, reply.Opcode);
        Assert.True(NetCodec.DecodeZoneAuthResult(reply.Payload).Ok);
    }

    /// <summary>Sends a request and returns the refusal code, accepting both <c>Reject</c> and a failed auth result.</summary>
    public async Task<string> RefusalCodeAsync(ushort opcode, byte[] payload, CancellationToken ct)
    {
        PacketEnvelope reply = await ExchangeAsync(opcode, payload, ct);
        if (reply.Opcode == NetOpcode.ZoneAuthResult)
        {
            ZoneAuthResultMsg result = NetCodec.DecodeZoneAuthResult(reply.Payload);
            Assert.False(result.Ok);
            return result.Code;
        }

        Assert.Equal(NetOpcode.Reject, reply.Opcode);
        return NetCodec.DecodeReject(reply.Payload).Code;
    }

    public async Task<PacketEnvelope> ExchangeAsync(ushort opcode, byte[] payload, CancellationToken ct)
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
    /// Drives <c>EnterWorld</c> to the end of its burst. The burst (sheets, shortcuts, welcome, knownlist)
    /// carries no terminator, so it closes once a quiet window passes after the welcome line.
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
                throw new InvalidOperationException($"enter refused: {NetCodec.DecodeReject(envelope.Payload).Code}");

            if (envelope.Opcode == NetOpcode.SystemMessage)
                sawWelcome = true;
            if (envelope.Opcode == NetOpcode.EntitySpawn)
            {
                EntitySpawnMsg spawn = NetCodec.DecodeEntitySpawn(envelope.Payload);
                if (spawn.IsSelf)
                    EnteredName = spawn.Name;
            }
        }
    }

    /// <summary>Waits for one opcode, skipping unrelated fanout (presence and status frames are load dependent).</summary>
    public async Task<PacketEnvelope> ExpectAsync(ushort expected, CancellationToken ct)
    {
        while (true)
        {
            PacketEnvelope? envelope = await NetFraming.ReadAsync(_stream, ct).ConfigureAwait(false);
            if (envelope == null)
                throw new InvalidOperationException($"zone closed while waiting for 0x{expected:X4}");
            if (envelope.Opcode == expected)
                return envelope;
            if (envelope.Opcode == NetOpcode.Reject)
                throw new InvalidOperationException(
                    $"rejected while waiting for 0x{expected:X4}: {NetCodec.DecodeReject(envelope.Payload).Code}");
        }
    }

    /// <summary>Closes the socket and waits for the server null its presence entry.</summary>
    public async Task DisconnectAsync()
    {
        try { _client.Close(); } catch { /* already gone */ }
        for (int i = 0; i < 200 && World.OnlinePlayerCount != 0; i++)
            await Task.Delay(10);
    }

    public ValueTask DisposeAsync()
    {
        try { _client.Dispose(); } catch { /* ignore */ }
        return ValueTask.CompletedTask;
    }
}
