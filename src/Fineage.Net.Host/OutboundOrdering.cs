using Fineage.Net.Contract;

namespace Fineage.Net.Host;

/// <summary>
/// One ordering table for the per-connection mailbox (issue #867 PR3/PR4).
/// Replaceable presence frames collapse only until an ordering barrier; barriers are lifecycle frames
/// that the client must observe in place (spawn, despawn, death, inventory, drop, teleport).
/// </summary>
public static class OutboundOrdering
{
    public static bool IsReplaceable(ushort opcode) =>
        opcode is NetOpcode.MoveState or NetOpcode.ChangeMoveType
            or NetOpcode.StatusUpdate or NetOpcode.AbnormalStatusUpdate;

    public static bool IsBarrier(ushort opcode) =>
        opcode is NetOpcode.EntitySpawn or NetOpcode.EntityDespawn or NetOpcode.EntityDie
            or NetOpcode.InventoryUpdate or NetOpcode.DropItem or NetOpcode.TeleportToLocation;
}
