using Fineage.Login;
using Fineage.Protocol;
using Fineage.Protocol.Net;
using Fineage.Zone.World;
using MessagePack;

namespace Fineage.Zone;

public sealed class ZoneServer
{
    readonly HandoffTokens _tokens;
    readonly ZoneWorld _world;
    readonly Func<DateTimeOffset> _clock;
    readonly int _maxMessagesPerSecond;
    int _nextSession = 1;

    public ZoneServer(HandoffTokens tokens, Func<DateTimeOffset>? clock = null)
        : this(tokens, ZoneAoiSettings.CreateWorld(), clock)
    {
    }

    public ZoneServer(HandoffTokens tokens, ZoneWorld world, Func<DateTimeOffset>? clock = null)
        : this(tokens, world, clock, null)
    {
    }

    public ZoneServer(HandoffTokens tokens, ZoneWorld world, Func<DateTimeOffset>? clock, ZoneServerOptions? options)
    {
        _tokens = tokens;
        _world = world;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _maxMessagesPerSecond = options?.MaxMessagesPerSecond ?? ZoneNetSettings.MaxMessagesPerSecond;
    }

    public async Task HandleAsync(Stream stream, CancellationToken ct)
    {
        var session = new ZoneSession(stream);
        int sessionId = Interlocked.Increment(ref _nextSession);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var frame = await FrameIO.ReadAsync(stream, ct).ConfigureAwait(false);
                if (frame == null)
                    return;

                await DispatchAsync(session, sessionId, frame.Value.Type, frame.Value.Body, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            await _world.LeaveAsync(session, CancellationToken.None).ConfigureAwait(false);
        }
    }

    public async Task HandleAsync(PipelineConnection connection, CancellationToken ct)
    {
        var session = new ZoneSession(connection);
        int sessionId = Interlocked.Increment(ref _nextSession);
        var rate = new MessageRateGate(_maxMessagesPerSecond);
        using var idleWatch = new SessionIdleWatchdog(ct, TcpSessionSettings.IdleTimeout);
        CancellationToken readCt = idleWatch.Token;
        try
        {
            while (!readCt.IsCancellationRequested)
            {
                var frame = await PipeFrames.ReadV2Async(connection.Reader, readCt).ConfigureAwait(false);
                if (frame == null)
                    return;

                PipelineTelemetry.FrameIn("zone");
                idleWatch.Touch();

                if (!rate.TryConsume())
                {
                    await ErrorAsync(session, "rate_limit", "Too many messages per second.", readCt).ConfigureAwait(false);
                    return;
                }

                await DispatchAsync(session, sessionId, frame.Value.Type, frame.Value.Body, readCt).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (idleWatch.Token.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            await ErrorAsync(session, "idle_timeout", "Session timed out.", CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            await _world.LeaveAsync(session, CancellationToken.None).ConfigureAwait(false);
        }
    }

    async Task DispatchAsync(ZoneSession session, int sessionId, ushort type, byte[] body, CancellationToken ct)
    {
        if (ZoneWireBoundary.IsLoginClientOpcode(type))
        {
            ZoneLog.Info($"RX login opcode on zone type={type} session={sessionId}");
            await ErrorAsync(session, "wrong_port",
                "That message belongs on the login port, not the zone port.", ct).ConfigureAwait(false);
            return;
        }

        switch ((MsgType)type)
        {
            case MsgType.Hello:
            {
                var hello = Decode<HelloMsg>(body) ?? new HelloMsg();
                ZoneLog.Info($"RX Hello session={sessionId} proto={hello.ProtocolVersion} build={hello.ClientBuild}");
                await session.SendAsync(MsgType.HelloAck, new HelloAckMsg
                {
                    ProtocolVersion = 1,
                    ServerName = "FineageZone"
                }, ct).ConfigureAwait(false);
                break;
            }
            case MsgType.Ping:
            {
                var ping = Decode<PingMsg>(body) ?? new PingMsg();
                long now = _clock().ToUnixTimeMilliseconds();
                await session.SendAsync(MsgType.Pong, new PongMsg { ClientUnixMs = ping.ClientUnixMs, ServerUnixMs = now }, ct)
                    .ConfigureAwait(false);
                break;
            }
            case MsgType.Auth:
            {
                if (session.Bound)
                {
                    ZoneLog.Info($"RX Auth rejected session={sessionId} reason=already_authenticated");
                    await ErrorAsync(session, "already_authenticated",
                        "This session already completed zone Auth.", ct).ConfigureAwait(false);
                    break;
                }

                var auth = Decode<AuthMsg>(body);
                if (auth == null || string.IsNullOrWhiteSpace(auth.Account))
                {
                    await ErrorAsync(session, "bad_request", "Auth account is required.", ct).ConfigureAwait(false);
                    break;
                }

                var status = _tokens.Validate(auth.Password, _clock(), out var parsed);
                if (status == HandoffTokens.ValidateStatus.Expired)
                {
                    ZoneLog.Info($"RX Auth rejected session={sessionId} account={auth.Account} reason=expired");
                    await _world.LeaveAsync(session, ct).ConfigureAwait(false);
                    session.Clear();
                    await ErrorAsync(session, "handoff_expired", "Handoff token has expired.", ct).ConfigureAwait(false);
                    break;
                }

                if (status != HandoffTokens.ValidateStatus.Ok ||
                    !string.Equals(parsed.Account, auth.Account, StringComparison.OrdinalIgnoreCase))
                {
                    ZoneLog.Info($"RX Auth rejected session={sessionId} account={auth.Account}");
                    await _world.LeaveAsync(session, ct).ConfigureAwait(false);
                    session.Clear();
                    await ErrorAsync(session, "invalid_credentials", "Handoff token was rejected.", ct).ConfigureAwait(false);
                    break;
                }

                await _world.LeaveAsync(session, ct).ConfigureAwait(false);
                session.Bind(parsed.CharacterId, parsed.Account, parsed.CharacterName, parsed.ClassId);
                ZoneLog.Info($"RX Auth session={sessionId} account={session.Account} character={session.Name}");
                await session.SendAsync(MsgType.AuthOk, new AuthOkMsg
                {
                    SessionId = sessionId,
                    CharacterName = session.Name
                }, ct).ConfigureAwait(false);
                break;
            }
            case MsgType.EnterWorld:
            {
                if (!session.Bound)
                {
                    await ErrorAsync(session, "auth_required", "Auth with a handoff token first.", ct).ConfigureAwait(false);
                    break;
                }

                if (!await _world.TryEnterAsync(session, ct).ConfigureAwait(false))
                {
                    await ErrorAsync(session, "object_id", "That character id belongs to an NPC.", ct).ConfigureAwait(false);
                    break;
                }

                break;
            }
            case MsgType.MoveTo:
            {
                var move = Decode<MoveToMsg>(body);
                if (move == null)
                {
                    await ErrorAsync(session, "bad_request", "MoveTo body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryMoveAsync(session, move, ct).ConfigureAwait(false);
                if (code == "range")
                    await ErrorAsync(session, "range", "Destination is too far.", ct).ConfigureAwait(false);
                else if (code != null)
                    await ErrorAsync(session, "world", "EnterWorld first.", ct).ConfigureAwait(false);
                else
                    ZoneLog.Info($"RX Move session={sessionId} → {session.X},{session.Y},{session.Z}");
                break;
            }
            case MsgType.SelectTarget:
            {
                var select = Decode<SelectTargetMsg>(body);
                if (select == null)
                {
                    await ErrorAsync(session, "bad_request", "SelectTarget body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TrySelectTargetAsync(session, select, ct).ConfigureAwait(false);
                if (code == "world")
                    await ErrorAsync(session, "world", "EnterWorld first.", ct).ConfigureAwait(false);
                else if (code == "target")
                    await ErrorAsync(session, "target", "Invalid target.", ct).ConfigureAwait(false);
                else
                    ZoneLog.Info($"RX SelectTarget session={sessionId} → {select.TargetObjectId}");
                break;
            }
            case MsgType.AttackRequest:
            {
                var attack = Decode<AttackRequestMsg>(body) ?? new AttackRequestMsg();
                string? code = await _world.TryAttackAsync(session, attack, ct).ConfigureAwait(false);
                if (code == "world")
                    await ErrorAsync(session, "world", "EnterWorld first.", ct).ConfigureAwait(false);
                else if (code != null)
                    await ErrorAsync(session, "attack", "Attack failed.", ct).ConfigureAwait(false);
                else
                    ZoneLog.Info($"RX AttackRequest session={sessionId} target={session.SelectedTargetId}");
                break;
            }
            case MsgType.Say:
            {
                var say = Decode<SayMsg>(body);
                if (say == null)
                {
                    await ErrorAsync(session, "bad_request", "Say body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TrySayAsync(session, say, ct).ConfigureAwait(false);
                if (code == "world")
                    await ErrorAsync(session, "world", "EnterWorld first.", ct).ConfigureAwait(false);
                else if (code != null)
                    await ErrorAsync(session, "bad_request", "Say text was rejected.", ct).ConfigureAwait(false);
                break;
            }
            case MsgType.GrantItemRequest:
            {
                var grant = Decode<GrantItemRequestMsg>(body);
                if (grant == null)
                {
                    await ErrorAsync(session, "bad_request", "GrantItemRequest body was not readable.", ct)
                        .ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryGrantItemAsync(session, grant, ct).ConfigureAwait(false);
                if (code == "world")
                    await ErrorAsync(session, "world", "EnterWorld first.", ct).ConfigureAwait(false);
                else if (code != null)
                    await ErrorAsync(session, "bad_request", "Grant was rejected.", ct).ConfigureAwait(false);
                else
                    ZoneLog.Info($"RX GrantItem session={sessionId} item={grant.ItemId} count={grant.Count}");
                break;
            }
            default:
                ZoneLog.Info($"RX unknown type={type} session={sessionId}");
                await ErrorAsync(session, "unknown", $"msgType={type}", ct).ConfigureAwait(false);
                break;
        }
    }

    static Task ErrorAsync(ZoneSession session, string code, string message, CancellationToken ct)
    {
        ZoneLog.Info($"TX Error code={code}");
        return session.SendAsync(MsgType.Error, new ErrorMsg { Code = code, Message = message }, ct);
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
