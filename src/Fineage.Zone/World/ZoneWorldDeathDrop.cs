using Fineage.Game;
using Fineage.Game.Services;
using Fineage.Protocol;

namespace Fineage.Zone.World;

/// <summary>Mobius <c>Player.onDieDropItem</c>: inventory/equip items onto the death tile by karma/NPC rates.</summary>
public sealed partial class ZoneWorld
{
    /// <summary>
    /// Drop selected inventory rows at the corpse. Clan-war and PvP-zone exemptions are not modelled yet
    /// (no war / PVP zone data). Adena, quest items, and <c>death-drop.json</c> non-droppable ids are skipped.
    /// </summary>
    void OnDieDropItem(ZoneEntity player, int killerObjectId)
    {
        if (player.Inventory == null || player.Session == null)
            return;

        bool killerIsNpc = killerObjectId > 0
                             && _byId.TryGetValue(killerObjectId, out var killer)
                             && killer.Kind == ZoneEntityKind.Npc;
        var rates = _deathDrop.RatesFor(player.Karma, player.PkKills, killerIsNpc, player.Level);
        if (rates is not { } r)
            return;

        var candidates = new List<DeathDropRules.DropCandidate>(player.Inventory.Items.Count);
        foreach (var item in player.Inventory.Items)
        {
            if (!item.Template.CanDropTradeStore)
                continue;
            candidates.Add(new DeathDropRules.DropCandidate(
                item.ObjectId,
                item.ItemId,
                item.Count,
                item.IsEquipped,
                item.Template.Kind == ItemKind.Weapon));
        }

        var toDrop = _deathDrop.SelectDrops(candidates, r, _deathRandom);
        if (toDrop.Count == 0)
            return;

        var watchers = new List<ZoneEntity> { player };
        foreach (var other in Players())
        {
            if (ReferenceEquals(other, player))
                continue;
            if (other.Knownlist.Contains(player.ObjectId) || VisibleTo(other.ObjectId, player.ObjectId))
                watchers.Add(other);
        }

        var invChanges = new List<InventoryUpdateEntryMsg>();
        bool gearChanged = false;
        bool droppedWeapon = false;
        foreach (int objectId in toDrop)
        {
            var item = player.Inventory.Find(objectId);
            if (item == null)
                continue;
            if (item.IsEquipped)
            {
                gearChanged = true;
                if (item.Template.Kind == ItemKind.Weapon)
                    droppedWeapon = true;
                player.Inventory.Unequip(item);
            }

            int itemId = item.ItemId;
            int count = item.Count;
            invChanges.Add(InventoryMessages.RemoveRow(item, -1));
            player.Inventory.Remove(item);

            SpawnGroundStack(
                itemId,
                count,
                player.X,
                player.Y,
                player.Z,
                player.InstanceId,
                watchers,
                dropperObjectId: player.ObjectId);
            ZoneLog.Info($"die-drop object={player.ObjectId} item={itemId} count={count} karma={player.Karma} pk={player.PkKills}");
        }

        if (droppedWeapon)
        {
            player.SoulshotCharged = false;
            player.SpiritshotCharge = 0;
        }

        if (invChanges.Count > 0)
        {
            var update = new InventoryUpdateMsg { Changes = invChanges.ToArray() };
            NetBridge?.FanoutInventoryUpdate([player.Session.ObjectId], ToNetInventoryUpdate(update));
            SaveInventory(player.Session, player.Inventory);
        }

        if (gearChanged)
        {
            RefreshGradePenalty(player);
            RefreshRunSpeedAndFanout(player);
            SelfUserInfo(player);
            List<int>? netPeers = null;
            foreach (int id in player.Knownlist)
            {
                if (_byId.TryGetValue(id, out var other) && other.Session != null)
                    (netPeers ??= []).Add(id);
            }
            if (netPeers != null)
                NetBridge?.FanoutCharInfo(netPeers, player.ObjectId);
        }
    }

    /// <summary>Count of live ground stacks (death loot / bag drops).</summary>
    internal int GroundItemCountForTest()
    {
        lock (_gate)
            return _byId.Values.Count(e => e.Kind == ZoneEntityKind.GroundItem);
    }

    /// <summary>ObjectId of the first live ground stack (test helper).</summary>
    internal int FindGroundObjectIdForTest()
    {
        lock (_gate)
        {
            foreach (var e in _byId.Values)
            {
                if (e.Kind == ZoneEntityKind.GroundItem)
                    return e.ObjectId;
            }
        }
        return 0;
    }
}
