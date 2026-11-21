using Fineage.Game.Economy;
using Fineage.Zone;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius AUTO_LOOT death drops (gap rank 5).</summary>
public sealed class AutoLootTests
{
    [Fact]
    public async Task Auto_loot_puts_death_drops_in_inventory_not_ground()
    {
        var rates = new ZoneRates { AutoLoot = true };
        var kit = new NetTestKit(rates: rates);
        var (_, killer) = await kit.EnterAsync();
        long before = killer.Inventory!.CountOf(57);
        kit.World.DropDeathLootForTest(MobiusDropTestSeed.TrainingHallGremlinNpcId, killer.X, killer.Y, killer.Z, killer);

        Assert.Equal(0, kit.World.GroundItemCountForTest());
        Assert.True(killer.Inventory.CountOf(57) > before);
        var loot = Assert.Single(kit.Ledger.Entries, e => e.Transaction.Kind == LedgerTxKind.Loot).Transaction;
        Assert.Contains(
            loot.Postings,
            posting => posting.Account.Container == LedgerContainer.Inventory && posting.Amount > 0);
        Assert.DoesNotContain(loot.Postings, posting => posting.Account.Container == LedgerContainer.Ground);
    }

    [Fact]
    public async Task Retail_rates_leave_loot_on_ground()
    {
        var kit = new NetTestKit(rates: ZoneRates.Retail);
        var (_, killer) = await kit.EnterAsync();
        kit.World.DropDeathLootForTest(MobiusDropTestSeed.TrainingHallGremlinNpcId, killer.X, killer.Y, killer.Z, killer);
        Assert.True(kit.World.GroundItemCountForTest() >= 1);
    }
}