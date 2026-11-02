using Fineage.Net.Contract;

namespace Fineage.Zone.Net;

/// <summary>
/// ZoneWorld → Net PacketEnvelope fanout for sim ticks (NPC chase, attacks, vitals, die/drop).
/// Player enter/move knownlist diffs stay in <see cref="NetZoneServer"/>.
/// </summary>
interface INetPresenceBridge
{
    void FanoutMoveState(
        IReadOnlyList<int> watcherObjectIds,
        int moverObjectId,
        int originX,
        int originY,
        int originZ,
        int destX,
        int destY,
        int destZ,
        bool running);

    void FanoutAttackState(IReadOnlyList<int> watcherObjectIds, AttackStateMsg attack);

    void FanoutStatusUpdate(IReadOnlyList<int> watcherObjectIds, StatusUpdateNetMsg status);

    void FanoutDie(IReadOnlyList<int> watcherObjectIds, EntityDieMsg die);

    void FanoutDropItem(IReadOnlyList<int> watcherObjectIds, DropItemNetMsg drop);

    void FanoutEntityDespawn(IReadOnlyList<int> watcherObjectIds, EntityDespawnMsg despawn);
}
