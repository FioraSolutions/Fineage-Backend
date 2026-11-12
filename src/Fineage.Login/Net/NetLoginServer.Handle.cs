using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Accounts;

namespace Fineage.Login.Net;

public sealed partial class NetLoginServer
{
    public async Task HandleAsync(NetConnection conn, CancellationToken ct)
    {
        string? account = null;

        // Same guards the retired V2 lobby listener had: an idle or flooding client loses the slot.
        using var idle = new SessionIdleWatchdog(ct, _idleTimeout);
        var rate = new MessageRateGate(_maxMessagesPerSecond);
        CancellationToken readCt = idle.Token;

        await foreach (var envelope in conn.Inbox.ReadAllAsync(readCt).ConfigureAwait(false))
        {
            if (!rate.TryConsume())
            {
                await RejectAsync(conn, "rate_limit", "Too many messages per second.", readCt).ConfigureAwait(false);
                break;
            }

            idle.Touch();
            try
            {
                switch (envelope.Opcode)
                {
                    case NetOpcode.Hello:
                        await OnHelloAsync(conn, envelope, ct).ConfigureAwait(false);
                        break;
                    case NetOpcode.Auth:
                        account = await OnAuthAsync(conn, envelope, ct).ConfigureAwait(false) ?? account;
                        break;
                    case NetOpcode.ListCharacters:
                        await OnListAsync(conn, account, ct).ConfigureAwait(false);
                        break;
                    case NetOpcode.CreateCharacter:
                        await OnCreateAsync(conn, account, envelope, ct).ConfigureAwait(false);
                        break;
                    case NetOpcode.SelectCharacter:
                        await OnSelectAsync(conn, account, envelope, ct).ConfigureAwait(false);
                        break;
                    case NetOpcode.DeleteCharacter:
                        await OnDeleteAsync(conn, account, envelope, ct).ConfigureAwait(false);
                        break;
                    case NetOpcode.RestoreCharacter:
                        await OnRestoreAsync(conn, account, envelope, ct).ConfigureAwait(false);
                        break;
                    default:
                        await RejectAsync(conn, "unknown_opcode", $"opcode 0x{envelope.Opcode:X4}", ct)
                            .ConfigureAwait(false);
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                FineageLogging.Write(
                    "Fineage.Login",
                    "Information",
                    "log",
                    ("message", $"handler_error opcode=0x{envelope.Opcode:X4}: {ex}"));
                await RejectAsync(conn, "handler_error", "An internal error occurred.", ct).ConfigureAwait(false);
            }
        }
    }

    async Task OnHelloAsync(NetConnection conn, PacketEnvelope envelope, CancellationToken ct)
    {
        var hello = NetCodec.DecodeHello(envelope.Payload);
        uint granted = ProtocolCapabilities.FeatureSession | ProtocolCapabilities.FeatureLobby;
        conn.HelloCompleted = true;
        conn.FeatureFlags = granted & hello.FeatureFlags;
        if (conn.FeatureFlags == 0)
            conn.FeatureFlags = granted;

        await conn.SendAsync(
            NetOpcode.HelloAck,
            NetCodec.EncodeHelloAck(new HelloAckMsg
            {
                Major = PacketEnvelope.ProtocolMajor,
                Minor = PacketEnvelope.ProtocolMinor,
                FeatureFlags = conn.FeatureFlags,
                ServerName = "fineage-login-net"
            }),
            ct: ct).ConfigureAwait(false);
    }

    async Task<string?> OnAuthAsync(NetConnection conn, PacketEnvelope envelope, CancellationToken ct)
    {
        RequireHello(conn);
        var auth = NetCodec.DecodeAuth(envelope.Payload);
        LoginResult<string> result;
        if (auth.RegisterIfMissing && !_service.AccountExists(auth.Account))
            result = _service.Register(auth.Account, auth.Password);
        else
            result = _service.Login(auth.Account, auth.Password);

        await conn.SendAsync(
            NetOpcode.AuthResult,
            NetCodec.EncodeAuthResult(new AuthResultMsg
            {
                Ok = result.Ok,
                Code = result.Code,
                Message = result.Message,
                Account = result.Ok ? result.Value! : auth.Account
            }),
            envelope.RequestSequence,
            ct).ConfigureAwait(false);

        return result.Ok ? result.Value : null;
    }

    async Task OnListAsync(NetConnection conn, string? account, CancellationToken ct)
    {
        RequireHello(conn);
        if (account == null)
        {
            await RejectAsync(conn, "auth_required", "Login required.", ct).ConfigureAwait(false);
            return;
        }

        var list = _service.List(account);
        if (!list.Ok)
        {
            await RejectAsync(conn, list.Code, list.Message, ct).ConfigureAwait(false);
            return;
        }

        await conn.SendAsync(
            NetOpcode.CharacterRoster,
            NetCodec.EncodeCharacterRoster(ToRoster(list.Value!)),
            ct: ct).ConfigureAwait(false);
    }

    static void RequireHello(NetConnection conn)
    {
        if (!conn.HelloCompleted)
            throw new InvalidOperationException("hello_required");
    }

    static Task RejectAsync(NetConnection conn, string code, string message, CancellationToken ct)
    {
        NetTelemetry.PacketRejected("login");
        return conn.SendAsync(NetOpcode.Reject, NetCodec.EncodeReject(new RejectMsg { Code = code, Message = message }), ct: ct);
    }
}
