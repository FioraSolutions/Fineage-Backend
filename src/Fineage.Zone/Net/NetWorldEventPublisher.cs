using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

/// <summary>
/// World → Net presence fanout: Protocol/primitives in, wire encode out (#353 / B2b).
/// Extracted from <see cref="NetZoneServer"/> so the listener stays dispatch + lifecycle.
/// </summary>
sealed partial class NetWorldEventPublisher : IWorldEventPublisher
{
    readonly ZoneWorld _world;
    readonly EntityIdMap _ids;
    readonly NetPresenceHub _presence;
    readonly Func<ZoneEntity, int> _playerSpawnPAtkSpd;

    /// <summary>EntitySpawn frames emitted at the last knownlist diff drain (Fineage.Zone.Tests).</summary>
    internal int KnownlistSpawnCountAtDrain { get; private set; }

    public NetWorldEventPublisher(
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

    void IWorldEventPublisher.FanoutMoveState(
        IReadOnlyList<int> watcherObjectIds,
        int moverObjectId,
        int originX,
        int originY,
        int originZ,
        int destX,
        int destY,
        int destZ,
        bool running)
    {
        var mover = _ids.AllocateOrGet(moverObjectId);
        var payload = NetCodec.EncodeMoveState(new MoveStateMsg
        {
            EntityValue = mover.Value,
            EntityGeneration = mover.Generation,
            OriginX = originX,
            OriginY = originY,
            OriginZ = originZ,
            DestX = destX,
            DestY = destY,
            DestZ = destZ,
            Running = running
        });
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.MoveState, payload);
    }

    void IWorldEventPublisher.FanoutKnownlistDiff(int selfObjectId, IReadOnlyList<int> added, IReadOnlyList<int> removed) =>
        ((IWorldEventPublisher)this).PrepareKnownlistDiff(selfObjectId, added, removed)();

    Action IWorldEventPublisher.PrepareKnownlistDiff(int selfObjectId, IReadOnlyList<int> added, IReadOnlyList<int> removed)
    {
        var frames = new PreparedFrames(_presence);
        var removedSet = new HashSet<int>(removed);
        var addedIds = added.ToArray();
        if (removed.Count > 0)
        {
            var self = _ids.AllocateOrGet(selfObjectId);
            var despawnSelf = NetCodec.EncodeEntityDespawn(new EntityDespawnMsg
            {
                EntityValue = self.Value,
                EntityGeneration = self.Generation
            });
            foreach (int id in removed)
            {
                var forgotten = _ids.AllocateOrGet(id);
                frames.Critical(selfObjectId, NetOpcode.EntityDespawn, NetCodec.EncodeEntityDespawn(new EntityDespawnMsg
                {
                    EntityValue = forgotten.Value,
                    EntityGeneration = forgotten.Generation
                }));
                if (_world.TryGetLivePlayerById(id, out _))
                    frames.Critical(id, NetOpcode.EntityDespawn, despawnSelf);
            }
        }

        return () =>
        {
            KnownlistSpawnCountAtDrain = 0;
            if (!_world.TryGetEntityById(selfObjectId, out var selfEntity))
            {
                ZoneLog.Warn($"knownlist diff drain skip spawns self={selfObjectId}: observer no longer in world");
                frames.Send();
                return;
            }

            var self = _ids.AllocateOrGet(selfObjectId);
            byte[]? selfSpawn = null;
            List<(ushort Opcode, byte[] Payload)>? selfFollowUps = null;
            foreach (int id in addedIds)
            {
                if (removedSet.Contains(id))
                {
                    ZoneLog.Warn($"knownlist diff skip spawn object {id}: despawned same tick as add");
                    continue;
                }

                if (!_world.TryGetEntityById(id, out var seen))
                {
                    ZoneLog.Warn($"knownlist diff skip spawn object {id}: entity no longer in world at drain");
                    continue;
                }

                var otherId = _ids.AllocateOrGet(id);
                if (seen.Kind == ZoneEntityKind.GroundItem)
                {
                    frames.Normal(selfObjectId, NetOpcode.DropItem, NetCodec.EncodeDropItem(new DropItemNetMsg
                    {
                        DropperObjectId = 0,
                        EntityValue = otherId.Value,
                        EntityGeneration = otherId.Generation,
                        ItemId = seen.GroundItemId,
                        X = seen.X,
                        Y = seen.Y,
                        Z = seen.Z,
                        Stackable = seen.GroundStackable,
                        Count = seen.GroundCount
                    }));
                    continue;
                }

                KnownlistSpawnCountAtDrain++;
                frames.Critical(selfObjectId, NetOpcode.EntitySpawn,
                    NetCodec.EncodeEntitySpawn(NetEnterWorldProjector.BuildPresenceSpawn(seen, otherId, _playerSpawnPAtkSpd(seen))));
                foreach (var (opcode, payload) in NetEnterWorldProjector.BuildPresenceFollowUps(seen, otherId, _world))
                    frames.Critical(selfObjectId, opcode, payload);
                if (_world.SpawnRelationFrame(seen, selfEntity) is { } seenRelation)
                    frames.Critical(selfObjectId, NetOpcode.RelationChanged, seenRelation);
                if (seen.Kind == ZoneEntityKind.Player && seen.Session is { InWorld: true })
                {
                    selfSpawn ??= NetCodec.EncodeEntitySpawn(NetEnterWorldProjector.BuildRemoteSpawn(selfEntity, self, _playerSpawnPAtkSpd(selfEntity)));
                    frames.Critical(id, NetOpcode.EntitySpawn, selfSpawn);
                    selfFollowUps ??= NetEnterWorldProjector.BuildPresenceFollowUps(selfEntity, self, _world);
                    foreach (var (opcode, payload) in selfFollowUps)
                        frames.Critical(id, opcode, payload);
                    if (_world.SpawnRelationFrame(selfEntity, seen) is { } selfRelation)
                        frames.Critical(id, NetOpcode.RelationChanged, selfRelation);
                }
            }

            frames.Send();
        };
    }

    void IWorldEventPublisher.FanoutChangeMoveType(IReadOnlyList<int> watcherObjectIds, int moverObjectId, bool running, float runSpeed, float walkSpeed, bool inWater)
    {
        var mover = _ids.AllocateOrGet(moverObjectId);
        var payload = NetCodec.EncodeChangeMoveType(new ChangeMoveTypeNetMsg
        {
            EntityValue = mover.Value,
            EntityGeneration = mover.Generation,
            Running = running,
            RunSpeed = runSpeed,
            WalkSpeed = walkSpeed,
            InWater = inWater
        });
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.ChangeMoveType, payload);
    }

    void IWorldEventPublisher.FanoutPositionCorrection(IReadOnlyList<int> watcherObjectIds, int objectId, int x, int y, int z, int heading)
    {
        var id = _ids.AllocateOrGet(objectId);
        var payload = NetCodec.EncodePositionCorrection(new PositionCorrectionMsg
        {
            EntityValue = id.Value,
            EntityGeneration = id.Generation,
            X = x,
            Y = y,
            Z = z,
            Heading = heading
        });
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.PositionCorrection, payload);
    }

    void IWorldEventPublisher.FanoutAttackStance(IReadOnlyList<int> watcherObjectIds, int objectId, bool inStance)
    {
        var id = _ids.AllocateOrGet(objectId);
        var payload = NetCodec.EncodeAttackStance(new AttackStanceNetMsg { EntityValue = id.Value, EntityGeneration = id.Generation });
        ushort opcode = inStance ? NetOpcode.AutoAttackStart : NetOpcode.AutoAttackStop;
        _presence.TryBroadcast(watcherObjectIds, opcode, payload);
    }

    void IWorldEventPublisher.FanoutAutoSoulShot(IReadOnlyList<int> watcherObjectIds, int itemId, bool enabled)
    {
        var payload = NetCodec.EncodeAutoSoulShot(new AutoSoulShotNetMsg { ItemId = itemId, Enabled = enabled });
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.AutoSoulShot, payload);
    }

    void IWorldEventPublisher.FanoutUserInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.UserInfoMsg info)
    {
        var payload = NetCodec.EncodeUserInfo(ToNetUserInfo(info));
        _presence.TryBroadcastCritical(watcherObjectIds, NetOpcode.UserInfo, payload);
    }
}
