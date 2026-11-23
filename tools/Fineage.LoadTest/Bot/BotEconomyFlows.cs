using Fineage.LoadTest.Models;
using Fineage.Net.Contract;

namespace Fineage.LoadTest.Bot;

/// <summary>Headless Net flows for the auditable economy ledger hammer scenario (#955).</summary>
public static class BotEconomyFlows
{
    public const int InventoryChangeRemove = 3;

    public static async Task RunActionAsync(
        FineageBotClient bot,
        string action,
        IReadOnlyList<FineageBotClient> fleet,
        CancellationToken ct)
    {
        var economy = bot.Scenario.Economy;
        if (economy is not { Enabled: true })
            return;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(economy.FlowTimeoutSeconds));
        var t = timeout.Token;
        var flowStats = bot.Stats;
        Interlocked.Increment(ref flowStats.EconomyFlowsAttempted);
        try
        {
            switch (action)
            {
                case "economy_hub":
                    await bot.TeleportToAsync(economy.HubX, economy.HubY, economy.HubZ, t);
                    break;
                case "economy_shop_buy":
                    await bot.TeleportToAsync(economy.HubX, economy.HubY, economy.HubZ, t);
                    await bot.BypassAsync(economy.MerchantObjectId, "Buy 1", t);
                    await bot.BuyItemAsync(economy.MerchantObjectId, economy.ShopBuyListId,
                        economy.ShopBuyItemId, economy.ShopBuyCount, t);
                    break;
                case "economy_shop_sell":
                    await bot.TeleportToAsync(economy.HubX, economy.HubY, economy.HubZ, t);
                    int? sellId = bot.Bag.FirstTradableObjectId();
                    if (sellId is > 0)
                    {
                        await bot.BypassAsync(economy.MerchantObjectId, "Sell", t);
                        await bot.SellItemAsync(economy.MerchantObjectId, sellId.Value, 1, t);
                    }

                    break;
                case "economy_warehouse":
                    await bot.TeleportToAsync(economy.HubX, economy.HubY, economy.HubZ, t);
                    await bot.BypassAsync(economy.WarehouseObjectId, "DepositP", t);
                    if (bot.Bag.Adena >= economy.WarehouseDepositAdena)
                    {
                        int? adenaId = bot.Bag.Items.FirstOrDefault(p => p.Value.ItemId == EconomyHammerConfig.AdenaItemId).Key;
                        if (adenaId > 0)
                            await bot.WarehouseDepositAsync(economy.WarehouseObjectId, adenaId.Value, economy.WarehouseDepositAdena, t);
                    }

                    break;
                case "economy_trade":
                    await EconomyFleetCoordinator.TryTradeAsync(bot, fleet, t);
                    break;
                case "economy_store":
                    await EconomyFleetCoordinator.TryPrivateStoreAsync(bot, fleet, t);
                    break;
                case "economy_manufacture_maker":
                    if (Math.Abs(bot.BotId) % economy.ManufacturerModulo != economy.ManufacturerRemainder)
                        throw new InvalidOperationException(
                            $"economy_manufacture_maker picked for bot {bot.BotId} without manufacturer role");
                    await EconomyFleetCoordinator.TryManufactureMakerAsync(bot, t);
                    break;
                case "economy_manufacture_customer":
                    if (Math.Abs(bot.BotId) % economy.ManufacturerModulo != 1)
                        throw new InvalidOperationException(
                            $"economy_manufacture_customer picked for bot {bot.BotId} without customer role");
                    await EconomyFleetCoordinator.TryManufactureCustomerAsync(bot, fleet, t);
                    break;
                case "economy_manor":
                    await bot.TeleportToAsync(economy.GludioManorX, economy.GludioManorY, economy.GludioManorZ, t);
                    int manorNpc = economy.ManorManagerObjectId > 0 ? economy.ManorManagerObjectId : economy.MerchantObjectId;
                    await bot.ManorCommandAsync(ManorCommandKind.BuyList, economy.GludioCastleId, manorNpc, t);
                    await bot.ManorCommandAsync(ManorCommandKind.BuySeeds, economy.GludioCastleId, manorNpc, t,
                        rows: [new ManorRowNet { ItemId = economy.ManorSeedItemId, Count = 1 }]);
                    break;
                case "economy_pickup_drop":
                    await bot.TeleportToAsync(bot.Scenario.SpawnX, bot.Scenario.SpawnY, bot.Scenario.SpawnZ, t);
                    await bot.TryDropAndPickupAsync(t);
                    break;
            }

            Interlocked.Increment(ref flowStats.EconomyFlowsSucceeded);
        }
        catch (Exception ex)
        {
            flowStats.EconomyFlowFailures.Add($"{action}: {ex.Message}");
        }
    }
}