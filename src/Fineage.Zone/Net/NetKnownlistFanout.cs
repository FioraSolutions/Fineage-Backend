using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

/// <summary>
/// AOI knownlist spawn/despawn fanout for enter-move / revive / bypass (#353 B2b).
/// Extracted from <see cref="NetZoneServer"/> so handlers share one implementer.
/// </summary>
sealed class NetKnownlistFanout
{
    readonly ZoneWorld _world;
    readonly EntityIdMap _ids;
    readonly NetPresenceHub _presence;
    readonly Func<ZoneEntity, int> _playerSpawnPAtkSpd;

    public NetKnownlistFanout(
        ZoneWorld world,
        EntityIdMap ids,
        NetPresenceHub presence,
        Func<ZoneEntity, int> playerSpawnPAtkSpd)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));
        _presence = presence ?? throw new ArgumentNullException(nameof(presence));
        _playerSpawnPAtkSpd = playerSpawnPAtkSpd ?? throw new ArgumentNullException(nameof(playerSpawnPAtkSpd));
    }

    public async Task FanDiffAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        EntityId self,
        HashSet<int> before,
        HashSet<int> after,
        CancellationToken ct)
    {
        // Newly visible: EntitySpawn. Forgotten: EntityDespawn.
        foreach (int id in after)
        {
            if (before.Contains(id))
                continue;
            if (!_world.TryGetEntityById(id, out var newlySeen))
                continue;

            var otherId = _ids.AllocateOrGet(id);
            if (newlySeen.Kind == ZoneEntityKind.GroundItem)
            {
                await conn.SendAsync(
                    NetOpcode.DropItem,
                    NetCodec.EncodeDropItem(new DropItemNetMsg
                    {
                        DropperObjectId = 0,
                        EntityValue = otherId.Value,
                        EntityGeneration = otherId.Generation,
                        ItemId = newlySeen.GroundItemId,
                        X = newlySeen.X,
                        Y = newlySeen.Y,
                        Z = newlySeen.Z,
                        Stackable = newlySeen.GroundStackable,
                        Count = newlySeen.GroundCount
                    }),
                    ct: ct).ConfigureAwait(false);
                continue;
            }

            var spawn = NetCodec.EncodeEntitySpawn(
                NetEnterWorldProjector.BuildPresenceSpawn(newlySeen, otherId, _playerSpawnPAtkSpd(newlySeen)));
            await conn.SendAsync(NetOpcode.EntitySpawn, spawn, ct: ct).ConfigureAwait(false);
            if (newlySeen.Kind == ZoneEntityKind.Player &&
                _world.TryGetLivePlayer(zoneSession, out var selfEntity))
            {
                var toPeer = NetCodec.EncodeEntitySpawn(
                    NetEnterWorldProjector.BuildRemoteSpawn(selfEntity, self, _playerSpawnPAtkSpd(selfEntity)));
                await _presence.SendAsync(id, NetOpcode.EntitySpawn, toPeer, ct).ConfigureAwait(false);
            }
        }

        var despawnSelf = NetCodec.EncodeEntityDespawn(new EntityDespawnMsg
        {
            EntityValue = self.Value,
            EntityGeneration = self.Generation
        });
        foreach (int id in before)
        {
            if (after.Contains(id))
                continue;
            var forgottenId = _ids.AllocateOrGet(id);
            await conn.SendAsync(
                NetOpcode.EntityDespawn,
                NetCodec.EncodeEntityDespawn(new EntityDespawnMsg
                {
                    EntityValue = forgottenId.Value,
                    EntityGeneration = forgottenId.Generation
                }),
                ct: ct).ConfigureAwait(false);
            if (_world.TryGetLivePlayerById(id, out _))
                await _presence.SendAsync(id, NetOpcode.EntityDespawn, despawnSelf, ct).ConfigureAwait(false);
        }
    }
}
