using Fineage.Protocol;
using Fineage.Zone.SevenSigns;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    void TryGrantFestivalBloodOffering(ZoneEntity killer, ZoneEntity npc)
    {
        if (npc.FestivalPartyId <= 0 || killer.Kind != ZoneEntityKind.Player)
            return;
        if (FestivalBoard.OfParty(npc.FestivalPartyId) is not { } instance)
            return;
        if (!_byId.TryGetValue(instance.LeaderObjectId, out var leader) || leader.Inventory == null || leader.Session == null)
            return;
        int count = Math.Max(1, npc.FestivalOfferingBonus);
        var added = leader.Inventory.Add(_festivalCatalog.OfferingItemId, count);
        instance.OfferingScore += count * _festivalCatalog.OfferingValue;
        NetBridge?.FanoutInventoryUpdate(
            [leader.Session.ObjectId],
            InventoryMessages.Update(InventoryChange.Add, added));
        SaveInventory(leader.Session, leader.Inventory);
    }
}