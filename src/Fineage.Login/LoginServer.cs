using Fineage.Protocol;
using Fineage.Protocol.Net;
using MessagePack;

namespace Fineage.Login;

public sealed class LoginServer
{
    readonly LoginService _service;
    readonly int _maxMessagesPerSecond;
    readonly Func<DateTimeOffset> _clock;

    public LoginServer(LoginService service)
        : this(service, null, null)
    {
    }

    public LoginServer(LoginService service, LoginServerOptions? options, Func<DateTimeOffset>? clock = null)
    {
        _service = service;
        _maxMessagesPerSecond = options?.MaxMessagesPerSecond ?? LoginNetSettings.MaxMessagesPerSecond;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public Task HandleAsync(Stream stream, CancellationToken ct) =>
        RunSessionAsync(new LoginSession(stream), ct, rateLimit: false, idle: false);

    public Task HandleAsync(PipelineConnection connection, CancellationToken ct) =>
        RunSessionAsync(new LoginSession(connection), ct, rateLimit: true, idle: true);

    async Task RunSessionAsync(LoginSession session, CancellationToken ct, bool rateLimit, bool idle)
    {
        using var idleWatch = idle
            ? new SessionIdleWatchdog(ct, TcpSessionSettings.IdleTimeout)
            : null;
        CancellationToken readCt = idleWatch?.Token ?? ct;
        MessageRateGate? rate = rateLimit ? new MessageRateGate(_maxMessagesPerSecond) : null;

        string? account = null;
        try
        {
            while (!readCt.IsCancellationRequested)
            {
                var frame = await session.ReadAsync(readCt).ConfigureAwait(false);
                if (frame == null)
                    return;

                idleWatch?.Touch();

                if (rate != null && !rate.TryConsume())
                {
                    await SendErrorAsync(session, "rate_limit", "Too many messages per second.", readCt).ConfigureAwait(false);
                    return;
                }

                account = await DispatchAsync(session, account, frame.Value.Type, frame.Value.Body, readCt).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (idleWatch != null && idleWatch.Token.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            await SendErrorAsync(session, "idle_timeout", "Session timed out.", CancellationToken.None).ConfigureAwait(false);
        }
    }

    async Task<string?> DispatchAsync(LoginSession session, string? account, ushort type, byte[] body, CancellationToken ct)
    {
        switch ((MsgType)type)
        {
            case MsgType.Hello:
                {
                    var hello = Decode<HelloMsg>(body) ?? new HelloMsg();
                    LoginLog.Info($"RX Hello proto={hello.ProtocolVersion} build={hello.ClientBuild}");
                    await session.SendAsync(MsgType.HelloAck, new HelloAckMsg
                    {
                        ProtocolVersion = 1,
                        ServerName = "FineageLogin"
                    }, ct).ConfigureAwait(false);
                    return account;
                }
            case MsgType.Ping:
                {
                    await ReplyPingAsync(session, body, ct).ConfigureAwait(false);
                    return account;
                }
            case MsgType.Register:
                {
                    var msg = Decode<RegisterMsg>(body);
                    if (msg == null)
                    {
                        await SendErrorAsync(session, "bad_request", "Register body was not readable.", ct).ConfigureAwait(false);
                        return account;
                    }

                    LoginLog.Info($"RX Register account={msg.Account}");
                    var result = _service.Register(msg.Account, msg.Password);
                    if (!result.Ok || result.Value == null)
                    {
                        await SendErrorAsync(session, result.Code, result.Message, ct).ConfigureAwait(false);
                        return account;
                    }

                    await session.SendAsync(MsgType.RegisterOk, new RegisterOkMsg { Account = result.Value }, ct)
                        .ConfigureAwait(false);
                    return result.Value;
                }
            case MsgType.Auth:
                {
                    var msg = Decode<AuthMsg>(body);
                    if (msg == null)
                    {
                        await SendErrorAsync(session, "bad_request", "Auth body was not readable.", ct).ConfigureAwait(false);
                        return account;
                    }

                    LoginLog.Info($"RX Auth account={msg.Account}");
                    var result = _service.Login(msg.Account, msg.Password);
                    if (!result.Ok || result.Value == null)
                    {
                        await SendErrorAsync(session, result.Code, result.Message, ct).ConfigureAwait(false);
                        return account;
                    }

                    await session.SendAsync(MsgType.LoginOk, new LoginOkMsg { Account = result.Value }, ct)
                        .ConfigureAwait(false);
                    return result.Value;
                }
            case MsgType.CreateCharacter:
                {
                    if (account == null)
                    {
                        await SendErrorAsync(session, "auth_required", "Login required.", ct).ConfigureAwait(false);
                        return null;
                    }

                    var msg = Decode<CreateCharacterMsg>(body);
                    if (msg == null)
                    {
                        await SendErrorAsync(session, "bad_request", "CreateCharacter body was not readable.", ct).ConfigureAwait(false);
                        return account;
                    }

                    LoginLog.Info($"RX CreateCharacter account={account} name={msg.Name} class={msg.ClassId}");
                    var result = _service.CreateHuman(account, msg.Name, msg.ClassId, msg.Race, msg.Sex);
                    if (!result.Ok || result.Value == null)
                    {
                        await SendErrorAsync(session, result.Code, result.Message, ct).ConfigureAwait(false);
                        return account;
                    }

                    var created = result.Value;
                    await session.SendAsync(MsgType.CharacterCreated, new CharacterCreatedMsg
                    {
                        CharacterId = created.Id,
                        Name = created.Name,
                        ClassId = created.ClassId,
                        Race = created.Race,
                        Sex = created.Sex
                    }, ct).ConfigureAwait(false);
                    return account;
                }
            case MsgType.ListCharacters:
                {
                    if (account == null)
                    {
                        await SendErrorAsync(session, "auth_required", "Login required.", ct).ConfigureAwait(false);
                        return null;
                    }

                    var result = _service.List(account);
                    if (!result.Ok || result.Value == null)
                    {
                        await SendErrorAsync(session, result.Code, result.Message, ct).ConfigureAwait(false);
                        return account;
                    }

                    await session.SendAsync(MsgType.CharacterList, new CharacterListMsg
                    {
                        Characters = result.Value.Select(c => new CharacterSummaryMsg
                        {
                            CharacterId = c.Id,
                            Name = c.Name,
                            ClassId = c.ClassId,
                            Race = c.Race,
                            Sex = c.Sex,
                            Level = c.Level
                        }).ToArray()
                    }, ct).ConfigureAwait(false);
                    return account;
                }
            case MsgType.SelectCharacter:
                {
                    if (account == null)
                    {
                        await SendErrorAsync(session, "auth_required", "Login required.", ct).ConfigureAwait(false);
                        return null;
                    }

                    var msg = Decode<SelectCharacterMsg>(body);
                    if (msg == null)
                    {
                        await SendErrorAsync(session, "bad_request", "SelectCharacter body was not readable.", ct).ConfigureAwait(false);
                        return account;
                    }

                    LoginLog.Info($"RX SelectCharacter account={account} id={msg.CharacterId} name={msg.Name}");
                    var result = _service.Select(account, msg.CharacterId, msg.Name);
                    if (!result.Ok || result.Value == null)
                    {
                        await SendErrorAsync(session, result.Code, result.Message, ct).ConfigureAwait(false);
                        return account;
                    }

                    await SendHandoffAsync(session, result.Value, ct).ConfigureAwait(false);
                    return account;
                }
            default:
                LoginLog.Info($"RX unknown type={type}");
                await SendErrorAsync(session, "unknown", $"msgType={type}", ct).ConfigureAwait(false);
                return account;
        }
    }

    Task ReplyPingAsync(LoginSession session, byte[] body, CancellationToken ct)
    {
        var ping = Decode<PingMsg>(body) ?? new PingMsg();
        long now = _clock().ToUnixTimeMilliseconds();
        return session.SendAsync(MsgType.Pong, new PongMsg { ClientUnixMs = ping.ClientUnixMs, ServerUnixMs = now }, ct);
    }

    static async Task SendHandoffAsync(LoginSession session, HandoffTicket ticket, CancellationToken ct)
    {
        LoginLog.Info($"TX Handoff account={ticket.Account} character={ticket.CharacterName} zone={ticket.ZoneHost}:{ticket.ZonePort}");
        await session.SendAsync(MsgType.Handoff, new HandoffMsg
        {
            Account = ticket.Account,
            CharacterName = ticket.CharacterName,
            CharacterId = ticket.CharacterId,
            ClassId = ticket.ClassId,
            Token = ticket.Token,
            ZoneHost = ticket.ZoneHost,
            ZonePort = ticket.ZonePort,
            ExpiresUnixSeconds = ticket.ExpiresUnixSeconds
        }, ct).ConfigureAwait(false);
    }

    static Task SendErrorAsync(LoginSession session, string code, string message, CancellationToken ct)
    {
        LoginLog.Info($"TX Error code={code}");
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

static class LoginLog
{
    public static void Info(string message) => Console.WriteLine($"[LOGIN] {message}");
}
