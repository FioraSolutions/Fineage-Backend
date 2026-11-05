using Fineage.Net.Contract;
using Fineage.Net.Host;

namespace Fineage.Login.Net;

/// <summary>Fineage Net login path — calls <see cref="LoginService"/> directly, no V2/Interlude wire.</summary>
public sealed class NetLoginServer
{
    readonly LoginService _service;
    readonly Func<DateTimeOffset> _clock;
    readonly string _zoneHost;
    readonly int _zonePort;

    public NetLoginServer(
        LoginService service,
        string? zoneHost = null,
        int? zonePort = null,
        Func<DateTimeOffset>? clock = null)
    {
        _service = service;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _zoneHost = string.IsNullOrWhiteSpace(zoneHost) ? "127.0.0.1" : zoneHost.Trim();
        _zonePort = zonePort is > 0 and <= 65535 ? zonePort.Value : NetSettings.ZonePort;
    }

    public async Task HandleAsync(NetConnection conn, CancellationToken ct)
    {
        string? account = null;
        await foreach (var envelope in conn.Inbox.ReadAllAsync(ct).ConfigureAwait(false))
        {
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
                await RejectAsync(conn, "handler_error", ex.Message, ct).ConfigureAwait(false);
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

    async Task OnCreateAsync(NetConnection conn, string? account, PacketEnvelope envelope, CancellationToken ct)
    {
        RequireHello(conn);
        if (account == null)
        {
            await RejectAsync(conn, "auth_required", "Login required.", ct).ConfigureAwait(false);
            return;
        }

        var req = NetCodec.DecodeCreateCharacter(envelope.Payload);
        var created = _service.CreateHuman(
            account,
            req.Name,
            req.ClassId,
            req.Race,
            req.Sex,
            req.HairStyle,
            req.HairColor,
            req.Face);

        CharacterSummaryNet? summary = null;
        if (created.Ok)
        {
            var roster = _service.List(account);
            int slot = 0;
            if (roster.Ok)
            {
                for (int i = 0; i < roster.Value!.Count; i++)
                {
                    if (roster.Value[i].Id == created.Value!.Id)
                    {
                        slot = i;
                        break;
                    }
                }
            }

            summary = ToSummary(created.Value!, slot);
        }

        await conn.SendAsync(
            NetOpcode.CreateResult,
            NetCodec.EncodeCreateResult(new CreateResultMsg
            {
                Ok = created.Ok,
                Code = created.Code,
                Message = created.Message,
                Character = summary
            }),
            envelope.RequestSequence,
            ct).ConfigureAwait(false);

        if (created.Ok)
            await OnListAsync(conn, account, ct).ConfigureAwait(false);
    }

    async Task OnSelectAsync(NetConnection conn, string? account, PacketEnvelope envelope, CancellationToken ct)
    {
        RequireHello(conn);
        if (account == null)
        {
            await RejectAsync(conn, "auth_required", "Login required.", ct).ConfigureAwait(false);
            return;
        }

        var req = NetCodec.DecodeSelectCharacter(envelope.Payload);
        var selected = _service.Select(account, req.CharacterId, req.Name, req.Slot);
        if (!selected.Ok)
        {
            await RejectAsync(conn, selected.Code, selected.Message, ct).ConfigureAwait(false);
            return;
        }

        var ticket = selected.Value!;
        await conn.SendAsync(
            NetOpcode.Handoff,
            NetCodec.EncodeHandoff(new HandoffMsg
            {
                Account = ticket.Account,
                CharacterId = ticket.CharacterId,
                CharacterName = ticket.CharacterName,
                ClassId = ticket.ClassId,
                Race = ticket.Race,
                Sex = ticket.Sex,
                Token = ticket.Token,
                ExpiresUnixSeconds = ticket.ExpiresUnixSeconds,
                ZoneHost = _zoneHost,
                ZonePort = _zonePort
            }),
            envelope.RequestSequence,
            ct).ConfigureAwait(false);
    }

    async Task OnDeleteAsync(NetConnection conn, string? account, PacketEnvelope envelope, CancellationToken ct)
    {
        RequireHello(conn);
        if (account == null)
        {
            await RejectAsync(conn, "auth_required", "Login required.", ct).ConfigureAwait(false);
            return;
        }

        var req = NetCodec.DecodeDeleteCharacter(envelope.Payload);
        var result = _service.Delete(account, req.Slot, req.CharacterId);
        if (!result.Ok || result.Value == null)
        {
            await conn.SendAsync(
                NetOpcode.DeleteResult,
                NetCodec.EncodeDeleteResult(new DeleteResultMsg
                {
                    Ok = false,
                    Code = result.Code,
                    Message = result.Message,
                    Slot = req.Slot,
                    CharacterId = req.CharacterId
                }),
                envelope.RequestSequence,
                ct).ConfigureAwait(false);
            return;
        }

        // The client redraws from the roster, not from the reply, so send it after the ack.
        await conn.SendAsync(
            NetOpcode.DeleteResult,
            NetCodec.EncodeDeleteResult(new DeleteResultMsg
            {
                Ok = true,
                Slot = result.Value.Slot,
                CharacterId = result.Value.CharacterId
            }),
            envelope.RequestSequence,
            ct).ConfigureAwait(false);
        await SendRosterAsync(conn, account, ct).ConfigureAwait(false);
    }

    async Task OnRestoreAsync(NetConnection conn, string? account, PacketEnvelope envelope, CancellationToken ct)
    {
        RequireHello(conn);
        if (account == null)
        {
            await RejectAsync(conn, "auth_required", "Login required.", ct).ConfigureAwait(false);
            return;
        }

        var req = NetCodec.DecodeRestoreCharacter(envelope.Payload);
        var result = _service.Restore(account, req.Slot, req.CharacterId);
        if (!result.Ok || result.Value == null)
        {
            await conn.SendAsync(
                NetOpcode.DeleteResult,
                NetCodec.EncodeDeleteResult(new DeleteResultMsg
                {
                    Ok = false,
                    Code = result.Code,
                    Message = result.Message,
                    Slot = req.Slot,
                    CharacterId = req.CharacterId
                }),
                envelope.RequestSequence,
                ct).ConfigureAwait(false);
            return;
        }

        await conn.SendAsync(
            NetOpcode.DeleteResult,
            NetCodec.EncodeDeleteResult(new DeleteResultMsg
            {
                Ok = true,
                Slot = req.Slot,
                CharacterId = req.CharacterId
            }),
            envelope.RequestSequence,
            ct).ConfigureAwait(false);
        await SendRosterAsync(conn, account, ct).ConfigureAwait(false);
    }

    Task SendRosterAsync(NetConnection conn, string account, CancellationToken ct)
    {
        var list = _service.List(account);
        if (!list.Ok)
            return RejectAsync(conn, list.Code, list.Message, ct);

        return conn.SendAsync(
            NetOpcode.CharacterRoster,
            NetCodec.EncodeCharacterRoster(ToRoster(list.Value!)),
            ct: ct);
    }

    CharacterRosterMsg ToRoster(IReadOnlyList<CharacterRecord> characters)
    {
        var now = _clock();
        int lastPlayedId = LoginService.LastPlayedId(characters);
        var rows = new CharacterSummaryNet[characters.Count];
        for (int i = 0; i < characters.Count; i++)
            rows[i] = ToSummary(characters[i], i, now, lastPlayedId);
        return new CharacterRosterMsg { Characters = rows };
    }

    CharacterSummaryNet ToSummary(CharacterRecord c, int slot, DateTimeOffset? now = null, int? lastPlayedId = null)
    {
        now ??= _clock();
        lastPlayedId ??= 0;
        var point = c.HasPosition
            ? new Fineage.Protocol.ClassSpawn.Point(c.X, c.Y, c.Z)
            : ClassTemplates.Preview(c.ClassId);
        var stats = CharSelectStats.For(c);
        int deleteSeconds = c.DeleteSecondsLeft(now.Value);
        return new CharacterSummaryNet
        {
            CharacterId = c.Id,
            Slot = slot,
            Name = c.Name,
            ClassId = c.ClassId,
            Race = c.Race,
            Sex = c.Sex,
            Level = c.Level <= 0 ? 1 : c.Level,
            Hp = stats.CurHp,
            MaxHp = stats.MaxHp,
            Mp = stats.CurMp,
            MaxMp = stats.MaxMp,
            HairStyle = c.HairStyle,
            HairColor = c.HairColor,
            Face = c.Face,
            PendingDelete = deleteSeconds > 0 || c.IsPendingDelete,
            X = point.X,
            Y = point.Y,
            Z = point.Z,
            DeleteSecondsLeft = deleteSeconds,
            IsLastPlayed = c.Id == lastPlayedId.Value,
            CurCp = stats.CurCp,
            MaxCp = stats.MaxCp,
            Exp = c.Exp,
            Sp = c.Sp,
            Karma = 0,
            WeaponItemId = stats.WeaponItemId,
            Paperdoll = stats.Paperdoll
        };
    }

    static void RequireHello(NetConnection conn)
    {
        if (!conn.HelloCompleted)
            throw new InvalidOperationException("hello_required");
    }

    static Task RejectAsync(NetConnection conn, string code, string message, CancellationToken ct) =>
        conn.SendAsync(NetOpcode.Reject, NetCodec.EncodeReject(new RejectMsg { Code = code, Message = message }), ct: ct);
}
