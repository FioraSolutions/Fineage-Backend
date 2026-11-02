namespace Fineage.Zone.Net;

/// <summary>
/// ZoneWorld → Net PacketEnvelope fanout for sim ticks (NPC chase MoveState, etc.).
/// Player enter/move fanout stays in <see cref="NetZoneServer"/>.
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
}
