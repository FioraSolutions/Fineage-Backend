using Fineage.Login;
using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Protocol;
using Fineage.Zone.World;
using NetHelloAck = Fineage.Net.Contract.HelloAckMsg;
using NetReject = Fineage.Net.Contract.RejectMsg;

namespace Fineage.Zone.Net;

/// <summary>
/// Fineage Net zone path. Reuses <see cref="ZoneWorld.TryEnterAsync"/> / <see cref="ZoneWorld.TryMoveAsync"/>;
/// V2 wire flushes go to a discarded stream so Net packets are the client-facing truth.
/// </summary>
public sealed class NetZoneServer
{
    readonly HandoffTokens _tokens;
    readonly ZoneWorld _world;
    readonly IAccountStore? _accounts;
    readonly EntityIdMap _ids = new();
    readonly Func<DateTimeOffset> _clock;

    public NetZoneServer(
        HandoffTokens tokens,
        ZoneWorld world,
        IAccountStore? accounts = null,
        Func<DateTimeOffset>? clock = null)
    {
        _tokens = tokens;
        _world = world;
        _accounts = accounts;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task HandleAsync(NetConnection conn, CancellationToken ct)
    {
        await using var discard = new MemoryStream();
        var zoneSession = new ZoneSession(discard);
        EntityId self = default;

        await foreach (var envelope in conn.Inbox.ReadAllAsync(ct).ConfigureAwait(false))
        {
            try
            {
                switch (envelope.Opcode)
                {
                    case NetOpcode.Hello:
                        await OnHelloAsync(conn, envelope, ct).ConfigureAwait(false);
                        break;
                    case NetOpcode.ZoneAuth:
                        self = await OnAuthAsync(conn, zoneSession, envelope, ct).ConfigureAwait(false);
                        break;
                    case NetOpcode.EnterWorld:
                        await OnEnterAsync(conn, zoneSession, self, ct).ConfigureAwait(false);
                        break;
                    case NetOpcode.MoveInput:
                        await OnMoveAsync(conn, zoneSession, self, envelope, ct).ConfigureAwait(false);
                        break;
                    case NetOpcode.StopMove:
                        // Authoritative stop: correct to current session position.
                        if (zoneSession.Bound && zoneSession.InWorld)
                        {
                            await conn.SendAsync(
                                NetOpcode.PositionCorrection,
                                NetCodec.EncodePositionCorrection(new PositionCorrectionMsg
                                {
                                    EntityValue = self.Value,
                                    EntityGeneration = self.Generation,
                                    X = zoneSession.X,
                                    Y = zoneSession.Y,
                                    Z = zoneSession.Z
                                }),
                                envelope.RequestSequence,
                                ct).ConfigureAwait(false);
                        }
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

        try
        {
            await _world.LeaveAsync(zoneSession, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            /* ignore */
        }
    }

    async Task OnHelloAsync(NetConnection conn, PacketEnvelope envelope, CancellationToken ct)
    {
        var hello = NetCodec.DecodeHello(envelope.Payload);
        uint granted = ProtocolCapabilities.FeatureSession | ProtocolCapabilities.FeatureWorldPresence;
        conn.HelloCompleted = true;
        conn.FeatureFlags = granted & (hello.FeatureFlags == 0 ? granted : hello.FeatureFlags);

        await conn.SendAsync(
            NetOpcode.HelloAck,
            NetCodec.EncodeHelloAck(new NetHelloAck
            {
                Major = PacketEnvelope.ProtocolMajor,
                Minor = PacketEnvelope.ProtocolMinor,
                FeatureFlags = conn.FeatureFlags,
                ServerName = "fineage-zone-net"
            }),
            ct: ct).ConfigureAwait(false);
    }

    async Task<EntityId> OnAuthAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        var auth = NetCodec.DecodeZoneAuth(envelope.Payload);
        var status = _tokens.Validate(auth.Token, _clock(), out var parsed);
        if (status == HandoffTokens.ValidateStatus.Expired)
        {
            await conn.SendAsync(
                NetOpcode.ZoneAuthResult,
                NetCodec.EncodeZoneAuthResult(new ZoneAuthResultMsg
                {
                    Ok = false,
                    Code = "handoff_expired",
                    Message = "Handoff token has expired."
                }),
                envelope.RequestSequence,
                ct).ConfigureAwait(false);
            return default;
        }

        if (status != HandoffTokens.ValidateStatus.Ok ||
            !string.Equals(parsed.Account, auth.Account, StringComparison.OrdinalIgnoreCase))
        {
            await conn.SendAsync(
                NetOpcode.ZoneAuthResult,
                NetCodec.EncodeZoneAuthResult(new ZoneAuthResultMsg
                {
                    Ok = false,
                    Code = "invalid_credentials",
                    Message = "Handoff token was rejected."
                }),
                envelope.RequestSequence,
                ct).ConfigureAwait(false);
            return default;
        }

        await _world.LeaveAsync(zoneSession, ct).ConfigureAwait(false);
        zoneSession.Bind(parsed.CharacterId, parsed.Account, parsed.CharacterName, parsed.ClassId);
        var entityId = _ids.AllocateOrGet(parsed.CharacterId);

        await conn.SendAsync(
            NetOpcode.ZoneAuthResult,
            NetCodec.EncodeZoneAuthResult(new ZoneAuthResultMsg
            {
                Ok = true,
                EntityValue = entityId.Value,
                EntityGeneration = entityId.Generation
            }),
            envelope.RequestSequence,
            ct).ConfigureAwait(false);

        return entityId;
    }

    async Task OnEnterAsync(NetConnection conn, ZoneSession zoneSession, EntityId self, CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound)
        {
            await RejectAsync(conn, "auth_required", "Auth with a handoff token first.", ct).ConfigureAwait(false);
            return;
        }

        bool ok = await _world.TryEnterAsync(zoneSession, ct).ConfigureAwait(false);
        if (!ok)
        {
            await RejectAsync(conn, "enter_failed", "Could not enter world.", ct).ConfigureAwait(false);
            return;
        }

        var stored = _accounts?.FindById(zoneSession.Account, zoneSession.ObjectId);
        int race = stored?.Race ?? 0;
        int sex = stored?.Sex ?? 0;
        int level = stored is { Level: > 0 } ? stored.Level : 1;
        long exp = stored?.Exp ?? 0;
        int sp = stored?.Sp ?? 0;
        int maxHp = stored is { MaxHp: > 0 } ? stored.MaxHp : 100;
        int hp = stored is { Hp: > 0 } ? stored.Hp : maxHp;
        int maxMp = stored is { MaxMp: > 0 } ? stored.MaxMp : 50;
        int mp = stored is { Mp: > 0 } ? stored.Mp : maxMp;

        await conn.SendAsync(
            NetOpcode.EntitySpawn,
            NetCodec.EncodeEntitySpawn(new EntitySpawnMsg
            {
                EntityValue = self.Value,
                EntityGeneration = self.Generation,
                Kind = EntityKindNet.Player,
                IsSelf = true,
                Name = zoneSession.Name,
                TemplateId = zoneSession.ClassId,
                ClassId = zoneSession.ClassId,
                Race = race,
                Sex = sex,
                X = zoneSession.X,
                Y = zoneSession.Y,
                Z = zoneSession.Z,
                RunSpeed = 120f,
                WalkSpeed = 80f,
                Running = true
            }),
            ct: ct).ConfigureAwait(false);

        await conn.SendAsync(
            NetOpcode.PlayerStats,
            NetCodec.EncodePlayerStats(new PlayerStatsMsg
            {
                EntityValue = self.Value,
                EntityGeneration = self.Generation,
                Level = level,
                Exp = exp,
                Sp = sp,
                Hp = hp,
                MaxHp = maxHp,
                Mp = mp,
                MaxMp = maxMp,
                Cp = 0,
                MaxCp = 0
            }),
            ct: ct).ConfigureAwait(false);
    }

    async Task OnMoveAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        EntityId self,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await RejectAsync(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var move = NetCodec.DecodeMoveInput(envelope.Payload);
        var msg = new MoveToMsg
        {
            ObjectId = zoneSession.ObjectId,
            DestX = move.DestX,
            DestY = move.DestY,
            DestZ = move.DestZ,
            OriginX = zoneSession.X,
            OriginY = zoneSession.Y,
            OriginZ = zoneSession.Z
        };

        string? error = await _world.TryMoveAsync(zoneSession, msg, ct).ConfigureAwait(false);
        if (error != null)
        {
            await conn.SendAsync(
                NetOpcode.PositionCorrection,
                NetCodec.EncodePositionCorrection(new PositionCorrectionMsg
                {
                    EntityValue = self.Value,
                    EntityGeneration = self.Generation,
                    X = zoneSession.X,
                    Y = zoneSession.Y,
                    Z = zoneSession.Z
                }),
                envelope.RequestSequence,
                ct).ConfigureAwait(false);
            return;
        }

        await conn.SendAsync(
            NetOpcode.MoveState,
            NetCodec.EncodeMoveState(new MoveStateMsg
            {
                EntityValue = self.Value,
                EntityGeneration = self.Generation,
                OriginX = msg.OriginX,
                OriginY = msg.OriginY,
                OriginZ = msg.OriginZ,
                DestX = move.DestX,
                DestY = move.DestY,
                DestZ = move.DestZ,
                Running = true
            }),
            envelope.RequestSequence,
            ct).ConfigureAwait(false);
    }

    static void RequireHello(NetConnection conn)
    {
        if (!conn.HelloCompleted)
            throw new InvalidOperationException("hello_required");
    }

    static Task RejectAsync(NetConnection conn, string code, string message, CancellationToken ct) =>
        conn.SendAsync(NetOpcode.Reject, NetCodec.EncodeReject(new NetReject { Code = code, Message = message }), ct: ct);
}
