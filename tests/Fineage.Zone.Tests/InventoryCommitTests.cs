using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Protocol;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;
using static Fineage.Zone.Tests.NetShopTests;

namespace Fineage.Zone.Tests;

/// <summary>
/// Buy, destroy, equip, and unequip each publish one InventoryUpdate after the bag write, and the persisted
/// rows match memory. A gap request returns ItemList at the current version and does not bump it.
/// </summary>
public sealed class InventoryCommitTests
{
    [Fact]
    public async Task Buy_destroy_equip_and_unequip_each_commit_one_versioned_update()
    {
        var clock = new MutableClock();
        WorldBench bench = Flat(clock, Kit((Adena, 1000), (Sword, 1)));
        BenchPlayer buyer = await bench.EnterAsync("buyer", "Buyer");
        Assert.Equal(1, buyer.Inventory.Version);
        Assert.Empty(Updates(bench.Rec));
        AssertPersisted(buyer);

        await buyer.BypassAsync(MerchantObjectId, "Buy 1");
        Assert.Null(await buyer.Buy(MerchantObjectId, 1, (Potion, 2)));
        var bought = AssertNextUpdate(bench, buyer, expectedCount: 1, expectedVersion: 2);
        Assert.Contains(bought.Changes, row => row.ItemId == Potion && row.Count == 2 && row.Change == InventoryChange.Add);
        Assert.Contains(bought.Changes, row => row.ItemId == Adena && row.Count == 920 && row.Change == InventoryChange.Modify);
        AssertPersisted(buyer);
        AssertLedgerMatchesBag(bench, buyer);

        int potion = buyer.Slot(Potion);
        Assert.Null(await bench.World.TryDestroyItemAsync(buyer.Session, potion, 1, CancellationToken.None));
        var destroyed = AssertNextUpdate(bench, buyer, expectedCount: 2, expectedVersion: 3);
        Assert.Contains(destroyed.Changes, row => row.ObjectId == potion && row.Count == 1 && row.Change == InventoryChange.Modify);
        AssertPersisted(buyer);

        int beforeRefuse = Updates(bench.Rec).Length;
        long version = buyer.Inventory.Version;
        Assert.NotNull(await bench.World.TryDestroyItemAsync(buyer.Session, potion, 50, CancellationToken.None));
        Assert.Equal(beforeRefuse, Updates(bench.Rec).Length);
        Assert.Equal(version, buyer.Inventory.Version);
        Assert.Equal(1, buyer.Inventory.CountOf(Potion));

        int sword = buyer.Slot(Sword);
        Assert.Null(await buyer.Use(sword));
        var equipped = AssertNextUpdate(bench, buyer, expectedCount: 3, expectedVersion: 4);
        var worn = Assert.Single(equipped.Changes);
        Assert.Equal(sword, worn.ObjectId);
        Assert.Equal(InventoryChange.Modify, worn.Change);
        Assert.Equal((int)PaperdollSlot.RHand, worn.EquippedSlot);
        AssertPersisted(buyer);
        Assert.Equal(LedgerAccount.Inventory(buyer.ObjectId), bench.World.LedgerForTests.OwnerOf(sword));

        Assert.Null(await buyer.Unequip((int)PaperdollSlot.RHand));
        var bagged = AssertNextUpdate(bench, buyer, expectedCount: 4, expectedVersion: 5);
        var returned = Assert.Single(bagged.Changes);
        Assert.Equal(sword, returned.ObjectId);
        Assert.Equal(InventoryChange.Modify, returned.Change);
        Assert.Equal(-1, returned.EquippedSlot);
        Assert.Null(buyer.Inventory.Find(sword)!.EquippedSlot);
        AssertPersisted(buyer);

        Assert.Null(await bench.World.TryDestroyItemAsync(buyer.Session, potion, 1, CancellationToken.None));
        var removed = AssertNextUpdate(bench, buyer, expectedCount: 5, expectedVersion: 6);
        Assert.Contains(removed.Changes, row => row.ObjectId == potion && row.Change == InventoryChange.Remove && row.Count == 0);
        Assert.Null(buyer.Inventory.Find(potion));
        AssertPersisted(buyer);

        Assert.Null(await bench.World.TryGrantItemAsync(buyer.Session, Potion, 3, CancellationToken.None));
        var granted = AssertNextUpdate(bench, buyer, expectedCount: 6, expectedVersion: 7);
        Assert.Contains(granted.Changes, row => row.ItemId == Potion && row.Count == 3 && row.Change == InventoryChange.Add);
        AssertPersisted(buyer);
        AssertLedgerMatchesBag(bench, buyer);
        Assert.Null(LedgerChain.FirstBroken(bench.Ledger.Entries));

        int updates = Updates(bench.Rec).Length;
        Assert.Null(bench.World.TrySendItemList(buyer.Session));
        var list = Assert.IsType<ItemListMsg>(bench.Rec.Of(nameof(IWorldEventPublisher.FanoutItemList)).Single()[1]);
        Assert.Equal(7, list.Version);
        Assert.Equal(buyer.Inventory.Version, list.Version);
        Assert.Equal(updates, Updates(bench.Rec).Length);
        Assert.Equal(buyer.Inventory.Items.Count, list.Items.Length);
        Assert.Equal(
            buyer.Inventory.Items.Select(item => item.ObjectId).OrderBy(id => id),
            list.Items.Select(item => item.ObjectId).OrderBy(id => id));
    }

    static InventoryUpdateMsg AssertNextUpdate(WorldBench bench, BenchPlayer player, int expectedCount, int expectedVersion)
    {
        var updates = Updates(bench.Rec);
        Assert.Equal(expectedCount, updates.Length);
        var update = updates[^1];
        Assert.Equal(expectedVersion, update.Version);
        Assert.Equal(expectedVersion, player.Inventory.Version);
        return update;
    }

    static InventoryUpdateMsg[] Updates(NetBridgeRecorder rec) =>
        rec.Of(nameof(IWorldEventPublisher.FanoutInventoryUpdate))
            .Select(args => Assert.IsType<InventoryUpdateMsg>(args[1]))
            .ToArray();

    static void AssertPersisted(BenchPlayer player)
    {
        var saved = player.Saved().OrderBy(item => item.ObjectId).ToArray();
        var live = player.Inventory.Items.OrderBy(item => item.ObjectId).ToArray();
        Assert.Equal(live.Length, saved.Length);
        var ids = new HashSet<int>();
        for (int i = 0; i < live.Length; i++)
        {
            Assert.True(ids.Add(live[i].ObjectId));
            Assert.Equal(live[i].ObjectId, saved[i].ObjectId);
            Assert.Equal(live[i].ItemId, saved[i].ItemId);
            Assert.Equal(live[i].Count, saved[i].Count);
            Assert.Equal(live[i].EnchantLevel, saved[i].EnchantLevel);
            int slot = live[i].EquippedSlot is { } worn ? (int)worn : -1;
            Assert.Equal(slot, saved[i].EquippedSlot);
        }
    }

    static void AssertLedgerMatchesBag(WorldBench bench, BenchPlayer player)
    {
        var bag = LedgerAccount.Inventory(player.ObjectId);
        var holdings = bench.World.LedgerForTests.HoldingsOf(bag);
        Assert.Equal(player.Inventory.CountOf(Adena), holdings.CountOf(Adena));
        Assert.Equal(player.Inventory.CountOf(Potion), holdings.CountOf(Potion));
        Assert.Equal(player.Inventory.CountOf(Sword), holdings.CountOf(Sword));
    }
}
