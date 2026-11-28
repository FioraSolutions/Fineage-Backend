using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Protocol;
using Fineage.Zone.Inventory;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public Task<string?> TryCrystallizeItemAsync(ZoneSession session, int objectId, int count, CancellationToken ct)
    {
        _ = ct;
        if (!IsLive(session))
            return Task.FromResult<string?>("world");
        using (_affinity.Step())
            return Task.FromResult(TryCrystallizeItem(session, objectId, count));
    }

    internal string? TryCrystallizeItem(ZoneSession session, int objectId, int count)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var player) || player.Inventory == null)
            return "world";
        if (player.Dead)
            return InventoryProtocol.Dead;
        if (objectId <= 0 || count < 1)
        {
            ZoneLog.Error($"[crystallize] obj={player.ObjectId} count={count} objectId={objectId} is not a crystallize request");
            return "bad_request";
        }

        if (player.PrivateStoreType != PrivateStoreProtocol.TypeNone || _tradeByPlayer.ContainsKey(player.ObjectId))
        {
            SelfLine(session, CrystallizeProtocol.StoreBlocked,
                "While operating a private store or workshop, you cannot discard, destroy, or trade an item.");
            return CrystallizeProtocol.StoreOpen;
        }

        var item = player.Inventory.Find(objectId);
        if (item == null)
            return InventoryProtocol.NotOwned;
        if (count > item.Count)
            count = item.Count;

        if (item.IsAugmented || CrystallizeRules.IsHeroOnly(item.Template))
        {
            ZoneLog.Error($"[crystallize] item {item.ItemId} obj={item.ObjectId} cannot be crystallized (augmented={item.IsAugmented} hero={CrystallizeRules.IsHeroOnly(item.Template)})");
            return CrystallizeProtocol.NotCrystallizable;
        }

        if (!EnchantRules.IsCrystallizable(item.Template))
        {
            ZoneLog.Error($"[crystallize] item {item.ItemId} is not crystallizable (grade={item.Template.Crystal} crystalCount={item.Template.CrystalCount})");
            return CrystallizeProtocol.NotCrystallizable;
        }

        int skillLevel = CrystallizeSkillLevel(player);
        if (!CrystallizeRules.SkillAllows(item.Template.Crystal, skillLevel))
        {
            ZoneLog.Error($"[crystallize] item {item.ItemId} grade={item.Template.Crystal} needs skill {CrystallizeRules.SkillId} level {CrystallizeRules.RequiredSkillLevel(item.Template.Crystal)}, have {skillLevel}");
            SelfLine(session, CrystallizeProtocol.SkillLow, "You may not crystallize this item. Your crystallization skill level is too low.");
            return CrystallizeProtocol.SkillTooLow;
        }

        int crystalId = EnchantRules.CrystalItemId(item.Template.Crystal);
        if (crystalId == 0)
        {
            ZoneLog.Error($"[crystallize] item {item.ItemId} grade={item.Template.Crystal} has no crystal item");
            return CrystallizeProtocol.MissingCrystal;
        }

        var crystalTemplate = _game.Items.Require(crystalId);

        int yield = CrystallizeRules.Yield(item.Template, item.EnchantLevel, count);
        if (yield < 1)
        {
            ZoneLog.Error($"[crystallize] item {item.ItemId} enchant={item.EnchantLevel} count={count} produced no crystals");
            return CrystallizeProtocol.NotCrystallizable;
        }

        int itemId = item.ItemId;
        string itemName = item.Template.Name;
        int enchant = item.EnchantLevel;
        bool wasEquipped = item.IsEquipped;
        int wornSlot = item.EquippedSlot is { } slot ? (int)slot : -1;
        var inventory = player.Inventory;
        bool done = RecordPlayerBagChange(
            player,
            LedgerTxKind.Crystallize,
            LedgerAccount.Of(SystemAccount.Destroyed),
            () =>
            {
                var changes = new List<InventoryUpdateEntryMsg>();
                if (wasEquipped)
                {
                    inventory.Unequip(item);
                    SelfLine(session, enchant > 0 ? EnchantProtocol.EquipmentRemoved : EnchantProtocol.Disarmed,
                        enchant > 0
                            ? $"The equipment, +{enchant} {itemName}, has been removed."
                            : $"{itemName} has been disarmed.",
                        enchant > 0 ? [enchant.ToString(), itemName] : [itemName]);
                }

                if (!inventory.TryReduce(item, count, out _))
                {
                    ZoneLog.Error($"[crystallize] item {itemId} obj={objectId} could not be removed");
                    return false;
                }

                changes.Add(item.Count == 0
                    ? InventoryMessages.RemoveRow(item, wasEquipped ? wornSlot : -1)
                    : InventoryMessages.ChangeRow(InventoryChange.Modify, item));

                bool stacked = inventory.FindByItemId(crystalId) != null;
                var stack = inventory.Add(crystalId, yield);
                changes.Add(InventoryMessages.ChangeRow(stacked ? InventoryChange.Modify : InventoryChange.Add, stack));

                NetBridge?.FanoutInventoryUpdate([session.ObjectId], new InventoryUpdateMsg { Changes = changes.ToArray() });
                SelfLine(session, CrystallizeProtocol.Crystallized, "$s1 has been crystallized.", itemName);
                SelfLine(session, CrystallizeProtocol.Earned, $"You have earned {yield} {crystalTemplate.Name}(s).",
                    crystalTemplate.Name, yield.ToString());
                SaveInventory(session, inventory);
                if (wasEquipped)
                    SelfUserInfo(player);
                return true;
            },
            _ => itemId);
        if (!done)
            return CrystallizeProtocol.NotCrystallizable;

        ZoneLog.Info($"crystallize object={session.ObjectId} item={itemId} x{count} crystals={crystalId} x{yield}");
        return null;
    }

    static int CrystallizeSkillLevel(ZoneEntity player)
    {
        if (player.LearnedSkills.TryGetValue(CrystallizeRules.SkillId, out int learned) && learned > 0)
            return learned;
        if (player.KnownSkills.TryGetValue(CrystallizeRules.SkillId, out int known) && known > 0)
            return known;
        return 0;
    }
}
