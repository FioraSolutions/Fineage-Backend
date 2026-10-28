using System.Net;
using System.Net.Sockets;
using Fineage.Login;
using Fineage.Protocol;
using MessagePack;

namespace Fineage.Zone;

public static class ZoneAcceptance
{
    public static async Task RunAsync()
    {
        var login = await LoginAcceptance.RunAsync().ConfigureAwait(false);
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);

        await using (var zone = ZoneListen.Start(tokens))
        {
            await PlayAsync(zone.Port, login.Fighter.Account, login.Fighter.Token, login.Fighter.CharacterId, login.Fighter.CharacterName)
                .ConfigureAwait(false);
            await PlayAsync(zone.Port, login.Mystic.Account, login.Mystic.Token, login.Mystic.CharacterId, login.Mystic.CharacterName)
                .ConfigureAwait(false);
        }
    }

    public static void Write(TextWriter output) => output.WriteLine("selftest OK");

    static async Task PlayAsync(int port, string account, string token, int characterId, string name)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port).ConfigureAwait(false);
        await using var stream = client.GetStream();

        await SendAsync(stream, MsgType.Hello, new HelloMsg { ProtocolVersion = 1, ClientBuild = "selftest" })
            .ConfigureAwait(false);
        await ExpectAsync(stream, MsgType.HelloAck).ConfigureAwait(false);

        await SendAsync(stream, MsgType.EnterWorld, new EnterWorldMsg()).ConfigureAwait(false);
        var early = await ExpectErrorAsync(stream).ConfigureAwait(false);
        if (early.Code != "auth_required")
            throw new InvalidOperationException($"expected auth_required, got {early.Code}");

        await SendAsync(stream, MsgType.Auth, new AuthMsg { Account = account, Password = "not-a-token" })
            .ConfigureAwait(false);
        var bad = await ExpectErrorAsync(stream).ConfigureAwait(false);
        if (bad.Code != "invalid_credentials")
            throw new InvalidOperationException($"expected invalid_credentials, got {bad.Code}");

        await SendAsync(stream, MsgType.Auth, new AuthMsg { Account = account, Password = token })
            .ConfigureAwait(false);
        var authBody = await ExpectAsync(stream, MsgType.AuthOk).ConfigureAwait(false);
        var auth = MessagePackSerializer.Deserialize<AuthOkMsg>(authBody);
        if (auth.CharacterName != name || auth.SessionId == 0)
            throw new InvalidOperationException($"auth ok mismatch name={auth.CharacterName} session={auth.SessionId}");

        await SendAsync(stream, MsgType.MoveTo, new MoveToMsg
        {
            DestX = TalkingIsland.SpawnX + 10,
            DestY = TalkingIsland.SpawnY,
            DestZ = TalkingIsland.SpawnZ
        }).ConfigureAwait(false);
        var tooSoon = await ExpectErrorAsync(stream).ConfigureAwait(false);
        if (tooSoon.Code != "world")
            throw new InvalidOperationException($"expected world, got {tooSoon.Code}");

        await SendAsync(stream, MsgType.EnterWorld, new EnterWorldMsg { CharacterSlot = 0 }).ConfigureAwait(false);
        var spawnedBody = await ExpectAsync(stream, MsgType.PlayerSpawned).ConfigureAwait(false);
        var spawned = MessagePackSerializer.Deserialize<PlayerSpawnedMsg>(spawnedBody);
        if (spawned.ObjectId != characterId || spawned.Name != name ||
            spawned.X != TalkingIsland.SpawnX || spawned.Y != TalkingIsland.SpawnY || spawned.Z != TalkingIsland.SpawnZ)
            throw new InvalidOperationException($"spawn mismatch id={spawned.ObjectId} name={spawned.Name} @ {spawned.X},{spawned.Y},{spawned.Z}");

        var seen = new List<NpcInfoMsg>();
        for (int i = 0; i < TalkingIsland.Gremlins.Length; i++)
        {
            var body = await ExpectAsync(stream, MsgType.NpcInfo).ConfigureAwait(false);
            seen.Add(MessagePackSerializer.Deserialize<NpcInfoMsg>(body));
        }

        if (seen.Count != TalkingIsland.Gremlins.Length)
            throw new InvalidOperationException($"expected {TalkingIsland.Gremlins.Length} gremlins, got {seen.Count}");
        for (int i = 0; i < seen.Count; i++)
        {
            var npc = seen[i];
            var expected = TalkingIsland.Gremlins[i];
            if (npc.NpcId != TalkingIsland.GremlinNpcId || npc.Name != "Gremlin" ||
                npc.ObjectId != expected.ObjectId || npc.X != expected.X || npc.Y != expected.Y || npc.Z != expected.Z)
                throw new InvalidOperationException($"gremlin {i} mismatch id={npc.NpcId} @ {npc.X},{npc.Y},{npc.Z}");
        }

        var nearest = TalkingIsland.Gremlins[0];
        await SendAsync(stream, MsgType.MoveTo, new MoveToMsg
        {
            ObjectId = spawned.ObjectId,
            DestX = nearest.X,
            DestY = nearest.Y,
            DestZ = nearest.Z,
            OriginX = spawned.X,
            OriginY = spawned.Y,
            OriginZ = spawned.Z
        }).ConfigureAwait(false);
        var ackBody = await ExpectAsync(stream, MsgType.MoveToAck).ConfigureAwait(false);
        var ack = MessagePackSerializer.Deserialize<MoveToMsg>(ackBody);
        if (ack.ObjectId != characterId || ack.OriginX != TalkingIsland.SpawnX || ack.OriginY != TalkingIsland.SpawnY ||
            ack.DestX != nearest.X || ack.DestY != nearest.Y)
            throw new InvalidOperationException($"move ack mismatch dest={ack.DestX},{ack.DestY} origin={ack.OriginX},{ack.OriginY}");
    }

    static async Task SendAsync<T>(NetworkStream stream, MsgType type, T msg)
    {
        byte[] body = MessagePackSerializer.Serialize(msg);
        await FrameIO.SendAsync(stream, (ushort)type, body).ConfigureAwait(false);
    }

    static async Task<byte[]> ExpectAsync(NetworkStream stream, MsgType expected)
    {
        var frame = await FrameIO.ReadAsync(stream).ConfigureAwait(false);
        if (frame == null)
            throw new InvalidOperationException($"expected {expected}, got EOF");
        if (frame.Value.Type != (ushort)expected)
        {
            string detail = "";
            if (frame.Value.Type == (ushort)MsgType.Error)
            {
                var err = MessagePackSerializer.Deserialize<ErrorMsg>(frame.Value.Body);
                detail = $" {err.Code} {err.Message}";
            }

            throw new InvalidOperationException($"expected {expected}, got {(MsgType)frame.Value.Type}{detail}");
        }

        return frame.Value.Body;
    }

    static async Task<ErrorMsg> ExpectErrorAsync(NetworkStream stream)
    {
        var body = await ExpectAsync(stream, MsgType.Error).ConfigureAwait(false);
        return MessagePackSerializer.Deserialize<ErrorMsg>(body);
    }

    sealed class ZoneListen : IAsyncDisposable
    {
        readonly TcpListener _listener;
        readonly CancellationTokenSource _cts;
        readonly Task _loop;

        ZoneListen(TcpListener listener, CancellationTokenSource cts, Task loop)
        {
            _listener = listener;
            _cts = cts;
            _loop = loop;
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        public int Port { get; }

        public static ZoneListen Start(HandoffTokens tokens)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var cts = new CancellationTokenSource();
            var server = new ZoneServer(tokens);
            var loop = Task.Run(async () =>
            {
                try
                {
                    while (!cts.IsCancellationRequested)
                    {
                        TcpClient client;
                        try
                        {
                            client = await listener.AcceptTcpClientAsync(cts.Token).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
                        {
                            break;
                        }

                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await using var stream = client.GetStream();
                                await server.HandleAsync(stream, cts.Token).ConfigureAwait(false);
                            }
                            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
                            {
                            }
                            finally
                            {
                                client.Dispose();
                            }
                        });
                    }
                }
                catch (ObjectDisposedException)
                {
                }
            });

            return new ZoneListen(listener, cts, loop);
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync().ConfigureAwait(false);
            _listener.Stop();
            try
            {
                await _loop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
            }

            _cts.Dispose();
        }
    }
}
