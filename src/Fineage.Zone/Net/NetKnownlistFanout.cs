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

    /// <summary>
    /// On the simulation thread: the spawn and despawn frames for a knownlist change from <paramref name="before"/> to
    /// <paramref name="after"/>, for the mover and the peers that start or stop seeing it.
    /// </summary>
    public List<NetFrame> PlanDiff(ZoneSession zoneSession, EntityId self, HashSet<int> before, HashSet<int> after)
    {
        var frames = new List<NetFrame>();
        // Newly visible: EntitySpawn. Forgotten: EntityDespawn.
        foreach (int id in after.OrderBy(static x => x))
        {
            if (before.Contains(id))
                continue;
            if (!_world.TryGetEntityById(id, out var newlySeen))
                continue;

            var otherId = _ids.AllocateOrGet(id);
            if (newlySeen.Kind == ZoneEntityKind.GroundItem)
            {
                frames.Add(NetFrame.ToSelf(
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
                    })));
                continue;
            }

            var spawn = NetCodec.EncodeEntitySpawn(
                NetEnterWorldProjector.BuildPresenceSpawn(newlySeen, otherId, _playerSpawnPAtkSpd(newlySeen)));
            frames.Add(NetFrame.ToSelf(NetOpcode.EntitySpawn, spawn));
            // Mobius sendInfo: a pawn already running / sitting must not appear idle-standing.
            foreach (var (opcode, payload) in NetEnterWorldProjector.BuildPresenceFollowUps(newlySeen, otherId, _world))
                frames.Add(NetFrame.ToSelf(opcode, payload));
            if (newlySeen.Kind == ZoneEntityKind.Player &&
                _world.TryGetLivePlayer(zoneSession, out var selfEntity))
            {
                var toPeer = NetCodec.EncodeEntitySpawn(
                    NetEnterWorldProjector.BuildRemoteSpawn(selfEntity, self, _playerSpawnPAtkSpd(selfEntity)));
                frames.Add(new NetFrame(id, NetOpcode.EntitySpawn, toPeer));
                // The mover just started this leg: the newly reached peer needs it, not only the stayed watchers.
                foreach (var (opcode, payload) in NetEnterWorldProjector.BuildPresenceFollowUps(selfEntity, self, _world))
                    frames.Add(new NetFrame(id, opcode, payload));
            }
        }

        var despawnSelf = NetCodec.EncodeEntityDespawn(new EntityDespawnMsg
        {
            EntityValue = self.Value,
            EntityGeneration = self.Generation
        });
        foreach (int id in before.OrderBy(static x => x))
        {
            if (after.Contains(id))
                continue;
            var forgottenId = _ids.AllocateOrGet(id);
            frames.Add(NetFrame.ToSelf(
                NetOpcode.EntityDespawn,
                NetCodec.EncodeEntityDespawn(new EntityDespawnMsg
                {
                    EntityValue = forgottenId.Value,
                    EntityGeneration = forgottenId.Generation
                })));
            if (_world.TryGetLivePlayerById(id, out _))
                frames.Add(new NetFrame(id, NetOpcode.EntityDespawn, despawnSelf));
        }

        return frames;
    }

    public Task SendAsync(NetConnection conn, IEnumerable<NetFrame> frames, CancellationToken ct) =>
        NetFrame.SendAsync(conn, _presence, frames, ct);
}
