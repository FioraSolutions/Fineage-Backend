using Fineage.Game;
using Fineage.Game.Shops;
using Fineage.Protocol;
using Fineage.Zone.Shops;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    string? Preview(ZoneSession session, int npcObjectId)
    {
        if (!TryShopNpc(session, npcObjectId, out _, out _, out var error))
            return error;
        return null;
    }

    bool TryShopNpc(ZoneSession session, int npcObjectId, out ZoneEntity player, out ZoneEntity npc, out string? error)
    {
        player = null!;
        npc = null!;
        error = null;
        if (!TryPlayer(session, out player!) || player.Inventory == null)
        {
            error = "world";
            return false;
        }

        if (player.Dead)
        {
            error = InventoryProtocol.Dead;
            return false;
        }

        if (!_byId.TryGetValue(npcObjectId, out npc!) || npc.Kind != ZoneEntityKind.Npc)
        {
            error = QuestProtocol.Npc;
            return false;
        }

        if (npc.Attackable)
        {
            error = NpcHtmlProtocol.Attackable;
            return false;
        }

        if (!InTalkRange(player, npc))
        {
            error = "range";
            return false;
        }

        return true;
    }

    bool CanSell(ZoneEntity npc) =>
        npc.NpcType.Equals("Merchant", StringComparison.OrdinalIgnoreCase) ||
        npc.NpcType.Equals("Fisherman", StringComparison.OrdinalIgnoreCase) ||
        _buyLists.NpcListed(npc.NpcId);

    void Tax(ZoneEntity npc, out int baseTax, out int castleTax, out bool owned)
    {
        baseTax = Math.Clamp(npc.MerchantBaseTaxPercent, 0, 100);
        castleTax = 0;
        owned = false;
        if (npc.MerchantCastleId <= 0)
            return;
        var castle = _siege.Find(npc.MerchantCastleId);
        if (castle == null || castle.OwnerClanId <= 0)
            return;
        owned = true;
        castleTax = Math.Clamp(castle.TaxPercent, 0, 100);
    }

    BuyListMsg BuyList(ZoneEntity npc, BuyListDefinition list, int baseTax, int castleTax)
    {
        long now = _clock().ToUnixTimeMilliseconds();
        var items = new BuyListItemMsg[list.Products.Count];
        for (int i = 0; i < items.Length; i++)
        {
            var product = list.Products[i];
            int price = product.Price;
            if (product.ItemId >= ShopTrade.SiegeGuardFirst && product.ItemId <= ShopTrade.SiegeGuardLast)
                price = (int)(price * ShopTrade.SiegeGuardPriceRate);
            items[i] = new BuyListItemMsg
            {
                ItemId = product.ItemId,
                Price = Math.Max(0, ShopTrade.TaxedUnitPrice(price, baseTax, castleTax)),
                Count = product.Limited ? _shopStock.Available(list.Id, product.ItemId, now) : -1
            };
        }

        return new BuyListMsg
        {
            NpcObjectId = npc.ObjectId,
            ListId = list.Id,
            BaseTaxPercent = baseTax,
            CastleTaxPercent = castleTax,
            Items = items
        };
    }

    static SellListMsg SellList(ZoneEntity npc, ZoneEntity player)
    {
        var rows = new List<SellListItemMsg>();
        foreach (var item in player.Inventory!.Items)
        {
            if (item.IsEquipped || !item.Template.CanSell)
                continue;
            rows.Add(new SellListItemMsg
            {
                ObjectId = item.ObjectId,
                ItemId = item.ItemId,
                Count = item.Count,
                Price = item.Template.ReferencePrice / 2,
                EnchantLevel = item.EnchantLevel
            });
        }

        return new SellListMsg { NpcObjectId = npc.ObjectId, Items = rows.ToArray() };
    }

    static MultiSellListMsg MultiList(ZoneEntity npc, PreparedMultiSell prepared, int castleTax)
    {
        var entries = new MultiSellEntryMsg[prepared.Entries.Count];
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = prepared.Entries[i];
            entries[i] = new MultiSellEntryMsg
            {
                EntryId = entry.EntryId,
                Ingredients = Parts(entry.Ingredients),
                Products = Parts(entry.Products)
            };
        }

        return new MultiSellListMsg
        {
            NpcObjectId = npc.ObjectId,
            ListId = prepared.ListId,
            MaintainEnchantment = prepared.MaintainEnchantment,
            InventoryOnly = prepared.InventoryOnly,
            CastleTaxPercent = castleTax,
            Entries = entries
        };
    }

    static MultiSellPartMsg[] Parts(IReadOnlyList<PreparedShopPart> parts)
    {
        var rows = new MultiSellPartMsg[parts.Count];
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = new MultiSellPartMsg
            {
                ItemId = parts[i].ItemId,
                Count = parts[i].Count,
                EnchantLevel = Math.Max(0, parts[i].Enchant)
            };
        }

        return rows;
    }

    ShopBalanceMsg Balance(ZoneEntity player, ZoneEntity npc)
    {
        Tax(npc, out int baseTax, out int castleTax, out _);
        long weight = player.Inventory!.TotalWeight;
        return new ShopBalanceMsg
        {
            Adena = player.Inventory.CountOf(ShopTrade.AdenaItemId),
            Weight = weight > int.MaxValue ? int.MaxValue : (int)weight,
            MaxWeight = MaxLoadOf(player),
            Slots = player.Inventory.Items.Count,
            MaxSlots = ShopTrade.SlotLimit(player.Race),
            BaseTaxPercent = baseTax,
            CastleTaxPercent = castleTax
        };
    }

    void SendTouches(ZoneSession session, ShopMutation mutation)
    {
        if (mutation.Touches.Count == 0)
            return;
        var changes = new InventoryUpdateEntryMsg[mutation.Touches.Count];
        for (int i = 0; i < changes.Length; i++)
        {
            var touch = mutation.Touches[i];
            int kind = touch.Kind switch
            {
                ShopTouchKind.Add => InventoryChange.Add,
                ShopTouchKind.Remove => InventoryChange.Remove,
                _ => InventoryChange.Modify
            };
            changes[i] = InventoryMessages.ChangeRow(kind, touch.Item);
        }

        var invUpdate = new InventoryUpdateMsg { Changes = changes };
        if (NetBridge != null)
            NetBridge.FanoutInventoryUpdate([session.ObjectId], invUpdate);
    }

    void SendComplete(ZoneSession session)
    {
        var message = Line(ShopProtocol.TransactionComplete, ShopProtocol.TransactionCompleteText);
        FanNetSystem(session, message);
    }

    void SendDeny(ZoneSession session, ShopMutation mutation)
    {
        SystemMessageMsg message = mutation.Deny switch
        {
            ShopDeny.Adena => Line(ShopProtocol.NotEnoughAdena, ShopProtocol.NotEnoughAdenaText),
            ShopDeny.Weight => Line(ShopProtocol.WeightLimit, ShopProtocol.WeightLimitText),
            ShopDeny.Slots => Line(ShopProtocol.InventoryFull, ShopProtocol.InventoryFullText),
            ShopDeny.Quantity => Line(ShopProtocol.Quantity, ShopProtocol.QuantityText),
            ShopDeny.Pocket => Line(ShopProtocol.PocketLimit, ShopProtocol.PocketLimitText),
            ShopDeny.Stock => Line(ShopProtocol.OutOfStock, ShopProtocol.OutOfStockText),
            ShopDeny.Ingredients => new SystemMessageMsg
            {
                Id = ShopProtocol.Ingredients,
                Params = [mutation.MissingCount.ToString(), mutation.MissingName],
                Text = $"{mutation.MissingCount} unit(s) of the item {mutation.MissingName} is/are required."
            },
            _ => Line(ShopProtocol.NotTradable, ShopProtocol.NotTradableText)
        };
        FanNetSystem(session, message);
    }

    static SystemMessageMsg Line(int id, string text) => new() { Id = id, Params = [], Text = text };

    void FanNetBuy(ZoneSession session, BuyListMsg buy, ShopBalanceMsg balance)
    {
        if (NetBridge == null)
            return;
        int[] self = [session.ObjectId];
        NetBridge.FanoutBuyList(self, buy);
        NetBridge.FanoutShopBalance(self, balance);
    }

    void FanNetSell(ZoneSession session, SellListMsg sell, ShopBalanceMsg balance)
    {
        if (NetBridge == null)
            return;
        int[] self = [session.ObjectId];
        NetBridge.FanoutSellList(self, sell);
        NetBridge.FanoutShopBalance(self, balance);
    }

    void FanNetMulti(ZoneSession session, MultiSellListMsg multi, ShopBalanceMsg balance)
    {
        if (NetBridge == null)
            return;
        int[] self = [session.ObjectId];
        NetBridge.FanoutMultiSellList(self, multi);
        NetBridge.FanoutShopBalance(self, balance);
    }

    void FanNetSystem(ZoneSession session, SystemMessageMsg message)
    {
        if (NetBridge == null)
            return;
        NetBridge.FanoutSystemMessage([session.ObjectId], new SystemMessageMsg
        {
            Id = message.Id,
            Text = message.Text ?? "",
            Params = message.Params ?? []
        });
    }

}
