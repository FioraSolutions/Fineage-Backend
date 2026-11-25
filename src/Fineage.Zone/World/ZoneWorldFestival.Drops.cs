using Fineage.Game.Economy;
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
        int itemId = _festivalCatalog.OfferingItemId;
        int offeringValue = _festivalCatalog.OfferingValue;
        RecordBagChange(
            leader,
            LedgerTxKind.FestivalBloodOffering,
            LedgerAccount.Of(SystemAccount.SevenSigns),
            () =>
            {
                leader.Inventory.Add(itemId, count);
                instance.OfferingScore += count * offeringValue;
                SaveInventory(leader.Session, leader.Inventory);
                return npc.NpcId;
            },
            _ => npc.NpcId);
    }
}