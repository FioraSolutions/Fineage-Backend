using Fineage.LoadTest.Models;
using Fineage.Net.Contract;

namespace Fineage.LoadTest.Bot;

public static class EconomyFleetCoordinator
{
    public static async Task TryTradeAsync(FineageBotClient bot, IReadOnlyList<FineageBotClient> fleet, CancellationToken ct)
    {
        int? peer = bot.Peers.PickPeerObjectId(bot.BotId, bot.Rng) ?? bot.VisiblePlayerObjectId();
        if (peer is not > 0 || peer == bot.SelfObjectId)
            return;

        if (bot.BotId % 2 == 0)
        {
            await bot.TradeRequestAsync(peer.Value, ct);
            if (bot.Bag.Adena >= bot.Scenario.Economy!.TradeAdenaAmount)
            {
                int? adenaRow = bot.Bag.Items.FirstOrDefault(p => p.Value.ItemId == EconomyHammerConfig.AdenaItemId).Key;
                if (adenaRow > 0)
                {
                    await bot.WaitForOpcodeAsync(NetOpcode.TradeStart, ct);
                    await bot.TradeAddItemAsync(adenaRow.Value, bot.Scenario.Economy.TradeAdenaAmount, ct);
                    await bot.TradeDoneAsync(confirm: true, ct);
                }
            }
        }
        else
        {
            await bot.WaitForOpcodeAsync(NetOpcode.TradeAsk, ct);
            var ask = bot.LastTradeAsk();
            if (ask?.RequestorObjectId > 0)
            {
                await bot.TradeAnswerAsync(ask.RequestorObjectId, accept: true, ct);
                await bot.WaitForOpcodeAsync(NetOpcode.TradeStart, ct);
                await bot.TradeDoneAsync(confirm: true, ct);
            }
        }
    }

    public static async Task TryPrivateStoreAsync(FineageBotClient bot, IReadOnlyList<FineageBotClient> fleet, CancellationToken ct)
    {
        var economy = bot.Scenario.Economy!;
        if (bot.BotId % 4 == 0)
        {
            int? item = bot.Bag.FirstTradableObjectId();
            if (item is null or <= 0)
                return;
            await bot.SetPrivateStoreSellAsync(
                [new PrivateStoreSellRowNet { InventoryObjectId = item.Value, Count = 1, Price = 10 }], ct);
        }
        else if (bot.BotId % 4 == 1)
        {
            int? seller = fleet.FirstOrDefault(b => b.BotId % 4 == 0 && !b.Stats.Disconnected)?.SelfObjectId;
            if (seller is > 0)
            {
                await bot.PrivateStoreListRequestAsync(seller.Value, ct);
                await bot.WaitForOpcodeAsync(NetOpcode.PrivateStoreListSell, ct);
                var list = bot.LastPrivateStoreListSell();
                if (list?.Items is { Length: > 0 } items)
                    await bot.PrivateStoreBuyFromSellStoreAsync(seller.Value, items[0], ct);
            }
        }
        else if (bot.BotId % 4 == 2)
        {
            await bot.SetPrivateStoreBuyAsync(
                [new PrivateStoreBuyRowNet { ItemId = economy.ShopBuyItemId, Count = 1, Price = 5 }], ct);
        }
        else
        {
            int? store = fleet.FirstOrDefault(b => b.BotId % 4 == 2 && !b.Stats.Disconnected)?.SelfObjectId;
            int? stock = bot.Bag.FirstTradableObjectId();
            if (store is > 0 && stock is > 0 &&
                bot.Bag.Items.TryGetValue(stock.Value, out var row))
                await bot.PrivateStoreSellToBuyStoreAsync(store.Value, stock.Value, row.ItemId, 1, 5, ct);
        }
    }

    public static async Task TryManufactureMakerAsync(FineageBotClient bot, CancellationToken ct)
    {
        var economy = bot.Scenario.Economy!;
        var (expectClass, expectRace) = bot.Scenario.ResolveClassAndRace(bot.BotId);
        if (expectRace != economy.ManufacturerRace || expectClass != economy.ManufacturerClassId)
            throw new InvalidOperationException(
                $"economy_manufacture_maker: bot {bot.BotId} scenario class/race does not match dwarf manufacturer config");
        await bot.SetRecipeShopAsync(economy.RecipeListId, economy.ManufactureFee, ct);
        await bot.WaitForOpcodeAsync(NetOpcode.PrivateStoreInfo, ct);
    }

    public static async Task TryManufactureCustomerAsync(
        FineageBotClient bot,
        IReadOnlyList<FineageBotClient> fleet,
        CancellationToken ct)
    {
        var economy = bot.Scenario.Economy!;
        int? maker = fleet.FirstOrDefault(b =>
            Math.Abs(b.BotId) % economy.ManufacturerModulo == economy.ManufacturerRemainder &&
            !b.Stats.Disconnected)?.SelfObjectId;
        if (maker is not > 0)
            throw new InvalidOperationException("economy_manufacture_customer: no manufacturer bot online in fleet");
        await bot.PrivateStoreListRequestAsync(maker.Value, ct);
        await bot.WaitForOpcodeAsync(NetOpcode.RecipeShopSellList, ct);
        await bot.RecipeShopMakeItemAsync(maker.Value, economy.RecipeListId, ct);
        await bot.WaitForOpcodeAsync(NetOpcode.RecipeItemMakeInfo, ct);
    }

    /// <summary>
    /// Legacy single-action entry (<c>economy_manufacture</c>): dispatches on the bot's role instead of failing a
    /// bot that is neither the crafter nor a customer.
    /// </summary>
    public static async Task TryManufactureAsync(
        FineageBotClient bot,
        IReadOnlyList<FineageBotClient> fleet,
        CancellationToken ct)
    {
        var economy = bot.Scenario.Economy!;
        int mod = Math.Abs(bot.BotId) % economy.ManufacturerModulo;
        if (mod == economy.ManufacturerRemainder)
            await TryManufactureMakerAsync(bot, ct);
        else if (mod == 1)
            await TryManufactureCustomerAsync(bot, fleet, ct);
    }
}