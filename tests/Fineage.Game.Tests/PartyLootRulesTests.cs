using Fineage.Game.Services;
using Xunit;

namespace Fineage.Game.Tests;

public sealed class PartyLootRulesTests
{
    [Fact]
    public void FindersKeepers_always_sweeper()
    {
        int last = 0;
        int looter = PartyLootRules.ActualLooter(10, PartyDistributionType.FindersKeepers, spoil: true,
            [10, 20, 30], _ => true, ref last, new Random(1));
        Assert.Equal(10, looter);
    }

    [Fact]
    public void Random_does_not_redistribute_spoil()
    {
        int last = 0;
        int looter = PartyLootRules.ActualLooter(10, PartyDistributionType.Random, spoil: true,
            [10, 20, 30], _ => true, ref last, new Random(1));
        Assert.Equal(10, looter);
    }

    [Fact]
    public void Random_redistributes_ground_pickup()
    {
        int last = 0;
        var rnd = new Seq(1);
        int looter = PartyLootRules.ActualLooter(10, PartyDistributionType.Random, spoil: false,
            [10, 20, 30], _ => true, ref last, rnd);
        Assert.Equal(20, looter);
    }

    [Fact]
    public void RandomIncludingSpoil_picks_eligible_member()
    {
        int last = 0;
        // Scripted: Next(3) → 1 → member 20
        var rnd = new Seq(1);
        int looter = PartyLootRules.ActualLooter(10, PartyDistributionType.RandomIncludingSpoil, spoil: true,
            [10, 20, 30], _ => true, ref last, rnd);
        Assert.Equal(20, looter);
    }

    [Fact]
    public void ByTurnIncludingSpoil_advances_cursor()
    {
        int last = 0;
        int a = PartyLootRules.ActualLooter(10, PartyDistributionType.ByTurnIncludingSpoil, spoil: true,
            [10, 20, 30], _ => true, ref last, Random.Shared);
        Assert.Equal(20, a); // last was 0 → ++ → 1 → members[1]=20
        Assert.Equal(1, last);
        int b = PartyLootRules.ActualLooter(10, PartyDistributionType.ByTurnIncludingSpoil, spoil: true,
            [10, 20, 30], _ => true, ref last, Random.Shared);
        Assert.Equal(30, b);
        Assert.Equal(2, last);
        int c = PartyLootRules.ActualLooter(10, PartyDistributionType.ByTurnIncludingSpoil, spoil: true,
            [10, 20, 30], _ => true, ref last, Random.Shared);
        Assert.Equal(10, c);
        Assert.Equal(0, last);
    }

    [Fact]
    public void ByTurnIncludingSpoil_skips_ineligible()
    {
        int last = 0;
        // members[1]=20 ineligible → continue to 30
        int looter = PartyLootRules.ActualLooter(10, PartyDistributionType.ByTurnIncludingSpoil, spoil: true,
            [10, 20, 30], id => id != 20, ref last, Random.Shared);
        Assert.Equal(30, looter);
    }

    [Fact]
    public void AdenaShare_splits_evenly()
    {
        Assert.Equal(33, PartyLootRules.AdenaShare(100, 3));
        Assert.Equal(0, PartyLootRules.AdenaShare(2, 0));
    }

    sealed class Seq : Random
    {
        readonly Queue<int> _q;
        public Seq(params int[] values) => _q = new Queue<int>(values);
        public override int Next(int maxValue) => _q.Dequeue() % Math.Max(1, maxValue);
    }
}
