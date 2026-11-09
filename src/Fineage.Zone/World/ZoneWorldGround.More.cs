using Fineage.Game.Services;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Drops;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    /// <summary>Mobius <c>Party.distributeAdena</c> on ground pickup: equal share to in-range living members.</summary>
    void DistributePickupAdena(ZoneParty party, ZoneEntity picker, ZoneEntity ground)
    {
        var inRange = new List<ZoneEntity>();
        foreach (int id in party.MemberIds)
        {
            if (!_byId.TryGetValue(id, out var m) || m.Kind != ZoneEntityKind.Player || m.Inventory == null || m.Dead)
                continue;
            if (!ZoneCombat.Within(ground.X, ground.Y, m.X, m.Y, PartyLootRules.PartyRange))
                continue;
            inRange.Add(m);
        }

        int adena = ground.GroundCount;
        RemoveGroundItem(ground);
        if (inRange.Count == 0)
        {
            if (picker.Inventory != null && picker.Session != null)
                GrantInventoryStack(picker, WarehouseRules.AdenaItemId, adena, notifyObtained: true);
            return;
        }

        int share = PartyLootRules.AdenaShare(adena, inRange.Count);
        if (share <= 0)
            return;
        foreach (var m in inRange)
            GrantInventoryStack(m, WarehouseRules.AdenaItemId, share, notifyObtained: true);
    }

    bool CanReceiveGround(int objectId, ZoneEntity ground)
    {
        if (!_byId.TryGetValue(objectId, out var m) || m.Kind != ZoneEntityKind.Player || m.Inventory == null || m.Dead)
            return false;
        if (!ZoneCombat.Within(ground.X, ground.Y, m.X, m.Y, PartyLootRules.PartyRange))
            return false;
        return HasRoomForGroundItem(m, ground);
    }

    /// <summary>Move one ground stack into <paramref name="looter"/>'s bag and despawn the world item.</summary>
    void GrantGroundPickup(ZoneEntity looter, ZoneEntity ground)
    {
        if (looter.Inventory == null || looter.Session == null)
            return;
        int itemId = ground.GroundItemId;
        int count = ground.GroundCount;
        bool stackable = ground.GroundStackable;
        // Stack merge must be a Modify (Mobius InventoryUpdate addModifiedItem): the client ignores an Add
        // for an objectId it already holds, so the picked-up count only showed after relog.
        bool merged = stackable && looter.Inventory.FindByItemId(itemId) != null;
        InventoryUpdateMsg invUpdate;
        if (stackable || count <= 1)
        {
            var added = looter.Inventory.Add(itemId, count);
            invUpdate = InventoryMessages.Update(merged ? InventoryChange.Modify : InventoryChange.Add, added);
        }
        else
        {
            var pieces = looter.Inventory.AddPieces(itemId, count);
            invUpdate = new InventoryUpdateMsg
            {
                Changes = pieces.Select(p => InventoryMessages.Update(InventoryChange.Add, p).Changes[0]).ToArray()
            };
        }

        NetBridge?.FanoutInventoryUpdate([looter.Session.ObjectId], invUpdate);
        RemoveGroundItem(ground);
        SaveInventory(looter.Session, looter.Inventory);
        NotifyObtained(looter.Session, itemId, count);
    }

}
