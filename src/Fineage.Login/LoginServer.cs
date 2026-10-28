using Fineage.Protocol;
using MessagePack;

namespace Fineage.Login;

public sealed class LoginServer
{
    readonly LoginService _service;

    public LoginServer(LoginService service) => _service = service;

    public async Task HandleAsync(Stream stream, CancellationToken ct)
    {
        string? account = null;
        while (!ct.IsCancellationRequested)
        {
            var frame = await FrameIO.ReadAsync(stream, ct).ConfigureAwait(false);
            if (frame == null)
                return;
            account = await DispatchAsync(stream, account, frame.Value.Type, frame.Value.Body, ct).ConfigureAwait(false);
        }
    }

    async Task<string?> DispatchAsync(Stream stream, string? account, ushort type, byte[] body, CancellationToken ct)
    {
        switch ((MsgType)type)
        {
            case MsgType.Hello:
                {
                    var hello = Decode<HelloMsg>(body) ?? new HelloMsg();
                    LoginLog.Info($"RX Hello proto={hello.ProtocolVersion} build={hello.ClientBuild}");
                    await SendAsync(stream, MsgType.HelloAck, new HelloAckMsg
                    {
                        ProtocolVersion = 1,
                        ServerName = "FineageLogin"
                    }, ct).ConfigureAwait(false);
                    return account;
                }
            case MsgType.Register:
                {
                    var msg = Decode<RegisterMsg>(body);
                    if (msg == null)
                    {
                        await SendErrorAsync(stream, "bad_request", "Register body was not readable.", ct).ConfigureAwait(false);
                        return account;
                    }

                    LoginLog.Info($"RX Register account={msg.Account}");
                    var result = _service.Register(msg.Account, msg.Password);
                    if (!result.Ok || result.Value == null)
                    {
                        await SendErrorAsync(stream, result.Code, result.Message, ct).ConfigureAwait(false);
                        return account;
                    }

                    await SendAsync(stream, MsgType.RegisterOk, new RegisterOkMsg { Account = result.Value }, ct)
                        .ConfigureAwait(false);
                    return result.Value;
                }
            case MsgType.Auth:
                {
                    var msg = Decode<AuthMsg>(body);
                    if (msg == null)
                    {
                        await SendErrorAsync(stream, "bad_request", "Auth body was not readable.", ct).ConfigureAwait(false);
                        return account;
                    }

                    LoginLog.Info($"RX Auth account={msg.Account}");
                    var result = _service.Login(msg.Account, msg.Password);
                    if (!result.Ok || result.Value == null)
                    {
                        await SendErrorAsync(stream, result.Code, result.Message, ct).ConfigureAwait(false);
                        return account;
                    }

                    await SendAsync(stream, MsgType.LoginOk, new LoginOkMsg { Account = result.Value }, ct)
                        .ConfigureAwait(false);
                    return result.Value;
                }
            case MsgType.CreateCharacter:
                {
                    if (account == null)
                    {
                        await SendErrorAsync(stream, "auth_required", "Login required.", ct).ConfigureAwait(false);
                        return null;
                    }

                    var msg = Decode<CreateCharacterMsg>(body);
                    if (msg == null)
                    {
                        await SendErrorAsync(stream, "bad_request", "CreateCharacter body was not readable.", ct).ConfigureAwait(false);
                        return account;
                    }

                    LoginLog.Info($"RX CreateCharacter account={account} name={msg.Name} class={msg.ClassId}");
                    var result = _service.CreateHuman(account, msg.Name, msg.ClassId, msg.Race, msg.Sex);
                    if (!result.Ok || result.Value == null)
                    {
                        await SendErrorAsync(stream, result.Code, result.Message, ct).ConfigureAwait(false);
                        return account;
                    }

                    var created = result.Value;
                    await SendAsync(stream, MsgType.CharacterCreated, new CharacterCreatedMsg
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
                        await SendErrorAsync(stream, "auth_required", "Login required.", ct).ConfigureAwait(false);
                        return null;
                    }

                    var result = _service.List(account);
                    if (!result.Ok || result.Value == null)
                    {
                        await SendErrorAsync(stream, result.Code, result.Message, ct).ConfigureAwait(false);
                        return account;
                    }

                    await SendAsync(stream, MsgType.CharacterList, new CharacterListMsg
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
                        await SendErrorAsync(stream, "auth_required", "Login required.", ct).ConfigureAwait(false);
                        return null;
                    }

                    var msg = Decode<SelectCharacterMsg>(body);
                    if (msg == null)
                    {
                        await SendErrorAsync(stream, "bad_request", "SelectCharacter body was not readable.", ct).ConfigureAwait(false);
                        return account;
                    }

                    LoginLog.Info($"RX SelectCharacter account={account} id={msg.CharacterId} name={msg.Name}");
                    var result = _service.Select(account, msg.CharacterId, msg.Name);
                    if (!result.Ok || result.Value == null)
                    {
                        await SendErrorAsync(stream, result.Code, result.Message, ct).ConfigureAwait(false);
                        return account;
                    }

                    await SendHandoffAsync(stream, result.Value, ct).ConfigureAwait(false);
                    return account;
                }
            default:
                LoginLog.Info($"RX unknown type={type}");
                await SendErrorAsync(stream, "unknown", $"msgType={type}", ct).ConfigureAwait(false);
                return account;
        }
    }

    static async Task SendHandoffAsync(Stream stream, HandoffTicket ticket, CancellationToken ct)
    {
        LoginLog.Info($"TX Handoff account={ticket.Account} character={ticket.CharacterName} zone={ticket.ZoneHost}:{ticket.ZonePort}");
        await SendAsync(stream, MsgType.Handoff, new HandoffMsg
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

    static Task SendErrorAsync(Stream stream, string code, string message, CancellationToken ct)
    {
        LoginLog.Info($"TX Error code={code}");
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

static class LoginLog
{
    public static void Info(string message) => Console.WriteLine($"[LOGIN] {message}");
}
