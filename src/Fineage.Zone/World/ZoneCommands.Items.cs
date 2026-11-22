using Fineage.Game.Services;
using Fineage.Game.Shops;
using Fineage.Protocol;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

/// <summary>Inventory, enchant, shops, warehouse, trade, and private store commands.</summary>
public sealed record PickupCommand(int GroundObjectId) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryPickupAsync(session, GroundObjectId, CancellationToken.None));
}

public sealed record RequestItemListCommand : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        world.TrySendItemList(session);
}

public sealed record DestroyItemCommand(int ObjectId, int Count) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryDestroyItemAsync(session, ObjectId, Count, CancellationToken.None));
}

public sealed record DropItemCommand(int ObjectId, int Count, int X, int Y, int Z) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryDropItemAsync(session, ObjectId, Count, X, Y, Z, CancellationToken.None));
}

public sealed record UseItemCommand(int ObjectId) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryUseItemAsync(session, ObjectId, CancellationToken.None));
}

public sealed record UnequipItemCommand(int PaperdollSlot) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryUnequipItemAsync(session, PaperdollSlot, CancellationToken.None));
}

public sealed record EnchantRequestCommand(int ScrollObjectId) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryEnchantRequestAsync(session, ScrollObjectId, CancellationToken.None));
}

public sealed record EnchantConfirmCommand(int TargetObjectId) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryEnchantConfirmAsync(session, TargetObjectId, CancellationToken.None));
}

public sealed record EnchantCancelCommand : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryEnchantCancelAsync(session, CancellationToken.None));
}

public sealed record AugmentConfirmTargetCommand(int TargetObjectId) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryAugmentConfirmTargetAsync(session, TargetObjectId, CancellationToken.None));
}

public sealed record AugmentConfirmRefinerCommand(int TargetObjectId, int RefinerObjectId) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryAugmentConfirmRefinerAsync(session, TargetObjectId, RefinerObjectId, CancellationToken.None));
}

public sealed record AugmentConfirmGemCommand(int TargetObjectId, int RefinerObjectId, int GemstoneObjectId, long GemstoneCount) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryAugmentConfirmGemAsync(session, TargetObjectId, RefinerObjectId, GemstoneObjectId, GemstoneCount, CancellationToken.None));
}

public sealed record AugmentRefineCommand(int TargetObjectId, int RefinerObjectId, int GemstoneObjectId, long GemstoneCount) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryAugmentRefineAsync(session, TargetObjectId, RefinerObjectId, GemstoneObjectId, GemstoneCount, CancellationToken.None));
}

public sealed record AugmentRefineCancelCommand(int TargetObjectId) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryAugmentRefineCancelAsync(session, TargetObjectId, CancellationToken.None));
}

public sealed record AugmentCancelWindowCommand : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryAugmentCancelWindowAsync(session, CancellationToken.None));
}

public sealed record AutoSoulShotCommand(int ItemId, bool Enabled) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryAutoSoulShotAsync(session, ItemId, Enabled, CancellationToken.None));
}

public sealed record WarehouseDepositCommand(int NpcObjectId, WarehouseLine[] Lines) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryWarehouseDepositAsync(session, NpcObjectId, Lines, CancellationToken.None));
}

public sealed record WarehouseWithdrawCommand(int NpcObjectId, WarehouseLine[] Lines) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryWarehouseWithdrawAsync(session, NpcObjectId, Lines, CancellationToken.None));
}

public sealed record BuyCommand(int NpcObjectId, int ListId, ShopLine[] Items) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryBuyAsync(session, NpcObjectId, ListId, Items, CancellationToken.None));
}

public sealed record SellCommand(int NpcObjectId, ShopObjectLine[] Items) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TrySellAsync(session, NpcObjectId, Items, CancellationToken.None));
}

public sealed record MultiSellCommand(int NpcObjectId, int ListId, int EntryId, int Amount) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryMultiSellAsync(session, NpcObjectId, ListId, EntryId, Amount, CancellationToken.None));
}

public sealed record TradeRequestCommand(TradeRequestMsg Request) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryTradeRequestAsync(session, Request, CancellationToken.None));
}

public sealed record TradeAnswerCommand(TradeAnswerRequestMsg Answer) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryTradeAnswerAsync(session, Answer, CancellationToken.None));
}

public sealed record TradeAddItemCommand(TradeAddItemRequestMsg Add) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryTradeAddItemAsync(session, Add, CancellationToken.None));
}

public sealed record TradeDoneCommand(TradeDoneRequestMsg Done) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryTradeDoneAsync(session, Done, CancellationToken.None));
}

public sealed record SetPrivateStoreSellCommand(SetPrivateStoreSellMsg Request) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TrySetPrivateStoreSellAsync(session, Request, CancellationToken.None));
}

public sealed record PrivateStoreQuitCommand : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryPrivateStoreQuitAsync(session, CancellationToken.None));
}

public sealed record PrivateStoreListCommand(PrivateStoreListRequestMsg Request) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryPrivateStoreListAsync(session, Request, CancellationToken.None));
}

public sealed record PrivateStoreBuyCommand(PrivateStoreBuyMsg Request) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryPrivateStoreBuyAsync(session, Request, CancellationToken.None));
}

public sealed record SetPrivateStoreBuyCommand(SetPrivateStoreBuyMsg Request) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TrySetPrivateStoreBuyAsync(session, Request, CancellationToken.None));
}

public sealed record PrivateStoreSellToCommand(PrivateStoreSellToMsg Request) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryPrivateStoreSellToAsync(session, Request, CancellationToken.None));
}

public sealed record RecipeBookOpenCommand(RecipeBookOpenMsg Request) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryRecipeBookOpenAsync(session, Request, CancellationToken.None));
}

public sealed record RecipeMakeSelfCommand(RecipeMakeSelfMsg Request) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryRecipeMakeSelfAsync(session, Request, CancellationToken.None));
}

public sealed record SetRecipeShopCommand(SetRecipeShopMsg Request) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TrySetRecipeShopAsync(session, Request, CancellationToken.None));
}

public sealed record RecipeShopMakeInfoCommand(RecipeShopMakeInfoMsg Request) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryRecipeShopMakeInfoAsync(session, Request, CancellationToken.None));
}

public sealed record RecipeShopMakeItemCommand(RecipeShopMakeItemMsg Request) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryRecipeShopMakeItemAsync(session, Request, CancellationToken.None));
}