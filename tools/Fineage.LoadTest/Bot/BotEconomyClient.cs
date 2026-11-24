using System.Collections.Concurrent;
using Fineage.LoadTest.Models;
using Fineage.Net.Contract;

namespace Fineage.LoadTest.Bot;

public sealed partial class FineageBotClient
{
    public async Task TeleportToAsync(int x, int y, int z, CancellationToken ct) =>
        await ZoneWriteAsync(NetOpcode.TeleportToLocation,
            NetCodec.EncodeTeleportToLocation(new TeleportToLocationNetMsg { X = x, Y = y, Z = z }), ct);

    public async Task BypassAsync(int npcObjectId, string command, CancellationToken ct)
    {
        int mark = _observation.FrameCount;
        await ZoneWriteAsync(NetOpcode.BypassRequest,
            NetCodec.EncodeBypassRequest(new BypassRequestNetMsg { NpcObjectId = npcObjectId, Command = command }), ct);
        await Task.Delay(50, ct);
        _ = mark;
    }

    public async Task BuyItemAsync(int npcObjectId, int listId, int itemId, int count, CancellationToken ct)
    {
        int mark = _observation.FrameCount;
        await ZoneWriteAsync(NetOpcode.RequestBuyItem,
            NetCodec.EncodeRequestBuyItem(new RequestBuyItemNetMsg
            {
                NpcObjectId = npcObjectId,
                ListId = listId,
                Items = [new ShopCountNet { ItemId = itemId, Count = count }]
            }), ct);
        await WaitForAnyOpcodeAsync(mark, ct, NetOpcode.InventoryUpdate, NetOpcode.ItemList);
    }

    public async Task SellItemAsync(int npcObjectId, int inventoryObjectId, int count, CancellationToken ct)
    {
        int mark = _observation.FrameCount;
        await ZoneWriteAsync(NetOpcode.RequestSellItem,
            NetCodec.EncodeRequestSellItem(new RequestSellItemNetMsg
            {
                NpcObjectId = npcObjectId,
                Items = [new ShopObjectCountNet { ObjectId = inventoryObjectId, Count = count }]
            }), ct);
        await WaitForAnyOpcodeAsync(mark, ct, NetOpcode.InventoryUpdate, NetOpcode.ItemList);
    }

    public async Task WarehouseDepositAsync(int npcObjectId, int inventoryObjectId, int count, CancellationToken ct)
    {
        int mark = _observation.FrameCount;
        await ZoneWriteAsync(NetOpcode.WarehouseDepositRequest,
            NetCodec.EncodeWarehouseDepositRequest(new WarehouseDepositRequestNetMsg
            {
                NpcObjectId = npcObjectId,
                Lines = [new ShopObjectCountNet { ObjectId = inventoryObjectId, Count = count }]
            }), ct);
        await WaitForAnyOpcodeAsync(mark, ct, NetOpcode.InventoryUpdate, NetOpcode.WarehouseDepositList);
    }

    public Task TradeRequestAsync(int targetObjectId, CancellationToken ct) =>
        ZoneWriteAsync(NetOpcode.TradeRequest,
            NetCodec.EncodeTradeRequest(new TradeRequestNetMsg { TargetObjectId = targetObjectId }), ct);

    public Task TradeAnswerAsync(int requestorObjectId, bool accept, CancellationToken ct) =>
        ZoneWriteAsync(NetOpcode.TradeAnswerRequest,
            NetCodec.EncodeTradeAnswerRequest(new TradeAnswerRequestNetMsg
            {
                RequestorObjectId = requestorObjectId,
                Response = accept ? 1 : 0
            }), ct);

    public Task TradeAddItemAsync(int inventoryObjectId, long count, CancellationToken ct) =>
        ZoneWriteAsync(NetOpcode.TradeAddItemRequest,
            NetCodec.EncodeTradeAddItemRequest(new TradeAddItemRequestNetMsg
            {
                InventoryObjectId = inventoryObjectId,
                Count = count
            }), ct);

    public Task TradeDoneAsync(bool confirm, CancellationToken ct) =>
        ZoneWriteAsync(NetOpcode.TradeDoneRequest,
            NetCodec.EncodeTradeDoneRequest(new TradeDoneRequestNetMsg { Response = confirm ? 1 : 0 }), ct);

    public TradeAskNetMsg? LastTradeAsk() => _observation.LastTradeAsk;

    public async Task WaitForOpcodeAsync(ushort opcode, CancellationToken ct) =>
        await WaitForAnyOpcodeAsync(_observation.FrameCount, ct, opcode);

    public async Task SetPrivateStoreSellAsync(PrivateStoreSellRowNet[] rows, CancellationToken ct)
    {
        await ZoneWriteAsync(NetOpcode.SetPrivateStoreSell,
            NetCodec.EncodeSetPrivateStoreSell(new SetPrivateStoreSellNetMsg
            {
                Message = "hammer",
                Packaged = 0,
                Items = rows
            }), ct);
        await Task.Delay(80, ct);
    }

    public async Task SetPrivateStoreBuyAsync(PrivateStoreBuyRowNet[] rows, CancellationToken ct)
    {
        await ZoneWriteAsync(NetOpcode.SetPrivateStoreBuy,
            NetCodec.EncodeSetPrivateStoreBuy(new SetPrivateStoreBuyNetMsg { Message = "hammer", Items = rows }), ct);
        await Task.Delay(80, ct);
    }

    public Task PrivateStoreListRequestAsync(int storeObjectId, CancellationToken ct) =>
        ZoneWriteAsync(NetOpcode.PrivateStoreListRequest,
            NetCodec.EncodePrivateStoreListRequest(new PrivateStoreListRequestNetMsg { StoreObjectId = storeObjectId }), ct);

    public async Task PrivateStoreBuyFromSellStoreAsync(int storeObjectId, PrivateStoreSellRowNet row, CancellationToken ct)
    {
        int mark = _observation.FrameCount;
        await ZoneWriteAsync(NetOpcode.PrivateStoreBuy,
            NetCodec.EncodePrivateStoreBuy(new PrivateStoreBuyNetMsg
            {
                StoreObjectId = storeObjectId,
                Items = [row]
            }), ct);
        await WaitForAnyOpcodeAsync(mark, ct, NetOpcode.InventoryUpdate, NetOpcode.PrivateStoreInfo);
    }

    public async Task PrivateStoreSellToBuyStoreAsync(
        int storeObjectId,
        int inventoryObjectId,
        int itemId,
        long count,
        long price,
        CancellationToken ct)
    {
        int mark = _observation.FrameCount;
        await ZoneWriteAsync(NetOpcode.PrivateStoreSellTo,
            NetCodec.EncodePrivateStoreSellTo(new PrivateStoreSellToNetMsg
            {
                StoreObjectId = storeObjectId,
                Items =
                [
                    new PrivateStoreSellRowNet
                    {
                        InventoryObjectId = inventoryObjectId,
                        ItemId = itemId,
                        Count = count,
                        Price = price
                    }
                ]
            }), ct);
        await WaitForAnyOpcodeAsync(mark, ct, NetOpcode.InventoryUpdate);
    }

    public async Task SetRecipeShopAsync(int recipeId, long fee, CancellationToken ct)
    {
        await ZoneWriteAsync(NetOpcode.SetRecipeShop,
            NetCodec.EncodeSetRecipeShop(new SetRecipeShopNetMsg
            {
                Message = "hammer",
                Items = [new RecipeShopRowNet { RecipeId = recipeId, Price = fee }]
            }), ct);
        await Task.Delay(80, ct);
    }

    public Task RecipeShopMakeItemAsync(int manufacturerObjectId, int recipeId, CancellationToken ct) =>
        ZoneWriteAsync(NetOpcode.RecipeShopMakeItem,
            NetCodec.EncodeRecipeShopMakeItem(new RecipeShopMakeItemNetMsg
            {
                ManufacturerObjectId = manufacturerObjectId,
                RecipeId = recipeId
            }), ct);

    public async Task ManorCommandAsync(
        int kind,
        int castleId,
        int npcObjectId,
        CancellationToken ct,
        bool next = false,
        ManorRowNet[]? rows = null)
    {
        int mark = _observation.FrameCount;
        await ZoneWriteAsync(NetOpcode.ManorCommand,
            NetCodec.EncodeManorCommand(new ManorCommandNetMsg
            {
                Kind = kind,
                CastleId = castleId,
                NpcObjectId = npcObjectId,
                Next = next,
                Rows = rows ?? []
            }), ct);
        await WaitForAnyOpcodeAsync(mark, ct, NetOpcode.InventoryUpdate, NetOpcode.ManorInfo);
    }

    public PrivateStoreListSellNetMsg? LastPrivateStoreListSell() => _observation.LastPrivateStoreListSell;

    public async Task TryDropAndPickupAsync(CancellationToken ct)
    {
        int? tradable = Bag.FirstTradableObjectId();
        if (tradable is not > 0)
            return;

        int mark = _observation.FrameCount;
        await ZoneWriteAsync(NetOpcode.DropItemRequest,
            NetCodec.EncodeDropItemRequest(new DropItemRequestNetMsg
            {
                ObjectId = tradable.Value,
                Count = 1,
                X = _spawnX,
                Y = _spawnY,
                Z = _spawnZ
            }), ct);

        int? ground = null;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(4);
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            ground = _observation.TryPickNearestGroundItem(SelfObjectId, 600);
            if (ground is > 0)
                break;
            await Task.Delay(40, ct);
        }

        if (ground is > 0)
        {
            await ZoneWriteAsync(NetOpcode.PickupRequest,
                NetCodec.EncodePickupRequest(new PickupRequestNetMsg { GroundObjectId = ground.Value }), ct);
            await WaitForAnyOpcodeAsync(mark, ct, NetOpcode.InventoryUpdate, NetOpcode.EntityDespawn);
        }
    }

    async Task WaitForAnyOpcodeAsync(int afterFrame, CancellationToken ct, params ushort[] opcodes)
    {
        var economy = Scenario.Economy;
        int seconds = economy?.FlowTimeoutSeconds ?? 8;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(seconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (_observation.ContainsOpcodeAfter(afterFrame, opcodes))
                return;
            if (_observation.ContainsRejectAfter(afterFrame, out string? code))
                throw new InvalidOperationException(code ?? "reject");
            await Task.Delay(25, ct);
        }

        throw new TimeoutException($"timeout waiting for opcodes: {string.Join(',', opcodes.Select(o => $"0x{o:X4}"))}");
    }
}