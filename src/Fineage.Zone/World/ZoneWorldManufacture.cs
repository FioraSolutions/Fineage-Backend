using Fineage.Game;
using Fineage.Game.Recipes;
using Fineage.Game.Shops;
using Fineage.Protocol;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius manufacture / recipe shop (#283): open workshop, visitor list, make-info, make-item
/// (fee + materials from customer, MP + create skill from manufacturer, product to customer).
/// </summary>
public sealed partial class ZoneWorld
{
    public async Task<string?> TrySetRecipeShopAsync(ZoneSession session, SetRecipeShopMsg request, CancellationToken ct)
    {
        if (request == null)
            return "bad_request";
        if (!session.InWorld)
            return "world";
        string? error;
        lock (_gate)
            error = TrySetRecipeShop(session, request);
        return error;
    }

    public async Task<string?> TryRecipeShopMakeInfoAsync(ZoneSession session, RecipeShopMakeInfoMsg request, CancellationToken ct)
    {
        if (request == null)
            return "bad_request";
        if (!session.InWorld)
            return "world";
        string? error;
        lock (_gate)
            error = TryRecipeShopMakeInfo(session, request);
        return error;
    }

    public async Task<string?> TryRecipeShopMakeItemAsync(ZoneSession session, RecipeShopMakeItemMsg request, CancellationToken ct)
    {
        if (request == null)
            return "bad_request";
        if (!session.InWorld)
            return "world";
        string? error;
        lock (_gate)
            error = TryRecipeShopMakeItem(session, request);
        return error;
    }

    string? TrySetRecipeShop(ZoneSession session, SetRecipeShopMsg request)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var player) || player.Inventory == null)
            return "world";
        if (GuardOpenPrivateStore(player) is { } blocked)
            return blocked;

        var rows = request.Items ?? [];
        if (rows.Length == 0)
        {
            ClosePrivateStore(player.ObjectId, standUp: false);
            return null;
        }
        if (rows.Length > CraftProtocol.ManufactureSlots)
            return CraftProtocol.Limit;

        var listings = new List<ManufactureListing>(rows.Length);
        var seen = new HashSet<int>();
        foreach (var row in rows)
        {
            if (row.RecipeId < 1 || row.Price < 0)
                return PrivateStoreProtocol.Item;
            if (!seen.Add(row.RecipeId))
                return PrivateStoreProtocol.Item;
            if (!_game.Recipes.TryGet(row.RecipeId, out var recipe))
                return CraftProtocol.Unknown;
            if (!player.KnownRecipes.Contains(recipe.Id))
                return CraftProtocol.NotLearned;
            if (row.Price > ShopTrade.MaxAdena)
                return PrivateStoreProtocol.Adena;
            listings.Add(new ManufactureListing { RecipeId = recipe.Id, Price = row.Price });
        }

        player.PrivateSellList.Clear();
        player.PrivateBuyList.Clear();
        player.ManufactureList.Clear();
        player.ManufactureList.AddRange(listings);
        ActivatePrivateStore(player, PrivateStoreProtocol.TypeManufacture, request.Message);
        return null;
    }

    string? TryRecipeShopMakeInfo(ZoneSession session, RecipeShopMakeInfoMsg request)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var visitor))
            return "world";
        if (!_byId.TryGetValue(request.ManufacturerObjectId, out var maker) || maker.Kind != ZoneEntityKind.Player)
            return CraftProtocol.Closed;
        if (maker.PrivateStoreType != PrivateStoreProtocol.TypeManufacture
            || maker.ManufactureList.All(row => row.RecipeId != request.RecipeId))
            return CraftProtocol.Closed;
        if (!StoreInRange(visitor, maker))
            return CraftProtocol.Range;

        FanoutRecipeShopItemInfo(visitor.ObjectId, maker, request.RecipeId, result: 0);
        return null;
    }

    string? TryRecipeShopMakeItem(ZoneSession session, RecipeShopMakeItemMsg request)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var customer) || customer.Inventory == null)
            return "world";
        if (customer.Dead)
            return InventoryProtocol.Dead;
        if (customer.PrivateStoreType != PrivateStoreProtocol.TypeNone)
            return CraftProtocol.Busy;
        if (_tradeByPlayer.ContainsKey(customer.ObjectId))
            return CraftProtocol.Busy;
        if (!_byId.TryGetValue(request.ManufacturerObjectId, out var maker)
            || maker.Kind != ZoneEntityKind.Player
            || maker.Inventory == null)
            return CraftProtocol.Closed;
        if (maker.ObjectId == customer.ObjectId)
            return "bad_request";
        if (maker.PrivateStoreType != PrivateStoreProtocol.TypeManufacture)
            return CraftProtocol.Closed;
        if (!StoreInRange(customer, maker))
            return CraftProtocol.Range;
        if (maker.Dead)
            return CraftProtocol.Closed;

        var listing = maker.ManufactureList.FirstOrDefault(row => row.RecipeId == request.RecipeId);
        if (listing == null)
            return CraftProtocol.Closed;
        if (!_game.Recipes.TryGet(listing.RecipeId, out var recipe))
            return CraftProtocol.Unknown;
        if (!maker.KnownRecipes.Contains(recipe.Id))
            return CraftProtocol.NotLearned;

        int skillId = recipe.Dwarven ? RecipeCatalog.CreateDwarvenSkillId : RecipeCatalog.CreateCommonSkillId;
        if (!maker.KnownSkills.TryGetValue(skillId, out int skillLevel) || skillLevel < recipe.Level)
            return CraftProtocol.Skill;
        if (maker.CurMp < recipe.MpConsume)
            return CraftProtocol.Mp;

        long fee = listing.Price;
        if (fee > ShopTrade.MaxAdena)
            return CraftProtocol.Adena;
        if (fee > 0 && customer.Inventory.CountOf(ShopTrade.AdenaItemId) < fee)
            return CraftProtocol.Adena;
        if (fee > 0)
        {
            long makerAdena = maker.Inventory.CountOf(ShopTrade.AdenaItemId);
            if (makerAdena + fee > ShopTrade.MaxAdena)
                return CraftProtocol.Adena;
        }

        foreach (var ing in recipe.Ingredients)
        {
            if (customer.Inventory.CountOf(ing.ItemId) < ing.Count)
                return CraftProtocol.Materials;
        }

        var productOffers = new List<TradeOfferRow>
        {
            new()
            {
                InventoryObjectId = 0,
                ItemId = recipe.Production.ItemId,
                Count = recipe.Production.Count,
                EnchantLevel = 0
            }
        };
        var deny = IncomingDenyOf(customer, productOffers);
        if (deny == TradeIncomingDeny.Weight)
            return CraftProtocol.Weight;
        if (deny == TradeIncomingDeny.Slots)
            return CraftProtocol.Slots;

        var customerChanges = new List<InventoryUpdateEntryMsg>();
        var makerChanges = new List<InventoryUpdateEntryMsg>();

        // Mobius: fee first, then materials, then success roll (fee kept on fail).
        if (fee > 0 && !TransferAdena(customer.Inventory, maker.Inventory, (int)fee, customerChanges, makerChanges))
            return CraftProtocol.Adena;

        foreach (var ing in recipe.Ingredients)
        {
            int left = ing.Count;
            while (left > 0)
            {
                var stack = customer.Inventory.FindByItemId(ing.ItemId);
                if (stack == null)
                    return CraftProtocol.Materials;
                int take = Math.Min(left, stack.Count);
                if (!customer.Inventory.TryRemoveFromStack(stack, take, out bool gone))
                    return CraftProtocol.Materials;
                if (gone)
                    customerChanges.Add(InventoryMessages.RemoveRow(stack, -1));
                else
                    customerChanges.Add(InventoryMessages.ChangeRow(InventoryChange.Modify, stack));
                left -= take;
            }
        }

        maker.CurMp -= recipe.MpConsume;
        if (NetBridge != null)
        {
            NetBridge.FanoutStatusUpdate([maker.ObjectId], new Fineage.Net.Contract.StatusUpdateNetMsg
            {
                EntityValue = (ulong)(uint)maker.ObjectId,
                EntityGeneration = 1,
                AttributeIds = [InterludeStatus.CurMp, InterludeStatus.MaxMp],
                Values = [maker.CurMp, maker.MaxMp]
            });
        }

        bool success = recipe.SuccessRate >= 100
            || _spawnRandom.Next(100) < recipe.SuccessRate;
        int result = success ? 0 : 1;
        if (success)
        {
            int before = customer.Inventory.CountOf(recipe.Production.ItemId);
            var made = customer.Inventory.Add(recipe.Production.ItemId, recipe.Production.Count);
            bool stackable = made.Template.Stackable;
            int change = !stackable || before == 0 ? InventoryChange.Add : InventoryChange.Modify;
            customerChanges.Add(InventoryMessages.ChangeRow(change, made));
        }

        SaveInventory(session, customer.Inventory);
        if (maker.Session != null)
            SaveInventory(maker.Session, maker.Inventory);

        if (customerChanges.Count > 0)
        {
            NetBridge?.FanoutInventoryUpdate([customer.ObjectId],
                ToNetInventoryUpdate(new InventoryUpdateMsg { Changes = customerChanges.ToArray() }));
            SelfUserInfo(customer);
        }
        if (makerChanges.Count > 0)
        {
            NetBridge?.FanoutInventoryUpdate([maker.ObjectId],
                ToNetInventoryUpdate(new InventoryUpdateMsg { Changes = makerChanges.ToArray() }));
            SelfUserInfo(maker);
        }

        FanoutRecipeShopItemInfo(customer.ObjectId, maker, recipe.Id, result);
        return null;
    }

    void SendRecipeShopSellList(ZoneEntity visitor, ZoneEntity maker)
    {
        if (visitor.Session == null || visitor.Inventory == null)
            return;
        var items = maker.ManufactureList.Select(row => new Fineage.Net.Contract.RecipeShopRowNet
        {
            RecipeId = row.RecipeId,
            Price = row.Price
        }).ToArray();
        NetBridge?.FanoutRecipeShopSellList([visitor.ObjectId], new Fineage.Net.Contract.RecipeShopSellListNetMsg
        {
            ManufacturerObjectId = maker.ObjectId,
            CurrentMp = maker.CurMp,
            MaxMp = maker.MaxMp,
            BuyerAdena = visitor.Inventory.CountOf(ShopTrade.AdenaItemId),
            Items = items
        });
    }

    void FanoutRecipeShopItemInfo(int visitorObjectId, ZoneEntity maker, int recipeId, int result)
    {
        NetBridge?.FanoutRecipeShopItemInfo([visitorObjectId], new Fineage.Net.Contract.RecipeShopItemInfoNetMsg
        {
            ManufacturerObjectId = maker.ObjectId,
            RecipeId = recipeId,
            CurrentMp = maker.CurMp,
            MaxMp = maker.MaxMp,
            Result = result
        });
    }
}
