using Fineage.Login;
using Fineage.Protocol;
using MessagePack;

namespace Fineage.Zone;

public sealed class ZoneServer
{
    readonly HandoffTokens _tokens;
    readonly Func<DateTimeOffset> _clock;
    int _nextSession = 1;

    public ZoneServer(HandoffTokens tokens, Func<DateTimeOffset>? clock = null)
    {
        _tokens = tokens;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task HandleAsync(Stream stream, CancellationToken ct)
    {
        HandoffClaims? claims = null;
        bool inWorld = false;
        int x = TalkingIsland.SpawnX;
        int y = TalkingIsland.SpawnY;
        int z = TalkingIsland.SpawnZ;
        int sessionId = Interlocked.Increment(ref _nextSession);

        while (!ct.IsCancellationRequested)
        {
            var frame = await FrameIO.ReadAsync(stream, ct).ConfigureAwait(false);
            if (frame == null)
                return;

            switch ((MsgType)frame.Value.Type)
            {
                case MsgType.Hello:
                {
                    var hello = Decode<HelloMsg>(frame.Value.Body) ?? new HelloMsg();
                    ZoneLog.Info($"RX Hello session={sessionId} proto={hello.ProtocolVersion} build={hello.ClientBuild}");
                    await SendAsync(stream, MsgType.HelloAck, new HelloAckMsg
                    {
                        ProtocolVersion = 1,
                        ServerName = "FineageZone"
                    }, ct).ConfigureAwait(false);
                    break;
                }
                case MsgType.Auth:
                {
                    var auth = Decode<AuthMsg>(frame.Value.Body);
                    if (auth == null)
                    {
                        await ErrorAsync(stream, "bad_request", "Auth body was not readable.", ct).ConfigureAwait(false);
                        break;
                    }

                    if (!_tokens.TryValidate(auth.Password, _clock(), out var parsed) ||
                        !string.Equals(parsed.Account, auth.Account, StringComparison.OrdinalIgnoreCase))
                    {
                        ZoneLog.Info($"RX Auth rejected session={sessionId} account={auth.Account}");
                        claims = null;
                        inWorld = false;
                        await ErrorAsync(stream, "invalid_credentials", "Handoff token was rejected.", ct).ConfigureAwait(false);
                        break;
                    }

                    claims = parsed;
                    inWorld = false;
                    x = TalkingIsland.SpawnX;
                    y = TalkingIsland.SpawnY;
                    z = TalkingIsland.SpawnZ;
                    ZoneLog.Info($"RX Auth session={sessionId} account={claims.Account} character={claims.CharacterName}");
                    await SendAsync(stream, MsgType.AuthOk, new AuthOkMsg
                    {
                        SessionId = sessionId,
                        CharacterName = claims.CharacterName
                    }, ct).ConfigureAwait(false);
                    break;
                }
                case MsgType.EnterWorld:
                {
                    if (claims == null)
                    {
                        await ErrorAsync(stream, "auth_required", "Auth with a handoff token first.", ct).ConfigureAwait(false);
                        break;
                    }

                    inWorld = true;
                    x = TalkingIsland.SpawnX;
                    y = TalkingIsland.SpawnY;
                    z = TalkingIsland.SpawnZ;
                    ZoneLog.Info($"RX EnterWorld session={sessionId} character={claims.CharacterName} @ {x},{y},{z}");
                    await SendAsync(stream, MsgType.PlayerSpawned, new PlayerSpawnedMsg
                    {
                        ObjectId = claims.CharacterId,
                        Name = claims.CharacterName,
                        X = x,
                        Y = y,
                        Z = z,
                        RunSpeed = 120f,
                        WalkSpeed = 80f,
                        Running = true
                    }, ct).ConfigureAwait(false);
                    foreach (var npc in TalkingIsland.Gremlins)
                        await SendAsync(stream, MsgType.NpcInfo, npc, ct).ConfigureAwait(false);
                    break;
                }
                case MsgType.MoveTo:
                {
                    if (claims == null || !inWorld)
                    {
                        await ErrorAsync(stream, "world", "EnterWorld first.", ct).ConfigureAwait(false);
                        break;
                    }

                    var move = Decode<MoveToMsg>(frame.Value.Body);
                    if (move == null)
                    {
                        await ErrorAsync(stream, "bad_request", "MoveTo body was not readable.", ct).ConfigureAwait(false);
                        break;
                    }

                    if (!TalkingIsland.WithinStep(x, y, move.DestX, move.DestY))
                    {
                        await ErrorAsync(stream, "range", "Destination is too far.", ct).ConfigureAwait(false);
                        break;
                    }

                    var ack = new MoveToMsg
                    {
                        ObjectId = claims.CharacterId,
                        DestX = move.DestX,
                        DestY = move.DestY,
                        DestZ = move.DestZ,
                        OriginX = x,
                        OriginY = y,
                        OriginZ = z
                    };
                    x = move.DestX;
                    y = move.DestY;
                    z = move.DestZ;
                    ZoneLog.Info($"RX Move session={sessionId} → {x},{y},{z}");
                    await SendAsync(stream, MsgType.MoveToAck, ack, ct).ConfigureAwait(false);
                    break;
                }
                default:
                    ZoneLog.Info($"RX unknown type={frame.Value.Type} session={sessionId}");
                    await ErrorAsync(stream, "unknown", $"msgType={frame.Value.Type}", ct).ConfigureAwait(false);
                    break;
            }
        }
    }

    static Task ErrorAsync(Stream stream, string code, string message, CancellationToken ct)
    {
        ZoneLog.Info($"TX Error code={code}");
        return SendAsync(stream, MsgType.Error, new ErrorMsg { Code = code, Message = message }, ct);
    }

    static async Task SendAsync<T>(Stream stream, MsgType type, T msg, CancellationToken ct)
    {
        byte[] body = MessagePackSerializer.Serialize(msg);
        await FrameIO.SendAsync(stream, (ushort)type, body, ct).ConfigureAwait(false);
    }

    static T? Decode<T>(byte[] body) where T : class
    {
        if (body.Length == 0)
            return null;
        try
        {
            return MessagePackSerializer.Deserialize<T>(body);
        }
        catch (MessagePackSerializationException)
        {
            return null;
        }
    }
}

static class ZoneLog
{
    public static void Info(string message) => Console.WriteLine($"[ZONE] {message}");
}
