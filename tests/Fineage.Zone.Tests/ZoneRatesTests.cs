using Fineage.Zone;
using Fineage.Zone.Drops;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZoneRatesTests
{
    [Fact]
    public void Retail_is_one_times()
    {
        var rates = ZoneRates.Retail;
        Assert.Equal(1, rates.RateXp);
        Assert.Equal(1, rates.RateSp);
        Assert.Equal(1, rates.RateAdena);
        Assert.Equal(1, rates.DeathDropChanceMultiplier);
        Assert.Equal(1, rates.SpoilDropChanceMultiplier);
    }

    [Fact]
    public void AsDropRates_maps_spoil_and_adena()
    {
        var rates = new ZoneRates
        {
            RateAdena = 15,
            DeathDropChanceMultiplier = 20,
            DeathDropAmountMultiplier = 2,
            SpoilDropChanceMultiplier = 10,
            SpoilDropAmountMultiplier = 3,
        };
        DropRates drop = rates.AsDropRates();
        Assert.Equal(20, drop.DeathDropChanceMultiplier);
        Assert.Equal(2, drop.DeathDropAmountMultiplier);
        Assert.Equal(10, drop.CorpseDropChanceMultiplier);
        Assert.Equal(3, drop.CorpseDropAmountMultiplier);
        Assert.Equal(15, drop.AdenaAmountMultiplier);
    }

    [Fact]
    public void DropRoller_applies_adena_amount_on_death()
    {
        var definition = new DropNpcDefinition
        {
            NpcId = 1,
            Death = new DropDeathDefinition
            {
                Groups =
                [
                    new DropGroupDefinition
                    {
                        Chance = 100,
                        Items =
                        [
                            new DropItemDefinition
                            {
                                ItemId = ZoneRates.AdenaItemId,
                                Min = 10,
                                Max = 10,
                                Chance = 100,
                            }
                        ]
                    }
                ]
            },
            Corpse = null,
        };

        var rates = new DropRates
        {
            DeathDropChanceMultiplier = 1,
            DeathDropAmountMultiplier = 1,
            AdenaAmountMultiplier = 5,
        };
        var stacks = DropRoller.RollDeath(definition, new Random(1), rates);
        Assert.Single(stacks);
        Assert.Equal(ZoneRates.AdenaItemId, stacks[0].ItemId);
        Assert.Equal(50, stacks[0].Count);
    }
}
