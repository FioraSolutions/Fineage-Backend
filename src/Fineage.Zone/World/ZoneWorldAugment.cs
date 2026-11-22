using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Game.Shops;
using Fineage.Protocol;
using Fineage.Zone.Augmentation;
using Fineage.Zone.Inventory;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public Task<string?> TryAugmentConfirmTargetAsync(ZoneSession session, int targetObjectId, CancellationToken ct) =>
        Task.FromResult(WithDeferredInventoryPersist(() => TryAugmentConfirmTarget(session, targetObjectId)));

    public Task<string?> TryAugmentConfirmRefinerAsync(ZoneSession session, int targetObjectId, int refinerObjectId, CancellationToken ct) =>
        Task.FromResult(TryAugmentConfirmRefiner(session, targetObjectId, refinerObjectId));

    public Task<string?> TryAugmentConfirmGemAsync(ZoneSession session, int targetObjectId, int refinerObjectId, int gemObjectId, long gemCount, CancellationToken ct) =>
        Task.FromResult(TryAugmentConfirmGem(session, targetObjectId, refinerObjectId, gemObjectId, gemCount));

    public Task<string?> TryAugmentRefineAsync(ZoneSession session, int targetObjectId, int refinerObjectId, int gemObjectId, long gemCount, CancellationToken ct) =>
        Task.FromResult(WithDeferredInventoryPersist(() => TryAugmentRefine(session, targetObjectId, refinerObjectId, gemObjectId, gemCount)));

    public Task<string?> TryAugmentRefineCancelAsync(ZoneSession session, int targetObjectId, CancellationToken ct) =>
        Task.FromResult(WithDeferredInventoryPersist(() => TryAugmentRefineCancel(session, targetObjectId)));

    public Task<string?> TryAugmentCancelWindowAsync(ZoneSession session, CancellationToken ct) =>
        Task.FromResult(TryAugmentCancelWindow(session));

    public Task<string?> TryAugmentOpenMakeAsync(ZoneSession session, CancellationToken ct) =>
        Task.FromResult(TryAugmentOpenMake(session));

    public Task<string?> TryAugmentOpenCancelAsync(ZoneSession session, CancellationToken ct) =>
        Task.FromResult(TryAugmentOpenCancel(session));

    string? AugmentGate(ZoneSession session, out ZoneEntity? player)
    {
        player = null;
        if (!AugmentationRules.CanRefine)
            return AugmentationProtocol.Disabled;
        if (!TryLivePlayer(session, out player, out var error))
            return error;
        if (player!.Dead)
            return AugmentationProtocol.Dead;
        if (player.PrivateStoreType != PrivateStoreProtocol.TypeNone || _tradeByPlayer.ContainsKey(player.ObjectId))
            return AugmentationProtocol.Store;
        if (player.Fishing)
            return AugmentationProtocol.Busy;
        if (player.ActiveEnchantObjectId != 0)
            return AugmentationProtocol.Busy;
        return null;
    }

    string? TryAugmentOpenMake(ZoneSession session)
    {
        if (AugmentGate(session, out var player) is { } gate)
            return gate;
        player!.AugmentCancelWindow = false;
        NetBridge?.FanoutAugmentShowMakeWindow([session.ObjectId]);
        return null;
    }

    string? TryAugmentOpenCancel(ZoneSession session)
    {
        if (AugmentGate(session, out var player) is { } gate)
            return gate;
        player!.AugmentCancelWindow = true;
        NetBridge?.FanoutAugmentShowCancelWindow([session.ObjectId]);
        return null;
    }

    string? TryAugmentCancelWindow(ZoneSession session)
    {
        if (!TryLivePlayer(session, out var player, out var error))
            return error;
        player.AugmentCancelWindow = false;
        return null;
    }

    string? TryAugmentConfirmTarget(ZoneSession session, int targetObjectId)
    {
        if (AugmentGate(session, out var player) is { } gate)
            return gate;
        var target = player!.Inventory!.Find(targetObjectId);
        if (!AugmentationRules.ValidateTarget(target))
        {
            if (target != null && AugmentationRules.IsAugmented(target))
                return AugmentationProtocol.AlreadyAugmented;
            return AugmentationProtocol.NotSuitable;
        }

        NetBridge?.FanoutAugmentPutItemResult([session.ObjectId], new VariationPutItemMsg
        {
            TargetObjectId = target!.ObjectId,
            ItemId = target.ItemId
        });
        return null;
    }

    string? TryAugmentConfirmRefiner(ZoneSession session, int targetObjectId, int refinerObjectId)
    {
        if (AugmentGate(session, out var player) is { } gate)
            return gate;
        var inventory = player!.Inventory!;
        var target = inventory.Find(targetObjectId);
        var refiner = inventory.Find(refinerObjectId);
        if (!ValidateRefiner(player, target, refiner))
            return AugmentationProtocol.NotSuitable;

        NetBridge?.FanoutAugmentPutRefinerResult([session.ObjectId], new VariationPutRefinerMsg
        {
            RefinerObjectId = refiner!.ObjectId,
            ItemId = target!.ItemId,
            RefinerId = refiner.ItemId
        });
        return null;
    }

    string? TryAugmentConfirmGem(ZoneSession session, int targetObjectId, int refinerObjectId, int gemObjectId, long gemCount)
    {
        if (AugmentGate(session, out var player) is { } gate)
            return gate;
        var inventory = player!.Inventory!;
        var target = inventory.Find(targetObjectId);
        var refiner = inventory.Find(refinerObjectId);
        var gem = inventory.Find(gemObjectId);
        if (!ValidateRefiner(player, target, refiner) || !ValidateGem(target!, refiner!, gem, gemCount))
            return AugmentationProtocol.NotSuitable;

        NetBridge?.FanoutAugmentPutGemResult([session.ObjectId], new VariationPutGemMsg
        {
            GemstoneObjectId = gem!.ObjectId,
            RefinerItemId = refiner!.ItemId,
            GemstoneCount = gemCount
        });
        return null;
    }

    string? TryAugmentRefine(ZoneSession session, int targetObjectId, int refinerObjectId, int gemObjectId, long gemCount)
    {
        if (AugmentGate(session, out var player) is { } gate)
            return gate;
        var inventory = player!.Inventory!;
        var target = inventory.Find(targetObjectId);
        var refiner = inventory.Find(refinerObjectId);
        var gem = inventory.Find(gemObjectId);
        if (!ValidateRefiner(player, target, refiner) || !ValidateGem(target!, refiner!, gem, gemCount))
        {
            FailVariation(session);
            return AugmentationProtocol.NotSuitable;
        }

        if (!AugmentationLifeStones.TryGet(refiner!.ItemId, out var ls))
        {
            FailVariation(session);
            return AugmentationProtocol.NotSuitable;
        }

        var before = PaperdollSnapshot(inventory);
        bool wasEquipped = target!.IsEquipped;
        if (wasEquipped)
            inventory.Unequip(target);

        RecordPlayerBagChange(
            player,
            LedgerTxKind.Augment,
            LedgerAccount.Of(SystemAccount.Fees),
            () =>
            {
                int refinerLeft = inventory.TakeOne(refiner);
                bool gemRemoved = inventory.TryRemoveFromStack(gem!, (int)gemCount, out bool gemGone);

                var (opt1, opt2) = AugmentationRoller.RollRetail(
                    _augmentation,
                    Rng(_combatRandom, player),
                    target.Template,
                    refiner.ItemId,
                    ls.Grade);

                inventory.SetAugmentation(target, opt1, opt2);

                var changes = new List<InventoryUpdateEntryMsg>
                {
                    refinerLeft == 0
                        ? InventoryMessages.RemoveRow(refiner, -1)
                        : InventoryMessages.ChangeRow(InventoryChange.Modify, refiner),
                    gemRemoved
                        ? gemGone
                            ? InventoryMessages.RemoveRow(gem!, -1)
                            : InventoryMessages.ChangeRow(InventoryChange.Modify, gem!)
                        : InventoryMessages.ChangeRow(InventoryChange.Modify, gem!),
                    InventoryMessages.ChangeRow(InventoryChange.Modify, target)
                };

                foreach (var other in PaperdollChanges(inventory, before))
                {
                    if (other.ObjectId == target.ObjectId)
                        continue;
                    changes.Add(InventoryMessages.ChangeRow(InventoryChange.Modify, other));
                }

                NetBridge?.FanoutAugmentVariationResult([session.ObjectId], new VariationResultMsg
                {
                    Option1 = opt1,
                    Option2 = opt2,
                    Success = AugmentationProtocol.VariationSuccess
                });
                RefreshGearSkills(player);
                SelfUserInfo(player);
                SaveInventory(session, inventory);
                return true;
            },
            _ => refiner.ItemId);

        return null;
    }

    string? TryAugmentRefineCancel(ZoneSession session, int targetObjectId)
    {
        if (AugmentGate(session, out var player) is { } gate)
            return gate;
        var target = player!.Inventory!.Find(targetObjectId);
        if (target == null)
        {
            NetBridge?.FanoutAugmentCancelResult([session.ObjectId], new VariationCancelResultMsg());
            return AugmentationProtocol.NotSuitable;
        }

        if (!AugmentationRules.IsAugmented(target))
        {
            NetBridge?.FanoutAugmentCancelResult([session.ObjectId], new VariationCancelResultMsg());
            return AugmentationProtocol.NotAugmented;
        }

        int fee = AugmentationRules.CancelAdenaFee(target.Template);
        var inventory = player.Inventory!;
        if (fee <= 0 || inventory.CountOf(ShopTrade.AdenaItemId) < fee)
        {
            NetBridge?.FanoutAugmentCancelResult([session.ObjectId], new VariationCancelResultMsg());
            return AugmentationProtocol.NoAdena;
        }

        if (target.IsEquipped)
            inventory.Unequip(target);

        RecordPlayerBagChange(
            player,
            LedgerTxKind.Augment,
            LedgerAccount.Of(SystemAccount.Fees),
            () =>
            {
                var changes = new List<InventoryUpdateEntryMsg>();
                Take(inventory, ShopTrade.AdenaItemId, fee, changes);
                inventory.ClearAugmentation(target);
                changes.Add(InventoryMessages.ChangeRow(InventoryChange.Modify, target));
                NetBridge?.FanoutAugmentCancelResult([session.ObjectId], new VariationCancelResultMsg { RefundAdena = 1 });
                NetBridge?.FanoutAugmentPutCancelItemResult([session.ObjectId], new VariationCancelItemMsg
                {
                    TargetObjectId = target.ObjectId,
                    ItemId = target.ItemId,
                    Result = 7
                });
                RefreshGearSkills(player);
                SelfUserInfo(player);
                SaveInventory(session, inventory);
                return true;
            },
            _ => target.ItemId);

        return null;
    }

    void FailVariation(ZoneSession session) =>
        NetBridge?.FanoutAugmentVariationResult([session.ObjectId], new VariationResultMsg
        {
            Success = AugmentationProtocol.VariationFail
        });

    static bool ValidateRefiner(ZoneEntity player, ItemInstance? target, ItemInstance? refiner)
    {
        if (!AugmentationRules.ValidateTarget(target) || refiner == null || refiner.IsEquipped)
            return false;
        if (!AugmentationLifeStones.TryGet(refiner.ItemId, out var ls))
            return false;
        if (player.Level < ls.MinPlayerLevel)
            return false;
        if (AugmentationRules.IsAugmentableWeapon(target!.Template))
        {
            if (ls.Grade == AugmentationLifeStones.GradeAcc)
                return false;
        }
        else if (AugmentationRules.IsAugmentableAccessory(target.Template))
        {
            if (ls.Grade != AugmentationLifeStones.GradeAcc)
                return false;
        }
        else
        {
            return false;
        }

        return true;
    }

    static bool ValidateGem(ItemInstance target, ItemInstance refiner, ItemInstance? gem, long gemCount)
    {
        if (gem == null || gem.IsEquipped || gemCount < 1)
            return false;
        if (!AugmentationLifeStones.TryGet(refiner.ItemId, out var ls))
            return false;
        int need = AugmentationLifeStones.GetGemStoneCount(target.Template.Crystal, ls.Grade);
        if (need <= 0 || gemCount != need || gem.Count < need)
            return false;
        return AugmentationLifeStones.GetGemStoneId(target.Template.Crystal) == gem.ItemId;
    }
}