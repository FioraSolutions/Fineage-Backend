using Fineage.Game;
using Fineage.Game.Recipes;
using Fineage.Game.Shops;
using Fineage.Protocol;
using Fineage.Zone.Craft;
using Fineage.Zone.Trade;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius manufacture / recipe shop (#283): open workshop, visitor list, make-info, make-item
/// (fee + materials from customer, MP + create skill from manufacturer, product to customer).
/// </summary>
public sealed partial class ZoneWorld
{
    public Task<string?> TrySetRecipeShopAsync(ZoneSession session, SetRecipeShopMsg request, CancellationToken ct)
    {
        if (request == null)
            return Task.FromResult<string?>("bad_request");
        if (!session.InWorld)
            return Task.FromResult<string?>("world");
        return Task.FromResult(WithDeferredInventoryPersist(() => TrySetRecipeShop(session, request)));
    }

    public Task<string?> TryRecipeShopMakeInfoAsync(ZoneSession session, RecipeShopMakeInfoMsg request, CancellationToken ct)
    {
        if (request == null)
            return Task.FromResult<string?>("bad_request");
        if (!session.InWorld)
            return Task.FromResult<string?>("world");
        return Task.FromResult(WithDeferredInventoryPersist(() => TryRecipeShopMakeInfo(session, request)));
    }

    public Task<string?> TryRecipeShopMakeItemAsync(ZoneSession session, RecipeShopMakeItemMsg request, CancellationToken ct)
    {
        if (request == null)
            return Task.FromResult<string?>("bad_request");
        if (!session.InWorld)
            return Task.FromResult<string?>("world");
        // World mutate under _gate; Postgres inventory write after unlock (B8).
        return Task.FromResult(WithDeferredInventoryPersist(() => TryRecipeShopMakeItem(session, request)));
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
        if (ManufactureRules.OpenListLimitError(rows.Length) is { } limitError)
            return limitError;

        var listings = new List<ManufactureListing>(rows.Length);
        var seen = new HashSet<int>();
        foreach (var row in rows)
        {
            bool duplicate = !seen.Add(row.RecipeId);
            bool known = _game.Recipes.TryGet(row.RecipeId, out var recipe);
            if (ManufactureRules.OpenRowError(
                    row.RecipeId,
                    row.Price,
                    duplicate,
                    known,
                    known && player.KnownRecipes.Contains(recipe!.Id)) is { } rowError)
                return rowError;
            listings.Add(new ManufactureListing { RecipeId = recipe!.Id, Price = row.Price });
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
        bool makerPlayer = _byId.TryGetValue(request.ManufacturerObjectId, out var maker)
            && maker.Kind == ZoneEntityKind.Player;
        bool listingOpen = makerPlayer
            && maker!.PrivateStoreType == PrivateStoreProtocol.TypeManufacture
            && maker.ManufactureList.Any(row => row.RecipeId == request.RecipeId);
        if (ManufactureRules.MakeInfoError(
                makerPlayer,
                listingOpen,
                makerPlayer && StoreInRange(visitor, maker!)) is { } infoError)
            return infoError;

        FanoutRecipeShopItemInfo(visitor.ObjectId, maker!, request.RecipeId, result: 0);
        return null;
    }

    string? TryRecipeShopMakeItem(ZoneSession session, RecipeShopMakeItemMsg request)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var customer) || customer.Inventory == null)
            return "world";
        _byId.TryGetValue(request.ManufacturerObjectId, out var makerCandidate);
        bool makerOk = makerCandidate != null
            && makerCandidate.Kind == ZoneEntityKind.Player
            && makerCandidate.Inventory != null;
        var listing = makerOk
            ? makerCandidate!.ManufactureList.FirstOrDefault(row => row.RecipeId == request.RecipeId)
            : null;
        RecipeDefinition? recipeCandidate = null;
        bool recipeKnown = listing != null && _game.Recipes.TryGet(listing.RecipeId, out recipeCandidate);
        int skillId = recipeKnown
            ? (recipeCandidate!.Dwarven ? RecipeCatalog.CreateDwarvenSkillId : RecipeCatalog.CreateCommonSkillId)
            : 0;
        bool skillOk = recipeKnown
            && makerCandidate!.KnownSkills.TryGetValue(skillId, out int skillLevel)
            && skillLevel >= recipeCandidate!.Level;
        long fee = listing?.Price ?? 0;
        if (ManufactureRules.MakeItemError(
                customerDead: customer.Dead,
                customerStoreOpen: customer.PrivateStoreType != PrivateStoreProtocol.TypeNone,
                customerInTrade: _tradeByPlayer.ContainsKey(customer.ObjectId),
                makerValidManufacture: makerOk && makerCandidate!.PrivateStoreType == PrivateStoreProtocol.TypeManufacture,
                selfTarget: makerOk && makerCandidate!.ObjectId == customer.ObjectId,
                inRange: makerOk && StoreInRange(customer, makerCandidate!),
                makerDead: makerOk && makerCandidate!.Dead,
                listingPresent: listing != null,
                recipeKnown: recipeKnown,
                recipeLearned: recipeKnown && makerCandidate!.KnownRecipes.Contains(recipeCandidate!.Id),
                skillLevelOk: skillOk,
                makerCurMp: makerOk ? makerCandidate!.CurMp : 0,
                mpConsume: recipeKnown ? recipeCandidate!.MpConsume : 0,
                fee: fee,
                customerCanPayFee: fee <= 0 || customer.Inventory.CountOf(ShopTrade.AdenaItemId) >= fee,
                makerCanReceiveFee: fee <= 0
                    || (makerOk && makerCandidate!.Inventory!.CountOf(ShopTrade.AdenaItemId) + fee <= ShopTrade.MaxAdena)) is { } makeError)
            return makeError;

        var maker = makerCandidate!;
        var makerInventory = maker.Inventory!;
        var recipe = recipeCandidate!;

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
        if (deny == TradeRules.IncomingDeny.Weight)
            return CraftProtocol.Weight;
        if (deny == TradeRules.IncomingDeny.Slots)
            return CraftProtocol.Slots;

        var customerChanges = new List<InventoryUpdateEntryMsg>();
        var makerChanges = new List<InventoryUpdateEntryMsg>();

        // Mobius: fee first, then materials, then success roll (fee kept on fail).
        if (fee > 0 && !TransferAdena(customer.Inventory, makerInventory, (int)fee, customerChanges, makerChanges))
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
            NetBridge.FanoutStatusUpdate([maker.ObjectId], new StatusUpdateMsg
            {
                ObjectId = maker.ObjectId,
                AttributeIds = [InterludeStatus.CurMp, InterludeStatus.MaxMp],
                Values = [maker.CurMp, maker.MaxMp]
            });
        }

        bool success = CraftRules.Succeeds(recipe.SuccessRate, _spawnRandom.Next(100));
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
            SaveInventory(maker.Session, makerInventory);

        if (customerChanges.Count > 0)
        {
            NetBridge?.FanoutInventoryUpdate([customer.ObjectId], new InventoryUpdateMsg { Changes = customerChanges.ToArray() });
            SelfUserInfo(customer);
        }
        if (makerChanges.Count > 0)
        {
            NetBridge?.FanoutInventoryUpdate([maker.ObjectId], new InventoryUpdateMsg { Changes = makerChanges.ToArray() });
            SelfUserInfo(maker);
        }

        FanoutRecipeShopItemInfo(customer.ObjectId, maker, recipe.Id, result);
        return null;
    }

}
