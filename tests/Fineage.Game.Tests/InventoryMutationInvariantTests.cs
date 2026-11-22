using Fineage.Game;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>
/// One container, one stack rule, one object id. A random sequence of adds, reductions, equips, and enchants
/// must conserve counts, never duplicate an object id, and reload from its own snapshot as the same bag.
/// </summary>
public sealed class InventoryMutationInvariantTests
{
    const int Potion = 1;
    const int Adena = 2;
    const int Sword = 3;

    [Fact]
    public void Randomized_mutations_conserve_items_and_reload_equals_memory()
    {
        var catalog = new ItemCatalog(
        [
            new ItemTemplate(Potion, "Potion", ItemKind.Etc, BodySlot.None, "icon.potion", Stackable: true),
            new ItemTemplate(Adena, "Adena", ItemKind.Etc, BodySlot.None, "icon.adena", Stackable: true),
            new ItemTemplate(Sword, "Sword", ItemKind.Weapon, BodySlot.RightHand, "icon.sword", WeaponType.Sword)
        ]);
        var inventory = new PlayerInventory(catalog, firstObjectId: PlayerInventory.FirstObjectId);
        var rng = new Random(868);
        int expectedPotion = 0;
        int expectedAdena = 0;
        int expectedSwords = 0;

        for (int step = 0; step < 400; step++)
        {
            switch (rng.Next(7))
            {
                case 0:
                    int potions = rng.Next(1, 5);
                    inventory.Add(Potion, potions);
                    expectedPotion += potions;
                    break;
                case 1:
                    int adena = rng.Next(1, 20);
                    inventory.Add(Adena, adena);
                    expectedAdena += adena;
                    break;
                case 2:
                    inventory.Add(Sword);
                    expectedSwords++;
                    break;
                case 3:
                    var potion = inventory.FindByItemId(Potion);
                    if (potion != null)
                    {
                        int take = rng.Next(1, potion.Count + 1);
                        if (inventory.TryReduce(potion, take, out _))
                            expectedPotion -= take;
                    }
                    break;
                case 4:
                    var loose = FirstUnequipped(inventory, Sword);
                    if (loose != null)
                        inventory.Equip(loose);
                    break;
                case 5:
                    var worn = inventory.GetEquipped(PaperdollSlot.RHand);
                    if (worn != null)
                        inventory.Unequip(worn);
                    break;
                default:
                    var piece = FirstUnequipped(inventory, Sword) ?? inventory.GetEquipped(PaperdollSlot.RHand);
                    if (piece != null)
                        inventory.SetEnchantLevel(piece, rng.Next(0, 8));
                    var coin = inventory.FindByItemId(Adena);
                    if (coin != null)
                    {
                        int take = rng.Next(1, coin.Count + 1);
                        if (inventory.TryReduce(coin, take, out _))
                            expectedAdena -= take;
                    }
                    var spare = FirstUnequipped(inventory, Sword);
                    if (spare != null && inventory.TryReduce(spare, 1, out _))
                        expectedSwords--;
                    break;
            }

            Assert.Equal(expectedPotion, inventory.CountOf(Potion));
            Assert.Equal(expectedAdena, inventory.CountOf(Adena));
            Assert.Equal(expectedSwords, inventory.CountOf(Sword));
            AssertBag(inventory);

            if (step % 17 != 0)
                continue;

            var published = inventory.DrainChanges();
            var seen = new HashSet<int>();
            foreach (var change in published)
            {
                Assert.True(seen.Add(change.Item.ObjectId));
                Assert.InRange(change.Change, PlayerInventory.ChangeAdd, PlayerInventory.ChangeRemove);
                if (change.Change == PlayerInventory.ChangeRemove)
                {
                    Assert.Equal(0, change.Count);
                    Assert.Null(inventory.Find(change.Item.ObjectId));
                }
                else
                {
                    var live = inventory.Find(change.Item.ObjectId);
                    Assert.NotNull(live);
                    Assert.Equal(live.Count, change.Count);
                }
            }

            Assert.False(inventory.HasPendingChanges);
            var reloaded = new PlayerInventory(catalog, new ItemObjectIdAllocator(PlayerInventory.FirstObjectId));
            reloaded.ReplaceFromStored(inventory.SnapshotRows());
            AssertSame(inventory, reloaded);
        }
    }

    static ItemInstance? FirstUnequipped(PlayerInventory inventory, int itemId)
    {
        foreach (var item in inventory.Items)
        {
            if (item.ItemId == itemId && !item.IsEquipped)
                return item;
        }

        return null;
    }

    static void AssertBag(PlayerInventory inventory)
    {
        var ids = new HashSet<int>();
        var stackables = new HashSet<int>();
        foreach (var item in inventory.Items)
        {
            Assert.True(item.Count >= 1);
            Assert.True(ids.Add(item.ObjectId));
            if (!item.Template.Stackable)
                Assert.Equal(1, item.Count);
            else
                Assert.True(stackables.Add(item.ItemId));
            if (item.IsEquipped)
                Assert.Same(item, inventory.GetEquipped(item.EquippedSlot!.Value));
        }

        for (var slot = PaperdollSlot.Under; slot <= PaperdollSlot.Hair2; slot++)
        {
            var worn = inventory.GetEquipped(slot);
            if (worn == null)
                continue;
            Assert.Equal(slot, worn.EquippedSlot);
            Assert.Contains(worn, inventory.Items);
        }
    }

    static void AssertSame(PlayerInventory live, PlayerInventory reloaded)
    {
        var left = live.Items.OrderBy(item => item.ObjectId).ToArray();
        var right = reloaded.Items.OrderBy(item => item.ObjectId).ToArray();
        Assert.Equal(left.Length, right.Length);
        for (int i = 0; i < left.Length; i++)
        {
            Assert.Equal(left[i].ObjectId, right[i].ObjectId);
            Assert.Equal(left[i].ItemId, right[i].ItemId);
            Assert.Equal(left[i].Count, right[i].Count);
            Assert.Equal(left[i].EnchantLevel, right[i].EnchantLevel);
            Assert.Equal(left[i].EquippedSlot, right[i].EquippedSlot);
        }
    }
}
