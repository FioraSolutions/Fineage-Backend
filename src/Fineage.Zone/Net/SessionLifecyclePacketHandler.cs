using Fineage.Accounts;
using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Protocol;
using Fineage.Zone.Chat;
using Fineage.Zone.World;
using NetHelloAck = Fineage.Net.Contract.HelloAckMsg;

namespace Fineage.Zone.Net;

/// <summary>
/// Hello / ZoneAuth / EnterWorld extracted from <see cref="NetZoneServer"/> (B2b / #353).
/// Presence Register after auth stays on the connection loop (local registeredObjectId).
/// </summary>
sealed partial class SessionLifecyclePacketHandler
{
    readonly IHandoffStore _handoff;
    readonly ZoneWorld _world;
    readonly EntityIdMap _ids;
    readonly NetPresenceHub _presence;
    readonly Func<DateTimeOffset> _clock;
    readonly Func<ZoneEntity, int> _playerSpawnPAtkSpd;
    readonly Func<NetConnection, string, string, CancellationToken, Task> _reject;

    public SessionLifecyclePacketHandler(
        IHandoffStore handoff,
        ZoneWorld world,
        EntityIdMap ids,
        NetPresenceHub presence,
        Func<DateTimeOffset> clock,
        Func<ZoneEntity, int> playerSpawnPAtkSpd,
        Func<NetConnection, string, string, CancellationToken, Task> reject)
    {
        _handoff = handoff ?? throw new ArgumentNullException(nameof(handoff));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));
        _presence = presence ?? throw new ArgumentNullException(nameof(presence));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _playerSpawnPAtkSpd = playerSpawnPAtkSpd ?? throw new ArgumentNullException(nameof(playerSpawnPAtkSpd));
        _reject = reject ?? throw new ArgumentNullException(nameof(reject));
    }

    public async Task OnHelloAsync(NetConnection conn, PacketEnvelope envelope, CancellationToken ct)
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

    public async Task<EntityId> OnAuthAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);

        // A session authenticates once. Re-authing would silently swap a live character for another
        // handoff inside the same connection, so refuse it the way the retired V2 listener did.
        if (zoneSession.Bound)
        {
            await _reject(conn, "already_authenticated", "This session already completed zone auth.", ct)
                .ConfigureAwait(false);
            return default;
        }

        var auth = NetCodec.DecodeZoneAuth(envelope.Payload);
        if (string.IsNullOrWhiteSpace(auth.Account))
        {
            await _reject(conn, "bad_request", "Auth account is required.", ct).ConfigureAwait(false);
            return default;
        }

        var status = _handoff.TryConsume(auth.Token, _clock(), out var parsed);
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
        zoneSession.Bind(parsed.CharacterId, parsed.Account, parsed.CharacterName, parsed.ClassId, parsed.Race, parsed.Sex);
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
}
