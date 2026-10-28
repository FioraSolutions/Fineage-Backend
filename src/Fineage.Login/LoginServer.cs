using Fineage.Protocol;
using Fineage.Protocol.Net;
using MessagePack;
using V2 = Fineage.Protocol.V2;

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

                if (rateLimit)
                    PipelineTelemetry.FrameIn("login");

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
        if (LoginWireBoundary.IsZoneClientOpcode(type) && !TryGetV2Opcode(type, out _))
        {
            LoginLog.Info($"RX zone opcode on login type={type}");
            await SendErrorAsync(session, "wrong_endpoint",
                "That message belongs on the zone port, not the login port.", ct).ConfigureAwait(false);
            return account;
        }

        if (TryGetV2Opcode(type, out var v2))
        {
            return v2 switch
            {
                Fineage.Protocol.V2.MsgType.ListCharacters => await HandleV2ListAsync(session, account, ct).ConfigureAwait(false),
                Fineage.Protocol.V2.MsgType.CreateCharacter => await HandleV2CreateAsync(session, account, body, ct).ConfigureAwait(false),
                Fineage.Protocol.V2.MsgType.EnterWorld => await HandleV2EnterWorldAsync(session, account, body, ct).ConfigureAwait(false),
                Fineage.Protocol.V2.MsgType.RequestServerList => await HandleV2ServerListAsync(session, account, ct).ConfigureAwait(false),
                Fineage.Protocol.V2.MsgType.SelectServer => await HandleV2SelectServerAsync(session, account, body, ct).ConfigureAwait(false),
                Fineage.Protocol.V2.MsgType.AuthToken => await HandleV2AuthTokenStubAsync(session, ct).ConfigureAwait(false),
                _ => account
            };
        }

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

    static bool TryGetV2Opcode(ushort type, out V2.MsgType opcode)
    {
        opcode = (V2.MsgType)type;
        return type switch
        {
            3 or 5 or 6 or 7 or 10 or 11 => true,
            _ => false
        };
    }

    async Task<string?> HandleV2ListAsync(LoginSession session, string? account, CancellationToken ct)
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

        LoginLog.Info($"RX V2 ListCharacters account={account} count={result.Value.Count}");
        byte[] body = MessagePackSerializer.Serialize(LoginV2Wire.ToRoster(result.Value));
        await session.SendFrameAsync((ushort)V2.MsgType.CharacterRoster, body, ct).ConfigureAwait(false);
        return account;
    }

    async Task<string?> HandleV2CreateAsync(LoginSession session, string? account, byte[] body, CancellationToken ct)
    {
        if (account == null)
        {
            await SendErrorAsync(session, "auth_required", "Login required.", ct).ConfigureAwait(false);
            return null;
        }

        V2.CreateCharacterMsg? msg = DecodeV2<V2.CreateCharacterMsg>(body);
        if (msg == null)
        {
            await SendErrorAsync(session, "bad_request", "CreateCharacter body was not readable.", ct).ConfigureAwait(false);
            return account;
        }

        LoginLog.Info($"RX V2 CreateCharacter account={account} name={msg.Name} class={msg.ClassId}");
        var result = _service.CreateHuman(account, msg.Name, msg.ClassId, msg.Race, msg.Sex);
        V2.CreateCharacterResultMsg response;
        if (!result.Ok || result.Value == null)
        {
            response = LoginV2Wire.CreateFailed(result.Code, result.Message);
            await SendFrameV2Async(session, V2.MsgType.CreateCharacterResult, response, ct).ConfigureAwait(false);
            return account;
        }

        int slot = 0;
        var listAfter = _service.List(account);
        if (listAfter.Ok && listAfter.Value != null)
        {
            for (int i = 0; i < listAfter.Value.Count; i++)
            {
                if (listAfter.Value[i].Id == result.Value.Id)
                {
                    slot = i;
                    break;
                }
            }
        }

        response = LoginV2Wire.ToCreateResult(result.Value, slot);
        await SendFrameV2Async(session, V2.MsgType.CreateCharacterResult, response, ct).ConfigureAwait(false);
        return account;
    }

    async Task<string?> HandleV2EnterWorldAsync(LoginSession session, string? account, byte[] body, CancellationToken ct)
    {
        if (account == null)
        {
            await SendErrorAsync(session, "auth_required", "Login required.", ct).ConfigureAwait(false);
            return null;
        }

        var enter = Decode<EnterWorldMsg>(body) ?? new EnterWorldMsg();
        var roster = _service.List(account);
        if (!roster.Ok || roster.Value == null)
        {
            await SendErrorAsync(session, roster.Code, roster.Message, ct).ConfigureAwait(false);
            return account;
        }

        if (enter.CharacterSlot < 0 || enter.CharacterSlot >= roster.Value.Count)
        {
            await SendErrorAsync(session, "bad_slot", "Character slot is out of range.", ct).ConfigureAwait(false);
            return account;
        }

        var character = roster.Value[enter.CharacterSlot];
        var ticket = _service.Select(account, character.Id);
        if (!ticket.Ok || ticket.Value == null)
        {
            await SendErrorAsync(session, ticket.Code, ticket.Message, ct).ConfigureAwait(false);
            return account;
        }

        LoginLog.Info($"RX V2 EnterWorld account={account} slot={enter.CharacterSlot} character={character.Name}");
        await SendFrameV2Async(session, V2.MsgType.PlayToken, LoginV2Wire.ToPlayToken(ticket.Value), ct).ConfigureAwait(false);
        await SendHandoffAsync(session, ticket.Value, ct).ConfigureAwait(false);
        return account;
    }

    async Task<string?> HandleV2ServerListAsync(LoginSession session, string? account, CancellationToken ct)
    {
        LoginLog.Info("RX V2 RequestServerList");
        await SendFrameV2Async(session, V2.MsgType.ServerList, LoginV2Wire.DefaultServerList(), ct).ConfigureAwait(false);
        return account;
    }

    async Task<string?> HandleV2SelectServerAsync(LoginSession session, string? account, byte[] body, CancellationToken ct)
    {
        var pick = DecodeV2<V2.SelectServerMsg>(body);
        if (pick == null)
        {
            await SendErrorAsync(session, "bad_request", "SelectServer body was not readable.", ct).ConfigureAwait(false);
            return account;
        }

        if (pick.ServerId != 1)
        {
            await SendErrorAsync(session, "server_not_found", "Only server id 1 is available.", ct).ConfigureAwait(false);
            return account;
        }

        if (account == null)
        {
            await SendErrorAsync(session, "auth_required", "Login required.", ct).ConfigureAwait(false);
            return null;
        }

        LoginLog.Info($"RX V2 SelectServer account={account} server={pick.ServerId}");
        await SendErrorAsync(session, "character_required",
            "Send EnterWorld (3) with a character slot after choosing a character.", ct).ConfigureAwait(false);
        return account;
    }

    async Task<string?> HandleV2AuthTokenStubAsync(LoginSession session, CancellationToken ct)
    {
        await SendErrorAsync(session, "not_implemented",
            "AuthToken is not wired; use Auth (2) then EnterWorld (3) with a character slot.", ct).ConfigureAwait(false);
        return null;
    }

    Task SendFrameV2Async<T>(LoginSession session, V2.MsgType type, T message, CancellationToken ct)
    {
        byte[] body = MessagePackSerializer.Serialize(message);
        return session.SendFrameAsync((ushort)type, body, ct);
    }

    static T? DecodeV2<T>(byte[] body) where T : class => Decode<T>(body);

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
