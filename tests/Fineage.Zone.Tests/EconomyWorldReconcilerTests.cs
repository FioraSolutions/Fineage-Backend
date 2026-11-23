using Fineage.Game.Services;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class EconomyWorldReconcilerTests
{
    [Fact]
    public async Task Online_bag_matches_ledger_after_shop_buy()
    {
        var bench = WorldBench.Flat();
        var player = await bench.EnterAsync(items: [new StoredItem(WarehouseRules.AdenaItemId, 1_000, -1)]);
        Assert.Empty(EconomyWorldReconciler.Check(bench.World));
    }
}