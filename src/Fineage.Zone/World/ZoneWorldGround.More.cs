using Fineage.Game.Economy;
using Fineage.Game.Services;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Drops;
using Fineage.Zone.Party;

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
        var receivers = new List<(ZoneEntity Member, int Amount)>();
        if (inRange.Count == 0)
            receivers.Add((picker, adena));
        else if (PartyLootRules.AdenaShare(adena, inRange.Count) is var share and > 0)
            receivers.AddRange(inRange.Select(m => (m, share)));

        var move = BeginLedgerMove(LedgerTxKind.Pickup, picker.ObjectId, ground.ObjectId);
        PostOffGround(move, ground);
        long received = 0;
        foreach (var (member, amount) in receivers)
        {
            if (member.Inventory == null || member.Session == null)
                continue;
            var bag = LedgerAccount.Inventory(member.ObjectId);
            ObserveLedger(move, bag);
            move.Post(bag, WarehouseRules.AdenaItemId, amount);
            received += amount;
        }

        move.Post(LedgerAccount.Of(SystemAccount.Destroyed), WarehouseRules.AdenaItemId, adena - received);
        if (move.CheckCommit() != LedgerRejection.None)
        {
            ZoneLog.Warn($"party adena pickup refused ground {ground.ObjectId}: ledger rejected the postings");
            return;
        }

        foreach (var (member, amount) in receivers)
        {
            if (member.Inventory == null || member.Session == null)
                continue;
            GrantInventoryStack(member, WarehouseRules.AdenaItemId, amount, notifyObtained: true);
        }

        RemoveGroundItem(ground);
        move.Commit(LedgerHoldingsOf);
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
        var bag = LedgerAccount.Inventory(looter.ObjectId);
        var move = BeginLedgerMove(LedgerTxKind.Pickup, looter.ObjectId, ground.ObjectId);
        ObserveLedger(move, bag);
        PostOffGround(move, ground);
        move.Post(bag, itemId, count, GroundPieces(ground));
        if (move.CheckCommit() != LedgerRejection.None)
        {
            ZoneLog.Warn($"pickup refused for object {looter.ObjectId} ground {ground.ObjectId}: ledger rejected the postings");
            return;
        }

        var added = AddGroundStack(looter.Inventory, ground);
        var invUpdate = new InventoryUpdateMsg
        {
            Changes = added
                .Select(row => InventoryMessages.Update(merged ? InventoryChange.Modify : InventoryChange.Add, row).Changes[0])
                .ToArray()
        };

        NetBridge?.FanoutInventoryUpdate([looter.Session.ObjectId], invUpdate);
        RemoveGroundItem(ground);
        move.Commit(LedgerHoldingsOf);
        SaveInventory(looter.Session, looter.Inventory);
        NotifyObtained(looter.Session, itemId, count);
        OnCursedWeaponGroundPickup(looter, ground);
    }

}
