using Fineage.Net.Contract;

namespace Fineage.Net.Host;

/// <summary>
/// Ordering rules for the reliable lane. Movement coalesces in its own map until one of these
/// barriers (or a knownlist snapshot) must be observed in place.
/// </summary>
public static class OutboundOrdering
{
    public static bool IsBarrier(ushort opcode) =>
        opcode is NetOpcode.EntityDie or NetOpcode.InventoryUpdate or NetOpcode.DropItem
            or NetOpcode.TeleportToLocation or NetOpcode.ItemList;
}
