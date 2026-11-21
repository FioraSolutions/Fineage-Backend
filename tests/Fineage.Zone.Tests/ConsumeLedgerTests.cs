using Fineage.Accounts;
using Fineage.Game.Economy;
using Fineage.Protocol;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>P1-5f: consumption ledger postings.</summary>
public sealed class ConsumeLedgerTests
{
    const int HealingPotion = 1060;
    static readonly LedgerAccount Consumed = LedgerAccount.Of(SystemAccount.Consumed);

    [Fact]
    public async Task Potion_use_posts_consume_transaction()
    {
        var bench = new WorldBench();
        var player = await bench.EnterAsync(items: [new StoredItem(HealingPotion, 3, -1)]);
        Assert.True(bench.World.TryGetLivePlayer(player.Session, out var live));
        var potion = live.Inventory!.FindByItemId(HealingPotion);
        Assert.NotNull(potion);
        int before = bench.Ledger.Entries.Count;
        Assert.Null(await bench.World.TryUseItemAsync(player.Session, potion!.ObjectId, CancellationToken.None));
        var tx = Assert.Single(bench.Ledger.Entries.Skip(before), e => e.Transaction.Kind == LedgerTxKind.Consume).Transaction;
        Assert.Equal(HealingPotion, tx.Reference);
        var bag = LedgerAccount.Inventory(player.ObjectId);
        Assert.Contains(tx.Postings, p => p.Account == Consumed && p.ItemId == HealingPotion && p.Amount == 1);
        Assert.Contains(tx.Postings, p => p.Account == bag && p.ItemId == HealingPotion && p.Amount == -1);
    }
}