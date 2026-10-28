using Fineage.Game.Shops;
using Xunit;

namespace Fineage.Game.Tests;

public sealed class ShopRulesTests
{
    [Fact]
    public void Talking_island_lists_resolve_and_the_full_catalog_is_loaded()
    {
        var data = GameData.LoadDefault();
        var buys = BuyListCatalog.LoadDefault();
        var multis = MultiSellCatalog.LoadDefault();

        Assert.True(buys.Lists.Count >= 600);
        Assert.True(multis.Lists.Count >= 80);
        Assert.True(buys.TryGet(3000100, out var fighter));
        Assert.Contains(30001, fighter.Npcs);
        Assert.Equal(768, fighter.Find(1)!.Price);
        Assert.True(data.Items.TryGet(1, out var sword));
        Assert.Equal(768, sword.ReferencePrice);
        Assert.True(sword.Weight > 0);

        Assert.True(multis.TryGet(3, out var exchange));
        Assert.Contains(30001, exchange.Npcs);
        Assert.False(exchange.MaintainEnchantment);
        Assert.Contains(exchange.Entries, entry => entry.Products.Any(part => data.Items.TryGet(part.ItemId, out _)));
    }

    [Fact]
    public void Buy_price_adds_town_tax_and_castle_tax_only_when_asked()
    {
        Assert.Equal(1000, ShopTrade.TaxedUnitPrice(1000, 0, 0));
        Assert.Equal(1150, ShopTrade.TaxedUnitPrice(1000, 15, 0));
        Assert.Equal(1250, ShopTrade.TaxedUnitPrice(1000, 15, 10));
        Assert.Equal(500, ShopTrade.TaxedUnitPrice(1000, 0, 0) / 2);
    }

    [Fact]
    public void Sell_pays_half_the_reference_price_and_refuses_adena()
    {
        var inventory = Bag(adena: 100, swordPrice: 1000, swordEnchant: 0);
        var sword = inventory.Items.Single(item => item.ItemId == 100);
        var sold = ShopTrade.Sell(inventory, [new ShopObjectLine(sword.ObjectId, 1)]);

        Assert.True(sold.Ok);
        Assert.Null(inventory.Find(sword.ObjectId));
        Assert.Equal(600, inventory.CountOf(ShopTrade.AdenaItemId));

        var adena = inventory.FindByItemId(ShopTrade.AdenaItemId)!;
        Assert.False(ShopTrade.Sell(inventory, [new ShopObjectLine(adena.ObjectId, 1)]).Ok);
        Assert.Equal(600, inventory.CountOf(ShopTrade.AdenaItemId));
    }

    [Fact]
    public void Buy_refuses_short_adena_weight_slots_and_restocks_limited_goods()
    {
        var catalog = Catalog();
        var list = new BuyListDefinition(1, [30001],
        [
            new BuyProduct(200, 40),
            new BuyProduct(300, 50, maxCount: 1, restockMinutes: 1),
            new BuyProduct(400, 10)
        ]);
        var stock = new ShopStock(BuyListCatalog.From([list]));
        var poor = Bag(adena: 10, swordPrice: 1000, swordEnchant: 0);
        var broke = ShopTrade.Buy(poor, catalog, list, [new ShopLine(200, 1)], 0, 0, maxLoad: 100_000, slotLimit: 80, stock, nowUnixMs: 0);
        Assert.Equal(ShopDeny.Adena, broke.Deny);
        Assert.Equal(10, poor.CountOf(57));

        var heavy = Bag(adena: 1000, swordPrice: 1000, swordEnchant: 0);
        var overweight = ShopTrade.Buy(heavy, catalog, list, [new ShopLine(400, 1)], 0, 0, maxLoad: 1000, slotLimit: 80, stock, 0);
        Assert.Equal(ShopDeny.Weight, overweight.Deny);

        var full = Bag(adena: 5000, swordPrice: 1000, swordEnchant: 0, extraPieces: 78);
        var slots = ShopTrade.Buy(full, catalog, list, [new ShopLine(200, 1)], 0, 0, maxLoad: 1_000_000, slotLimit: 80, stock, 0);
        Assert.Equal(ShopDeny.Slots, slots.Deny);

        var buyer = Bag(adena: 500, swordPrice: 1000, swordEnchant: 0);
        var first = ShopTrade.Buy(buyer, catalog, list, [new ShopLine(300, 1)], 0, 0, 1_000_000, 80, stock, nowUnixMs: 1_000);
        Assert.True(first.Ok);
        Assert.Equal(0, stock.Available(1, 300, 1_000));
        var second = ShopTrade.Buy(buyer, catalog, list, [new ShopLine(300, 1)], 0, 0, 1_000_000, 80, stock, nowUnixMs: 1_000);
        Assert.Equal(ShopDeny.Stock, second.Deny);
        Assert.Equal(1, stock.Available(1, 300, 1_000 + 60_000));
        var third = ShopTrade.Buy(buyer, catalog, list, [new ShopLine(300, 1)], 0, 0, 1_000_000, 80, stock, nowUnixMs: 1_000 + 60_000);
        Assert.True(third.Ok);
    }

    [Fact]
    public void Exc_multisell_keeps_the_ingredient_enchant()
    {
        var catalog = Catalog();
        var list = new MultiSellListDefinition(9, applyTaxes: true, maintainEnchantment: true, [30001],
        [
            new MultiSellEntry(1,
                [new MultiSellPart(100, 1), new MultiSellPart(57, 100, tax: true), new MultiSellPart(57, 50)],
                [new MultiSellPart(500, 1)])
        ]);
        var inventory = Bag(adena: 1000, swordPrice: 1000, swordEnchant: 7);
        var plain = ShopTrade.Prepare(list, inventory, catalog, inventoryOnly: false, applyCastleTax: true, castleTaxPercent: 10);
        Assert.Equal(0, plain.Entries[0].Products[0].Enchant);
        Assert.Equal(60, plain.Entries[0].Ingredients.Single(part => part.ItemId == 57).Count);

        var kept = ShopTrade.Prepare(list, inventory, catalog, inventoryOnly: true, applyCastleTax: true, castleTaxPercent: 10);
        Assert.True(kept.MaintainEnchantment);
        Assert.Equal(100_007, kept.Entries[0].EntryId);
        Assert.Equal(7, kept.Entries[0].Products[0].Enchant);
        Assert.Equal(60, kept.Entries[0].Ingredients.Single(part => part.ItemId == 57).Count);

        var done = ShopTrade.Exchange(inventory, catalog, kept.Entries[0], 1, maxLoad: 1_000_000, slotLimit: 80);
        Assert.True(done.Ok);
        Assert.Equal(7, inventory.Items.Single(item => item.ItemId == 500).EnchantLevel);
        Assert.Equal(940, inventory.CountOf(57));
        Assert.DoesNotContain(inventory.Items, item => item.ItemId == 100);
    }

    static PlayerInventory Bag(int adena, int swordPrice, int swordEnchant, int extraPieces = 0)
    {
        var inventory = new PlayerInventory(Catalog());
        if (adena > 0)
            inventory.Add(57, adena);
        var sword = inventory.Add(100, 1, swordEnchant);
        Assert.Equal(swordEnchant, sword.EnchantLevel);
        if (extraPieces > 0)
            inventory.Add(100, extraPieces);
        return inventory;
    }

    static ItemCatalog Catalog() => new(
    [
        new ItemTemplate(57, "Adena", ItemKind.Etc, BodySlot.None, "icon.adena", Stackable: true, ReferencePrice: 1, Sellable: false),
        new ItemTemplate(100, "Sword", ItemKind.Weapon, BodySlot.RightHand, "icon.sword", WeaponType.Sword, ReferencePrice: 1000, Weight: 100),
        new ItemTemplate(200, "Potion", ItemKind.Etc, BodySlot.None, "icon.potion", Stackable: true, ReferencePrice: 40, Weight: 1),
        new ItemTemplate(300, "Stone", ItemKind.Etc, BodySlot.None, "icon.stone", Stackable: true, ReferencePrice: 50, Weight: 1),
        new ItemTemplate(400, "Anvil", ItemKind.Etc, BodySlot.None, "icon.anvil", ReferencePrice: 10, Weight: 50_000),
        new ItemTemplate(500, "Blade", ItemKind.Weapon, BodySlot.RightHand, "icon.blade", WeaponType.Sword, ReferencePrice: 2000, Weight: 100)
    ]);

    [Fact]
    public void Fighter_weight_limit_uses_the_con_bonus()
    {
        var classes = GameData.LoadDefault().Classes;
        Assert.NotNull(classes);
        int limit = ShopTrade.MaxLoad(classes, 0);
        Assert.True(classes.TryGet(0, out var fighter));
        int expected = (int)Math.Floor(classes.StatBonuses.Bonus(StatSlot.Con, fighter!.Con) * ShopTrade.WeightLimitBase);
        Assert.Equal(expected, limit);
        Assert.True(limit > ShopTrade.WeightLimitBase);
        Assert.Equal(80, ShopTrade.SlotLimit(0));
        Assert.Equal(100, ShopTrade.SlotLimit(ShopTrade.DwarfRace));
    }
}
