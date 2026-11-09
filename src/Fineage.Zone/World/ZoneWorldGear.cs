using Fineage.Game;
using Fineage.Protocol;
using Fineage.Zone.Inventory;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    /// <summary>
    /// Marks a live player dead or alive. Combat calls this when a player dies or revives.
    /// Players spawn alive with their saved HP. <see cref="ZoneEntity.CurHp"/> is not consulted:
    /// regen treats a low value as wounded, not dead.
    /// </summary>
    public bool TrySetPlayerDead(int objectId, bool dead)
    {
        lock (_gate)
        {
            if (!_byId.TryGetValue(objectId, out var entity) || entity.Kind != ZoneEntityKind.Player)
                return false;
            entity.Dead = dead;
            return true;
        }
    }

    public Task<string?> TryUseItemAsync(ZoneSession session, int objectId, CancellationToken ct)
    {
        string? error;
        lock (_gate)
            error = TryUseItem(session, objectId);
        if (error != null)
            return Task.FromResult<string?>(error);

        return Task.FromResult<string?>(null);
    }

    public Task<string?> TryUnequipItemAsync(ZoneSession session, int paperdollSlot, CancellationToken ct)
    {
        string? error;
        lock (_gate)
            error = TryUnequipItem(session, paperdollSlot);
        if (error != null)
            return Task.FromResult<string?>(error);

        return Task.FromResult<string?>(null);
    }

    public Task<string?> TryGiveItemAsync(ZoneSession session, int itemId, int count, CancellationToken ct)
    {
        string? error;
        lock (_gate)
            error = TryGiveItem(session, itemId, count);
        if (error != null)
            return Task.FromResult<string?>(error);

        return Task.FromResult<string?>(null);
    }

    string? TryUseItem(ZoneSession session, int objectId)
    {
        if (!TryLivePlayer(session, out var player, out var error))
            return error;

        var item = player.Inventory!.Find(objectId);
        if (GearRules.UseOwnedError(item != null) is { } ownedError)
            return ownedError;
        if (IsShot(item!.Template))
        {
            TryChargeShot(session, player, item, quiet: false);
            return null;
        }
        // Mobius UseItem: recipe scrolls register into the craft book; ItemSkills handle potions / SOE / shots.
        if (!item.Template.Equippable)
        {
            if (_game.Recipes.TryGetByRecipeItem(item.ItemId, out _))
                return TryLearnRecipeFromItem(session, player, item);
            return TryUseConsumable(session, player, item);
        }
        // Unequip skips conditions. There is no cast state, so weapon changes are not deferred.
        if (!item.IsEquipped && Restriction(player, item) is { } denied)
            return denied;

        var before = PaperdollSnapshot(player.Inventory);
        try
        {
            if (item.IsEquipped)
                player.Inventory.Unequip(item);
            else
                player.Inventory.Equip(item);
        }
        catch (InvalidOperationException)
        {
            return InventoryProtocol.Blocked;
        }

        if (item.Template.Kind == ItemKind.Weapon)
        {
            player.SoulshotCharged = false; // Mobius: charges live on the weapon instance
            player.SpiritshotCharge = 0;
            if (item.IsEquipped)
                RechargeAutoShots(player);
        }
        if (!PublishGear(session, player, before))
            return "bad_request";

        SaveInventory(session, player.Inventory);
        ZoneLog.Info($"gear object={session.ObjectId} use={objectId} equipped={item.IsEquipped}");
        return null;
    }

    string? TryUnequipItem(ZoneSession session, int paperdollSlot)
    {
        if (!TryLivePlayer(session, out var player, out var error))
            return error;
        if (GearRules.UnequipSlotError(paperdollSlot, (int)PaperdollSlot.Hair2) is { } slotError)
            return slotError;

        var item = player.Inventory!.GetEquipped((PaperdollSlot)paperdollSlot);
        if (GearRules.UnequipEmptyError(item != null) is { } emptyError)
            return emptyError;
        var liveItem = item!;

        var before = PaperdollSnapshot(player.Inventory);
        player.Inventory.Unequip(liveItem);
        if (liveItem.Template.Kind == ItemKind.Weapon)
        {
            player.SoulshotCharged = false;
            player.SpiritshotCharge = 0;
        }
        if (!PublishGear(session, player, before))
            return "bad_request";

        SaveInventory(session, player.Inventory);
        ZoneLog.Info($"gear object={session.ObjectId} unequip slot={paperdollSlot} item={liveItem.ObjectId}");
        return null;
    }

    string? TryGiveItem(ZoneSession session, int itemId, int count)
    {
        if (!IsLive(session))
            return "world";
        if (!_byId.TryGetValue(session.ObjectId, out var player) || player.Inventory == null)
            return "world";
        if (GearRules.GrantError(itemId, count, MaxGrantCount, _game.Items.TryGet(itemId, out _)) is { } giveError)
            return giveError;

        var added = player.Inventory.Add(itemId, count);
        // Net clients only read the PacketEnvelope path, so the V2 frame alone would leave the bag
        // stale for an admin GiveItem.
        NetBridge?.FanoutInventoryUpdate(
            [session.ObjectId],
            InventoryMessages.Update(InventoryChange.Add, added));
        SaveInventory(session, player.Inventory);
        ZoneLog.Info($"give object={session.ObjectId} item={itemId} count={count}");
        return null;
    }

    bool TryLivePlayer(ZoneSession session, out ZoneEntity player, out string? error)
    {
        player = null!;
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var entity) || entity.Inventory == null)
        {
            error = "world";
            return false;
        }

        if (entity.Dead)
        {
            error = InventoryProtocol.Dead;
            return false;
        }

        player = entity;
        error = null;
        return true;
    }

}
