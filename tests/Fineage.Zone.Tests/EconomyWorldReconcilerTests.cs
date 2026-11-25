using Fineage.Game.Economy;
using Fineage.Zone.World;
using Xunit;
using static Fineage.Zone.Tests.NetShopTests;

namespace Fineage.Zone.Tests;

/// <summary>
/// World projection of the auditable ledger: every live player bag and every ground stack the ledger credits must
/// exist in the world exactly as recorded, otherwise a shop buy, a festival drop or a leaving character has
/// silently desynced the durable record from the world.
///
/// The bench builds characters straight through the account store, so no create-grant is posted for them; these
/// tests adopt the bag with the same <see cref="LedgerTxKind.Adjustment"/> against
/// <see cref="SystemAccount.Untracked"/> that <see cref="LedgerMove.Observe"/> uses to align a journal, and then
/// assert that later moves keep the projection in step.
/// </summary>
public sealed class EconomyWorldReconcilerTests
{
    [Fact]
    public async Task An_entered_character_agrees_with_the_ledger()
    {
        WorldBench bench = Flat(new MutableClock(), Kit((Adena, 1_000)));
        BenchPlayer player = await bench.EnterAsync("recempty", "RecEmpty");
        AdoptBag(bench, player);

        Assert.Equal(1_000, player.Inventory.CountOf(Adena));
        Assert.Empty(EconomyWorldReconciler.Check(bench.World));
    }

    [Fact]
    public async Task A_shop_buy_keeps_bag_and_ledger_in_step()
    {
        WorldBench bench = Flat(new MutableClock(), Kit((Adena, 1_000)));
        BenchPlayer buyer = await bench.EnterAsync("recbuy", "RecBuy");
        AdoptBag(bench, buyer);

        await buyer.BypassAsync(MerchantObjectId, "Buy 1");
        Assert.Null(await buyer.Buy(MerchantObjectId, 1, (Potion, 2)));

        Assert.True(buyer.Inventory.CountOf(Potion) == 2,
            $"bag potion={buyer.Inventory.CountOf(Potion)} adena={buyer.Inventory.CountOf(Adena)} ledger=" +
            string.Join("|", bench.Ledger.Entries.Select(e => e.Transaction.Kind)) + " issues=" +
            string.Join("|", EconomyWorldReconciler.Check(bench.World).Select(i => i.Code + ":" + i.Message)));
        Assert.Empty(EconomyWorldReconciler.Check(bench.World));
    }

    /// <summary>
    /// The reconciler must report a skew rather than pass: a bag row the ledger never learned about is exactly
    /// the desync E5-E6 closed.
    /// </summary>
    [Fact]
    public async Task A_bag_row_the_ledger_does_not_know_is_reported()
    {
        WorldBench bench = Flat(new MutableClock(), Kit((Adena, 1_000)));
        BenchPlayer player = await bench.EnterAsync("recskew", "RecSkew");
        AdoptBag(bench, player);

        player.Inventory.Add(Potion, 1);

        var issue = Assert.Single(EconomyWorldReconciler.Check(bench.World));
        Assert.Equal("inventory_projection_skew", issue.Code);
        Assert.Contains($"char={player.ObjectId}", issue.Message);
        Assert.Contains($"{Potion}x1", issue.Message);
    }

    /// <summary>A ground account the ledger credits with no live stack under it is a lost drop: reported, never corrected.</summary>
    [Fact]
    public async Task A_ledger_ground_account_without_a_live_stack_is_reported()
    {
        WorldBench bench = Flat(new MutableClock(), Kit((Adena, 1_000)));
        BenchPlayer player = await bench.EnterAsync("recground", "RecGround");
        AdoptBag(bench, player);

        const int orphanGround = 900_001;
        var result = bench.World.LedgerForTests.Submit(new LedgerTransaction(
            new LedgerTxId(1, 90),
            LedgerTxKind.Drop,
            orphanGround,
            0,
            1,
            [
                new LedgerPosting(LedgerAccount.Of(SystemAccount.Untracked), Adena, -1),
                new LedgerPosting(LedgerAccount.Ground(orphanGround), Adena, 1),
            ]));
        Assert.True(result.Ok, result.Rejection.ToString());

        var issue = Assert.Single(EconomyWorldReconciler.Check(bench.World));
        Assert.Equal("ground_missing_entity", issue.Code);
        Assert.Contains(orphanGround.ToString(), issue.Message);
    }

    /// <summary>
    /// Posts the current bag as an untracked-origin adjustment, the same alignment <see cref="LedgerMove.Observe"/>
    /// performs, so the journal explains every row the bench character starts with.
    /// </summary>
    static void AdoptBag(WorldBench bench, BenchPlayer player)
    {
        var bag = LedgerAccount.Inventory(player.ObjectId);
        var current = LedgerInventoryChange.HoldingsFrom(player.Inventory);
        var move = new LedgerMove(
            bench.World.LedgerForTests, () => new LedgerTxId(1, 99), LedgerTxKind.Drop, 1, player.ObjectId, 0);
        move.Observe(bag, current);
    }
}
