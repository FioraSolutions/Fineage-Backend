using Fineage.Game;
using Fineage.Game.Recipes;
using Fineage.Protocol;
using Fineage.Zone.Craft;
using Fineage.Zone.Trade;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius self-craft (#281): learn recipe scrolls into the book, open book, make-self
/// (materials + MP + create-skill level + success rate). Manufacture → <c>ZoneWorldManufacture</c>.
/// </summary>
public sealed partial class ZoneWorld
{
    public async Task<string?> TryRecipeBookOpenAsync(ZoneSession session, RecipeBookOpenMsg request, CancellationToken ct)
    {
        if (request == null)
            return "bad_request";
        if (!session.InWorld)
            return "world";
        string? error;
        lock (_gate)
            error = TryRecipeBookOpen(session, request);
        return error;
    }

    public async Task<string?> TryRecipeMakeSelfAsync(ZoneSession session, RecipeMakeSelfMsg request, CancellationToken ct)
    {
        if (request == null)
            return "bad_request";
        if (!session.InWorld)
            return "world";
        string? error;
        lock (_gate)
            error = TryRecipeMakeSelf(session, request);
        return error;
    }

    /// <summary>Mobius recipe-scroll use: register the recipe list id and consume one scroll.</summary>
    string? TryLearnRecipeFromItem(ZoneSession session, ZoneEntity player, ItemInstance item)
    {
        bool resolved = _game.Recipes.TryGetByRecipeItem(item.ItemId, out var recipe);
        if (CraftRules.LearnScrollError(resolved, resolved && player.KnownRecipes.Contains(recipe!.Id)) is { } learnError)
            return learnError;
        if (!player.Inventory!.TryRemoveFromStack(item, 1, out bool removedEntirely))
            return InventoryProtocol.Blocked;

        player.KnownRecipes.Add(recipe.Id);
        PersistKnownRecipes(session, player);

        var changes = new List<InventoryUpdateEntryMsg>();
        if (removedEntirely)
            changes.Add(InventoryMessages.RemoveRow(item, -1));
        else
            changes.Add(InventoryMessages.ChangeRow(InventoryChange.Modify, item));
        SaveInventory(session, player.Inventory);
        NetBridge?.FanoutInventoryUpdate([player.ObjectId], new InventoryUpdateMsg { Changes = changes.ToArray() });
        NotifyLine(player.ObjectId, 0, $"You learned {recipe.Name}.");
        return null;
    }

    string? TryRecipeBookOpen(ZoneSession session, RecipeBookOpenMsg request)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var player))
            return "world";
        var ids = player.KnownRecipes
            .Where(id => _game.Recipes.TryGet(id, out var r) && r.Dwarven == request.Dwarven)
            .OrderBy(id => id)
            .ToArray();
        NetBridge?.FanoutRecipeBookList([player.ObjectId], new RecipeBookListMsg
        {
            Dwarven = request.Dwarven ? 1 : 0,
            RecipeIds = ids
        });
        return null;
    }

    string? TryRecipeMakeSelf(ZoneSession session, RecipeMakeSelfMsg request)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var player) || player.Inventory == null)
            return "world";
        bool recipeKnown = _game.Recipes.TryGet(request.RecipeId, out var recipe);
        int skillId = recipeKnown
            ? (recipe!.Dwarven ? RecipeCatalog.CreateDwarvenSkillId : RecipeCatalog.CreateCommonSkillId)
            : 0;
        bool skillOk = recipeKnown
            && player.KnownSkills.TryGetValue(skillId, out int skillLevel)
            && skillLevel >= recipe!.Level;
        if (CraftRules.MakeSelfError(
                dead: player.Dead,
                privateStoreOpen: player.PrivateStoreType != PrivateStoreProtocol.TypeNone,
                inTrade: _tradeByPlayer.ContainsKey(player.ObjectId),
                recipeKnown: recipeKnown,
                recipeLearned: recipeKnown && player.KnownRecipes.Contains(recipe!.Id),
                skillLevelOk: skillOk,
                curMp: player.CurMp,
                mpConsume: recipeKnown ? recipe!.MpConsume : 0) is { } makeError)
            return makeError;

        foreach (var ing in recipe!.Ingredients)
        {
            if (player.Inventory.CountOf(ing.ItemId) < ing.Count)
                return CraftProtocol.Materials;
        }

        // Weight/slots for the product (Mobius validates capacity before consume).
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
        // IncomingDenyOf uses FindByItemId for stackables — product object id unused.
        var deny = IncomingDenyOf(player, productOffers);
        if (deny == TradeRules.IncomingDeny.Weight)
            return PrivateStoreProtocol.Weight;
        if (deny == TradeRules.IncomingDeny.Slots)
            return PrivateStoreProtocol.Slots;

        var changes = new List<InventoryUpdateEntryMsg>();
        foreach (var ing in recipe.Ingredients)
        {
            int left = ing.Count;
            while (left > 0)
            {
                var stack = player.Inventory.FindByItemId(ing.ItemId);
                if (stack == null)
                    return CraftProtocol.Materials;
                int take = Math.Min(left, stack.Count);
                if (!player.Inventory.TryRemoveFromStack(stack, take, out bool gone))
                    return CraftProtocol.Materials;
                if (gone)
                    changes.Add(InventoryMessages.RemoveRow(stack, -1));
                else
                    changes.Add(InventoryMessages.ChangeRow(InventoryChange.Modify, stack));
                left -= take;
            }
        }

        player.CurMp -= recipe.MpConsume;
        if (NetBridge != null)
        {
            NetBridge.FanoutStatusUpdate([player.ObjectId], new StatusUpdateMsg
            {
                ObjectId = player.ObjectId,
                AttributeIds = [InterludeStatus.CurMp, InterludeStatus.MaxMp],
                Values = [player.CurMp, player.MaxMp]
            });
        }

        bool success = CraftRules.Succeeds(recipe.SuccessRate, _spawnRandom.Next(100));
        int result = success ? 0 : 1;
        if (success)
        {
            int before = player.Inventory.CountOf(recipe.Production.ItemId);
            var made = player.Inventory.Add(recipe.Production.ItemId, recipe.Production.Count);
            bool stackable = made.Template.Stackable;
            int change = !stackable || before == 0 ? InventoryChange.Add : InventoryChange.Modify;
            changes.Add(InventoryMessages.ChangeRow(change, made));
        }

        SaveInventory(session, player.Inventory);
        if (changes.Count > 0)
        {
            NetBridge?.FanoutInventoryUpdate([player.ObjectId], new InventoryUpdateMsg { Changes = changes.ToArray() });
            SelfUserInfo(player);
        }

        NetBridge?.FanoutRecipeItemMakeInfo([player.ObjectId], new RecipeItemMakeInfoMsg
        {
            RecipeId = recipe.Id,
            Result = result,
            CurrentMp = player.CurMp,
            MaxMp = player.MaxMp
        });
        // Fail still consumed materials/MP (Mobius); tell the client via RecipeItemMakeInfo, not Reject.
        return null;
    }

    void LoadKnownRecipes(ZoneEntity entity, Fineage.Accounts.CharacterRecord? stored)
    {
        entity.KnownRecipes.Clear();
        if (stored?.KnownRecipes == null)
            return;
        foreach (int id in stored.KnownRecipes)
        {
            if (id > 0 && _game.Recipes.TryGet(id, out _))
                entity.KnownRecipes.Add(id);
        }
    }

    void PersistKnownRecipes(ZoneSession session, ZoneEntity player) =>
        _persist.ReplaceKnownRecipes(
            session.Account,
            session.ObjectId,
            player.KnownRecipes.OrderBy(id => id).ToArray());
}
