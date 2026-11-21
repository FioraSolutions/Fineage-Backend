using Fineage.Accounts;
using Fineage.Game.Economy;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>P1-7: missing ground entities must not pretend the journal is empty.</summary>
public sealed class GroundStrictLedgerTests
{
    const int Adena = 57;

    [Fact]
    public async Task Despawn_without_ledger_move_throws_when_the_journal_still_holds_value()
    {
        var bench = new WorldBench();
        var player = await bench.EnterAsync(items: [new StoredItem(Adena, 50, -1)]);
        int groundId = bench.World.PlaceGroundForTest(player.Player, Adena, 10);
        bench.World.RemoveGroundForTest(groundId);
        var account = LedgerAccount.Ground(groundId);
        var ex = Assert.Throws<InvalidOperationException>(() => bench.World.LedgerHoldingsForTests(account));
        Assert.Contains(groundId.ToString(), ex.Message);
    }

    [Fact]
    public async Task Empty_ground_journal_after_pickup_does_not_need_a_live_entity()
    {
        var kit = new NetTestKit();
        var (session, player) = await kit.EnterAsync();
        int groundId = kit.World.PlaceGroundForTest(player, Adena, 5);
        Assert.Null(await kit.World.TryPickupAsync(session, groundId, default));
        Assert.True(kit.World.LedgerForTests.HoldingsOf(LedgerAccount.Ground(groundId)).IsEmpty);
    }
}