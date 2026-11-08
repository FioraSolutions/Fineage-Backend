using Fineage.Accounts;
using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Zone.Chat;
using Fineage.Zone.World;
using NetHelloAck = Fineage.Net.Contract.HelloAckMsg;

namespace Fineage.Zone.Net;

/// <summary>
/// Hello / ZoneAuth / EnterWorld extracted from <see cref="NetZoneServer"/> (B2b / #353).
/// Presence Register after auth stays on the connection loop (local registeredObjectId).
/// </summary>
sealed class SessionLifecyclePacketHandler
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

    public async Task OnEnterAsync(NetConnection conn, ZoneSession zoneSession, EntityId self, CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound)
        {
            await _reject(conn, "auth_required", "Auth with a handoff token first.", ct).ConfigureAwait(false);
            return;
        }

        bool ok = await _world.TryEnterAsync(zoneSession, ct).ConfigureAwait(false);
        if (!ok)
        {
            await _reject(conn, "enter_failed", "Could not enter world.", ct).ConfigureAwait(false);
            return;
        }

        if (!_world.TryGetLivePlayer(zoneSession, out var entity))
        {
            await _reject(conn, "enter_failed", "Enter world produced no live player entity.", ct).ConfigureAwait(false);
            return;
        }

        // Single source of truth: ZoneEntity after TryEnter (inventory, vitals, AutoSkills already applied).
        var packets = NetEnterWorldProjector.FromEntity(entity, self, _clock().ToUnixTimeMilliseconds(), _playerSpawnPAtkSpd(entity));
        await conn.SendAsync(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(packets.Spawn), ct: ct)
            .ConfigureAwait(false);
        await conn.SendAsync(NetOpcode.PlayerStats, NetCodec.EncodePlayerStats(packets.Stats), ct: ct)
            .ConfigureAwait(false);
        // Mobius enter: UserInfo (full sheet + paperdoll) before ItemList so the status window and model are dressed.
        if (_world.TryBuildNetUserInfo(zoneSession, out var userInfo))
            await conn.SendAsync(NetOpcode.UserInfo, NetCodec.EncodeUserInfo(userInfo), ct: ct).ConfigureAwait(false);
        await conn.SendAsync(NetOpcode.ItemList, NetCodec.EncodeItemList(packets.Items), ct: ct)
            .ConfigureAwait(false);
        await conn.SendAsync(NetOpcode.SkillList, NetCodec.EncodeSkillList(NetEnterWorldProjector.ToNetSkillList(packets.Skills)), ct: ct)
            .ConfigureAwait(false);
        // Mobius EnterWorld: ShortCutInit after ItemList / SkillList so item and skill icons resolve.
        await conn.SendAsync(NetOpcode.ShortCutInit, NetCodec.EncodeShortCutInit(_world.NetShortcutInit(zoneSession)), ct: ct)
            .ConfigureAwait(false);
        // Mobius EnterWorld:401 sends EtcStatusUpdate after ItemList/ShortCutInit and after updateEffectIcons, so the
        // overload icon appears on an otherwise fully-dressed status bar.
        if (_world.TryBuildNetEtcStatus(zoneSession, out var etcStatus))
            await conn.SendAsync(NetOpcode.EtcStatusUpdate, NetCodec.EncodeEtcStatusUpdate(etcStatus), ct: ct)
                .ConfigureAwait(false);
        // Journal on enter so the quest tabs are filled before the player presses the menu button.
        await conn.SendAsync(NetOpcode.QuestList, NetCodec.EncodeQuestList(_world.NetQuestList(zoneSession)), ct: ct)
            .ConfigureAwait(false);
        // Mobius EnterWorld:561 — a returning clan member gets the pledge sheet and roster up front,
        // otherwise the clan window is blank until some later clan action fans a status out.
        if (_world.TryBuildNetClanSnapshot(zoneSession, out var clanStatus, out var clanMembers))
        {
            await conn.SendAsync(NetOpcode.PledgeStatus, NetCodec.EncodePledgeStatus(clanStatus), ct: ct)
                .ConfigureAwait(false);
            await conn.SendAsync(NetOpcode.PledgeMemberList, NetCodec.EncodePledgeMemberList(clanMembers), ct: ct)
                .ConfigureAwait(false);
        }

        var welcome = SayRules.Welcome(zoneSession.Name);
        await conn.SendAsync(
            NetOpcode.SystemMessage,
            NetCodec.EncodeSystemMessage(new SystemMessageNetMsg
            {
                Id = welcome.Id,
                Text = welcome.Text,
                Params = welcome.Params ?? []
            }),
            ct: ct).ConfigureAwait(false);

        var known = _world.SnapshotKnownEntities(zoneSession);
        foreach (var other in known)
        {
            var otherId = _ids.AllocateOrGet(other.ObjectId);
            if (other.Kind == ZoneEntityKind.GroundItem)
            {
                await conn.SendAsync(
                    NetOpcode.DropItem,
                    NetCodec.EncodeDropItem(new DropItemNetMsg
                    {
                        DropperObjectId = 0,
                        EntityValue = otherId.Value,
                        EntityGeneration = otherId.Generation,
                        ItemId = other.GroundItemId,
                        X = other.X,
                        Y = other.Y,
                        Z = other.Z,
                        Stackable = other.GroundStackable,
                        Count = other.GroundCount
                    }),
                    ct: ct).ConfigureAwait(false);
                continue;
            }

            var remote = NetEnterWorldProjector.BuildPresenceSpawn(other, otherId, _playerSpawnPAtkSpd(other));
            await conn.SendAsync(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(remote), ct: ct)
                .ConfigureAwait(false);
        }

        var selfRemote = NetCodec.EncodeEntitySpawn(NetEnterWorldProjector.BuildRemoteSpawn(entity, self, _playerSpawnPAtkSpd(entity)));
        foreach (var peer in known)
        {
            if (peer.Kind == ZoneEntityKind.Player)
                await _presence.SendAsync(peer.ObjectId, NetOpcode.EntitySpawn, selfRemote, ct).ConfigureAwait(false);
        }
    }

    public async Task LeaveAsync(ZoneSession zoneSession, EntityId self, int registeredObjectId, NetConnection conn)
    {
        try
        {
            if (zoneSession.Bound && zoneSession.InWorld && self.Value != 0)
            {
                var peers = _world.SnapshotKnownPlayers(zoneSession);
                var despawn = NetCodec.EncodeEntityDespawn(new EntityDespawnMsg
                {
                    EntityValue = self.Value,
                    EntityGeneration = self.Generation
                });
                foreach (var peer in peers)
                    await _presence.SendAsync(peer.ObjectId, NetOpcode.EntityDespawn, despawn, CancellationToken.None)
                        .ConfigureAwait(false);
            }
        }
        catch
        {
            /* ignore */
        }

        if (registeredObjectId != 0)
            _presence.Unregister(registeredObjectId, conn);

        try
        {
            await _world.LeaveAsync(zoneSession, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            /* ignore */
        }
    }


    static void RequireHello(NetConnection conn)
    {
        if (!conn.HelloCompleted)
            throw new InvalidOperationException("hello_required");
    }
}
