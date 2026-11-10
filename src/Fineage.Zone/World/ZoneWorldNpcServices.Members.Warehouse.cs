using Fineage.Game;
using Fineage.Game.Services;
using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Inventory;
using Fineage.Zone.NpcHtml;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    void SendDepositList(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        var items = new List<ItemListEntryMsg>();
        if (player.Inventory != null)
        {
            foreach (var item in player.Inventory.Items)
            {
                if (!item.IsEquipped && item.Template.CanDropTradeStore)
                    items.Add(InventoryMessages.Entry(item));
            }
        }

        var list = new WarehouseDepositListMsg
        {
            NpcObjectId = npc.ObjectId,
            FeePerLine = WarehouseRules.DepositFeePerLine,
            SlotsUsed = player.Warehouse?.SlotCount ?? 0,
            SlotsMax = WarehouseRules.WarehouseSlots(player.Race),
            Adena = player.Inventory?.CountOf(WarehouseRules.AdenaItemId) ?? 0,
            Items = items.ToArray()
        };
        if (NetBridge != null)
            NetBridge.FanoutWarehouseDepositList([session.ObjectId], list);
    }

    void SendWithdrawList(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        var items = new List<ItemListEntryMsg>();
        if (player.Warehouse != null)
        {
            foreach (var stack in player.Warehouse.Items)
                items.Add(StackEntry(stack));
        }

        var list = new WarehouseWithdrawListMsg
        {
            NpcObjectId = npc.ObjectId,
            SlotsUsed = player.Warehouse?.SlotCount ?? 0,
            SlotsMax = WarehouseRules.WarehouseSlots(player.Race),
            InventorySlotsUsed = player.Inventory?.Items.Count ?? 0,
            InventorySlotsMax = WarehouseRules.InventorySlots(player.Race),
            CurrentWeight = player.Inventory == null ? 0 : _weights.Load(player.Inventory.Items.Select(item => (item.ItemId, item.Count))),
            MaxWeight = MaxLoadOf(player),
            Items = items.ToArray()
        };
        if (NetBridge != null)
            NetBridge.FanoutWarehouseWithdrawList([session.ObjectId], list);
    }

    ItemListEntryMsg StackEntry(WarehouseStack stack)
    {
        var template = _game.Items.Get(stack.ItemId);
        return new ItemListEntryMsg
        {
            ObjectId = stack.ObjectId,
            ItemId = stack.ItemId,
            Count = stack.Count,
            EquippedSlot = -1,
            BodySlot = "none",
            Kind = template.Kind switch
            {
                ItemKind.Weapon => "weapon",
                ItemKind.Armor => "armor",
                ItemKind.Etc => "etc",
                _ => "etc"
            },
            Icon = template.Icon,
            WeaponType = "",
            ArmorType = "",
            EnchantLevel = stack.EnchantLevel
        };
    }

    void CastBuff(
        ZoneEntity npc,
        ZoneEntity player,
        int skillId,
        int level,
        int abnormalLevel,
        int durationSeconds)
    {
        if (player.Session == null)
            return;
        long now = _clock().ToUnixTimeMilliseconds();
        ZoneAbnormals.Apply(player.Abnormals, skillId, abnormalLevel, durationSeconds, now, level);
        var cast = new MagicSkillUseMsg
        {
            CasterObjectId = npc.ObjectId,
            TargetObjectId = player.ObjectId,
            SkillId = skillId,
            SkillLevel = level,
            HitTimeMs = 0,
            ReuseDelayMs = 0
        };
                // Net clients only see the gesture through the bridge; the abnormal row alone leaves the pawn silent.
        NetBridge?.FanoutMagicSkillUse([player.ObjectId], cast);
        foreach (var other in Players())
        {
            if (other.Session == null || ReferenceEquals(other, player))
                continue;
            if (!other.Knownlist.Contains(player.ObjectId) && !other.Knownlist.Contains(npc.ObjectId))
                continue;
        }
    }
}
