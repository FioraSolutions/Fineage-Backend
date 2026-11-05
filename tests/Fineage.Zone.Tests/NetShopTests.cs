using Fineage.Game;
using Fineage.Game.Shops;
using Fineage.Login;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Net;
using Fineage.Zone.Shops;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Shop rules over the Net bridge: what a merchant list, a purchase, a sale, and a multisell hand the client,
/// plus every refusal that must not move adena. The retired V2 suite drove these through MessagePack frames;
/// here the world is the driver and the recorded <see cref="INetPresenceBridge"/> traffic is the client.
/// </summary>
public sealed class NetShopTests
{
    const int MerchantObjectId = 61000;
    const int MerchantNpcId = 30001;
    const int Adena = 57;
    const int Sword = 100;
    const int Potion = 200;
    const int Stone = 300;
    const int Anvil = 400;
    const int Blade = 500;

    [Fact]
    public async Task Buy_sell_and_multisell_move_adena_and_send_inventory_updates()
    {
        var clock = new MutableClock();
        WorldBench bench = Flat(clock, Kit((Adena, 1000), (Sword, 1)));
        BenchPlayer buyer = await bench.EnterAsync("buyer", "Buyer");

        await buyer.BypassAsync(MerchantObjectId, "Buy 1");
        var list = LastBuyList(bench.Rec);
        Assert.Equal(1, list.ListId);
        Assert.Equal(40, list.Items.Single(item => item.ItemId == Potion).Price);
        Assert.Equal(-1, list.Items.Single(item => item.ItemId == Potion).Count);
        Assert.Equal(0, list.CastleTaxPercent);
        Assert.Equal(1000, LastBalance(bench.Rec).Adena);

        Assert.Null(await buyer.Buy(MerchantObjectId, 1, (Potion, 2)));
        var bought = LastInventory(bench.Rec);
        Assert.Contains(bought.Changes, row => row.ItemId == Adena && row.Count == 920);
        Assert.Contains(bought.Changes,
            row => row.ItemId == Potion && row.Count == 2 && row.Change == InventoryChange.Add);
        Assert.Equal(ShopProtocol.TransactionComplete, LastSystemMessage(bench.Rec).Id);
        Assert.Equal(920, LastBalance(bench.Rec).Adena);

        Assert.Null(await buyer.Buy(MerchantObjectId, 1, (Potion, 100)));
        Assert.Equal(ShopProtocol.NotEnoughAdena, LastSystemMessage(bench.Rec).Id);
        Assert.Equal(920, LastBalance(bench.Rec).Adena);

        await buyer.BypassAsync(MerchantObjectId, "Sell");
        var sell = LastSellList(bench.Rec);
        var sword = Assert.Single(sell.Items, item => item.ItemId == Sword);
        Assert.Equal(500, sword.Price);

        Assert.Null(await buyer.Sell(MerchantObjectId, (sword.ObjectId, 1)));
        var sold = LastInventory(bench.Rec);
        Assert.Contains(sold.Changes, row => row.ObjectId == sword.ObjectId && row.Change == InventoryChange.Remove);
        Assert.Contains(sold.Changes, row => row.ItemId == Adena && row.Count == 1420);
        Assert.Equal(ShopProtocol.TransactionComplete, LastSystemMessage(bench.Rec).Id);
        Assert.Equal(1420, LastBalance(bench.Rec).Adena);

        await buyer.BypassAsync(MerchantObjectId, "Multisell 2");
        var multi = LastMultiSell(bench.Rec);
        Assert.False(multi.InventoryOnly);
        // The client echoes the entry id the list handed it, not the definition's ordinal.
        int entryId = multi.Entries[0].EntryId;
        Assert.Equal(200_000, entryId);
        Assert.Null(await buyer.MultiSell(MerchantObjectId, 2, entryId, 1));
        var exchanged = LastInventory(bench.Rec);
        Assert.Contains(exchanged.Changes,
            row => row.ItemId == Blade && row.EnchantLevel == 0 && row.Change == InventoryChange.Add);
        Assert.Equal(ShopProtocol.TransactionComplete, LastSystemMessage(bench.Rec).Id);
        Assert.Equal(1320, LastBalance(bench.Rec).Adena);

        // The bag survived the round trip to the store.
        Assert.Equal(1320, buyer.Saved().Single(item => item.ItemId == Adena).Count);
    }

    [Fact]
    public async Task Weight_slots_and_limited_stock_refuse_without_taking_adena()
    {
        var clock = new MutableClock();
        WorldBench bench = Limited(clock);
        BenchPlayer actor = await bench.EnterAsync("limits", "Limits", Bag((Adena, 1000)));

        await actor.BypassAsync(MerchantObjectId, "Buy 7");
        var list = LastBuyList(bench.Rec);
        Assert.Equal(1, list.Items.Single(item => item.ItemId == Stone).Count);
        Assert.Equal(1000, LastBalance(bench.Rec).Adena);

        Assert.Null(await actor.Buy(MerchantObjectId, 7, (Anvil, 1)));
        Assert.Equal(ShopProtocol.WeightLimit, LastSystemMessage(bench.Rec).Id);
        NoInventoryUpdate(bench.Rec);
        Assert.Equal(1000, LastBalance(bench.Rec).Adena);

        Assert.Null(await actor.Buy(MerchantObjectId, 7, (Stone, 1)));
        Assert.Contains(LastInventory(bench.Rec).Changes,
            row => row.ItemId == Stone && row.Count == 1 && row.Change == InventoryChange.Add);
        Assert.Equal(ShopProtocol.TransactionComplete, LastSystemMessage(bench.Rec).Id);
        Assert.Equal(0, LastBuyList(bench.Rec).Items.Single(item => item.ItemId == Stone).Count);
        Assert.Equal(950, LastBalance(bench.Rec).Adena);

        Assert.Null(await actor.Buy(MerchantObjectId, 7, (Stone, 1)));
        Assert.Equal(ShopProtocol.OutOfStock, LastSystemMessage(bench.Rec).Id);
        Assert.Equal(950, LastBalance(bench.Rec).Adena);

        clock.Now = clock.Now.AddMinutes(2);
        await actor.BypassAsync(MerchantObjectId, "Buy 7");
        Assert.Equal(1, LastBuyList(bench.Rec).Items.Single(item => item.ItemId == Stone).Count);
    }

    [Fact]
    public async Task Giran_buy_list_shows_base_tax_and_owner_tax()
    {
        var clock = new MutableClock();
        WorldBench bench = Flat(clock, Kit((Adena, 1000)), mapId: "giran");
        BenchPlayer actor = await bench.EnterAsync("taxed", "Taxed");

        var castle = bench.World.FindCastle(3);
        Assert.NotNull(castle);

        await actor.BypassAsync(MerchantObjectId, "Buy 1");
        var untaxedOwner = LastBuyList(bench.Rec);
        Assert.Equal(10, untaxedOwner.BaseTaxPercent);
        Assert.Equal(0, untaxedOwner.CastleTaxPercent);
        Assert.Equal(44, untaxedOwner.Items.Single(item => item.ItemId == Potion).Price);

        castle!.OwnerClanId = 8;
        castle.TaxPercent = 10;
        await actor.BypassAsync(MerchantObjectId, "Buy 1");
        var taxed = LastBuyList(bench.Rec);
        Assert.Equal(10, taxed.CastleTaxPercent);
        Assert.Equal(48, taxed.Items.Single(item => item.ItemId == Potion).Price);
    }

    [Fact]
    public async Task A_full_bag_refuses_another_slot()
    {
        var rows = new List<(int, int)> { (Adena, 1000) };
        for (int i = 0; i < 79; i++)
            rows.Add((Sword, 1));

        WorldBench bench = Flat(new MutableClock(), Kit(rows.ToArray()));
        BenchPlayer actor = await bench.EnterAsync("full", "Full");

        await actor.BypassAsync(MerchantObjectId, "Buy 1");
        Assert.Null(await actor.Buy(MerchantObjectId, 1, (Potion, 1)));
        Assert.Equal(ShopProtocol.InventoryFull, LastSystemMessage(bench.Rec).Id);
        Assert.Equal(1000, LastBalance(bench.Rec).Adena);
    }

    [Fact]
    public async Task A_purchase_needs_the_list_open_and_a_merchant_in_range()
    {
        WorldBench bench = Flat(new MutableClock(), Kit((Adena, 1000)));
        BenchPlayer actor = await bench.EnterAsync("idle", "Idle", Bag((Adena, 1000)));

        // No bypass first: the world refuses the buy rather than guessing which list was meant.
        Assert.Equal("bad_request", await actor.Buy(MerchantObjectId, 1, (Potion, 1)));

        await actor.BypassAsync(MerchantObjectId, "Buy 1");
        Assert.Equal("bad_request", await actor.Buy(MerchantObjectId, 2, (Potion, 1)));

        // Asking a missing NPC, and asking a shop from out of talk range, are both refusals.
        Assert.Equal(QuestProtocol.Npc, await actor.Buy(999_999, 1, (Potion, 1)));
        BenchPlayer far = await bench.EnterAsync("far", "Far", Bag((Adena, 1000)), x: 400_000, y: 400_000);
        await far.BypassAsync(MerchantObjectId, "Buy 1");
        Assert.Equal("range", await far.Buy(MerchantObjectId, 1, (Potion, 1)));

        // A dead player cannot shop.
        Assert.True(bench.World.TrySetPlayerDead(actor.Ticket.CharacterId, true));
        Assert.Equal(InventoryProtocol.Dead, await actor.Buy(MerchantObjectId, 1, (Potion, 1)));
    }

    [Fact]
    public async Task Selling_requires_the_sell_window_and_a_sellable_row()
    {
        WorldBench bench = Flat(new MutableClock(), Kit((Adena, 1000), (Sword, 1)));
        BenchPlayer actor = await bench.EnterAsync("seller", "Seller");

        int sword = actor.Slot(Sword);
        Assert.Equal("bad_request", await actor.Sell(MerchantObjectId, (sword, 1)));

        await actor.BypassAsync(MerchantObjectId, "Sell");
        Assert.Null(await actor.Sell(MerchantObjectId, (sword, 1)));
        Assert.Contains(LastInventory(bench.Rec).Changes,
            row => row.ObjectId == sword && row.Change == InventoryChange.Remove);
        Assert.Equal(1500, LastBalance(bench.Rec).Adena);

        // Adena is not sellable, and a second sale of the same row has nothing left to take.
        int adena = actor.Slot(Adena);
        Assert.Null(await actor.Sell(MerchantObjectId, (adena, 5)));
        Assert.Equal(ShopProtocol.NotTradable, LastSystemMessage(bench.Rec).Id);
        Assert.Null(await actor.Sell(MerchantObjectId, (sword, 1)));
        Assert.Equal(ShopProtocol.NotTradable, LastSystemMessage(bench.Rec).Id);
        Assert.Equal(1500, LastBalance(bench.Rec).Adena);
    }

    static WorldBench Flat(MutableClock clock, GameData game, string mapId = "shop_flat") =>
        new(game, clock: () => clock.Now, spawns: MerchantSpawns(mapId), mapId: mapId,
            buyLists: BuyLists(), multisells: MultiLists());

    static WorldBench Limited(MutableClock clock) =>
        new(GameData.From(Catalog(), new StarterKits([])),
            clock: () => clock.Now, spawns: MerchantSpawns("shop_flat"), mapId: "shop_flat",
            buyLists: LimitLists(), multisells: MultiLists());

    static StoredItem[] Bag(params (int ItemId, int Count)[] rows) =>
        rows.Select(row => new StoredItem(row.ItemId, row.Count, -1)).ToArray();

    static Fineage.Zone.Spawns.SpawnCatalog MerchantSpawns(string mapId) =>
        Spawns.Merchant(MerchantObjectId, MerchantNpcId, mapId: mapId);

    /// <summary>The fighter's starter kit, so <see cref="WorldBench.EnterAsync"/> builds the bag from it.</summary>
    static GameData Kit(params (int ItemId, int Count)[] rows) =>
        GameData.From(Catalog(), new StarterKits(
        [
            new StarterKit(HumanClasses.Fighter,
                rows.Select(row => new StarterKitEntry(row.ItemId, row.Count, false)).ToArray())
        ]));

    static BuyListCatalog BuyLists() => BuyListCatalog.From(
    [
        new BuyListDefinition(1, [MerchantNpcId], [new BuyProduct(Potion, 40), new BuyProduct(Blade, 100)])
    ]);

    static BuyListCatalog LimitLists() => BuyListCatalog.From(
    [
        new BuyListDefinition(7, [MerchantNpcId],
        [
            new BuyProduct(Potion, 40),
            new BuyProduct(Stone, 50, maxCount: 1, restockMinutes: 1),
            new BuyProduct(Anvil, 10)
        ])
    ]);

    static MultiSellCatalog MultiLists() => MultiSellCatalog.From(
    [
        new MultiSellListDefinition(2, false, false, [MerchantNpcId],
        [
            new MultiSellEntry(2, [new MultiSellPart(Potion, 1), new MultiSellPart(Adena, 100)],
                [new MultiSellPart(Blade, 1)])
        ])
    ]);

    static ItemCatalog Catalog() => new(
    [
        new ItemTemplate(Adena, "Adena", ItemKind.Etc, BodySlot.None, "icon.adena", Stackable: true,
            ReferencePrice: 1, Sellable: false),
        new ItemTemplate(Sword, "Sword", ItemKind.Weapon, BodySlot.RightHand, "icon.sword", WeaponType.Sword,
            ReferencePrice: 1000, Weight: 100),
        new ItemTemplate(Potion, "Potion", ItemKind.Etc, BodySlot.None, "icon.potion", Stackable: true,
            ReferencePrice: 40, Weight: 1),
        new ItemTemplate(Stone, "Stone", ItemKind.Etc, BodySlot.None, "icon.stone", Stackable: true,
            ReferencePrice: 50, Weight: 1),
        new ItemTemplate(Anvil, "Anvil", ItemKind.Etc, BodySlot.None, "icon.anvil", ReferencePrice: 10,
            Weight: 5_000_000),
        new ItemTemplate(Blade, "Blade", ItemKind.Weapon, BodySlot.RightHand, "icon.blade", WeaponType.Sword,
            ReferencePrice: 2000, Weight: 100)
    ]);

    static BuyListNetMsg LastBuyList(NetBridgeRecorder rec) =>
        Assert.IsType<BuyListNetMsg>(rec.Of(nameof(INetPresenceBridge.FanoutBuyList)).Last()[1]);

    static SellListNetMsg LastSellList(NetBridgeRecorder rec) =>
        Assert.IsType<SellListNetMsg>(rec.Of(nameof(INetPresenceBridge.FanoutSellList)).Last()[1]);

    static MultiSellListNetMsg LastMultiSell(NetBridgeRecorder rec) =>
        Assert.IsType<MultiSellListNetMsg>(rec.Of(nameof(INetPresenceBridge.FanoutMultiSellList)).Last()[1]);

    static ShopBalanceNetMsg LastBalance(NetBridgeRecorder rec) =>
        Assert.IsType<ShopBalanceNetMsg>(rec.Of(nameof(INetPresenceBridge.FanoutShopBalance)).Last()[1]);

    static InventoryUpdateNetMsg LastInventory(NetBridgeRecorder rec) =>
        Assert.IsType<InventoryUpdateNetMsg>(rec.Of(nameof(INetPresenceBridge.FanoutInventoryUpdate)).Last()[1]);

    static SystemMessageNetMsg LastSystemMessage(NetBridgeRecorder rec) =>
        Assert.IsType<SystemMessageNetMsg>(rec.Of(nameof(INetPresenceBridge.FanoutSystemMessage)).Last()[1]);

    static void NoInventoryUpdate(NetBridgeRecorder rec) =>
        Assert.Empty(rec.Of(nameof(INetPresenceBridge.FanoutInventoryUpdate)));
}
