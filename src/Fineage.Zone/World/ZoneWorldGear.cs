using Fineage.Game;
using Fineage.Game.Economy;
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
        using (_affinity.Step())
        {
            if (!_byId.TryGetValue(objectId, out var entity) || entity.Kind != ZoneEntityKind.Player)
                return false;
            entity.Dead = dead;
            return true;
        }
    }

    public Task<string?> TryUseItemAsync(ZoneSession session, int objectId, CancellationToken ct)
    {
        // The Postgres inventory write runs after the change, once per step (B8).
        return Task.FromResult(WithDeferredInventoryPersist(() => TryUseItem(session, objectId)));
    }

    public Task<string?> TryUnequipItemAsync(ZoneSession session, int paperdollSlot, CancellationToken ct)
    {
        return Task.FromResult(WithDeferredInventoryPersist(() => TryUnequipItem(session, paperdollSlot)));
    }

    public Task<string?> TryGiveItemAsync(ZoneSession session, int itemId, int count, CancellationToken ct)
    {
        return Task.FromResult(WithDeferredInventoryPersist(() => TryGiveItem(session, itemId, count)));
    }

    string? TryUseItem(ZoneSession session, int objectId)
    {
        if (!TryLivePlayer(session, out var player, out var error))
            return error;

        var item = player.Inventory!.Find(objectId);
        if (GearRules.UseOwnedError(item != null) is { } ownedError)
            return ownedError;
        if (OlympiadItemRefused(player, item!) is { } olympiadError)
            return olympiadError;
        // Mobius UseItem: nothing but fishing shots while the line is out.
        if (player.Fishing && !FishingProtocol.IsFishShot(item!.ItemId))
        {
            SelfLine(session, FishingProtocol.CannotWhileFishingUse, "You cannot do that while fishing.");
            return FishingProtocol.Busy;
        }
        if (FishingProtocol.IsFishShot(item!.ItemId))
            return TryChargeFishShot(session, player, item);
        if (IsShot(item!.Template))
        {
            TryChargeShot(session, player, item, quiet: false);
            return null;
        }
        // Mobius UseItem: recipe scrolls register into the craft book; ItemSkills handle potions / SOE / shots.
        if (_manor.Board.Data.Seed(item.ItemId) is { } seed)
            return TryUseSeed(session, player, item, seed);
        if (item.ItemId == ManorProtocol.HarvesterItemId)
            return TryUseHarvester(session, player);
        if (_pets.ByItem(item.ItemId) is { } petTemplate)
            return TryUseCollar(session, player, item, petTemplate);
        if (item.ItemId == PetResurrectScrollId)
            return TryUsePetResurrectScroll(session, player, item);
        if (_soulCrystals.IsCrystal(item.ItemId))
            return TryUseSoulCrystal(session, player, item);
        if (MercTicketIsSiegeGuardItem(item.ItemId))
            return TryUseMercTicket(session, player, item);
        if (_game.TransferSkillTrees.IsKnownCertificate(item.ItemId))
            return TryUseTransferCertificate(session, player, item);
        // Mobius ItemHandler table (Book / Maps / RollingDice / Calculator / SpecialXMas).
        if (TryNamedItemHandler(session, player, item) is { } handlerError)
            return handlerError;
        if (_itemHandlers.IsCatalogued(item.ItemId))
            return null;
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
            player.FishShotCharged = false;
            if (item.IsEquipped)
                RechargeAutoShots(player);
        }
        if (PaperdollChanges(player.Inventory, before).Count == 0)
            return "bad_request";

        SaveInventory(session, player.Inventory);
        if (!PublishGear(session, player, before))
            return "bad_request";
        if (item.IsEquipped)
            OnCursedWeaponEquipped(session, player, item);
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
            player.FishShotCharged = false;
        }
        if (PaperdollChanges(player.Inventory, before).Count == 0)
            return "bad_request";

        SaveInventory(session, player.Inventory);
        if (!PublishGear(session, player, before))
            return "bad_request";
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

        RecordBagChange(
            player,
            LedgerTxKind.GmGrant,
            GmGrantSource,
            () =>
            {
                player.Inventory.Add(itemId, count);
                return count;
            },
            static given => given);
        SaveInventory(session, player.Inventory);
        ZoneLog.Info($"give object={session.ObjectId} item={itemId} count={count}");
        return null;
    }

    /// <summary>
    /// Grants a free (price 0) GM buylist row. The admin shop must honour the exact requested amount:
    /// Mobius <c>admin_buy</c> on the currency page is used to mint millions of adena/gold, and the
    /// <see cref="MaxGrantCount"/> cap that guards the chat <c>admin_create_item</c> verb would refuse it.
    /// The amount is applied in capped chunks so one purchase still lands as a single mutation.
    /// </summary>
    string? TryGiveAdminFreeItem(ZoneSession session, int itemId, int count)
    {
        if (!IsLive(session))
            return "world";
        if (!_byId.TryGetValue(session.ObjectId, out var player) || player.Inventory == null)
            return "world";
        if (itemId <= 0 || count <= 0 || !_game.Items.TryGet(itemId, out _))
            return "bad_request";

        RecordBagChange(
            player,
            LedgerTxKind.GmGrant,
            GmGrantSource,
            () =>
            {
                int left = count;
                while (left > 0)
                {
                    int chunk = Math.Min(left, MaxGrantCount);
                    player.Inventory.Add(itemId, chunk);
                    left -= chunk;
                }

                return count;
            },
            static given => given);
        SaveInventory(session, player.Inventory);
        ZoneLog.Info($"admin give object={session.ObjectId} item={itemId} count={count}");
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
