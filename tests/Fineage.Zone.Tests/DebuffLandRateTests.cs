using Fineage.Accounts;
using Fineage.Game;
using Fineage.Zone.Combat;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius calcEffectSuccess + jewelry DefenceTrait (Queen Ant poison/hold).</summary>
public sealed class DebuffLandRateTests
{
    const int RingOfQueenAnt = 6660;

    [Fact]
    public void Poison_defence_trait_lowers_general_resistance_bonus()
    {
        Assert.Equal(1d, ZoneCombat.GeneralResistanceBonus(null, null, "POISON"));
        var defence = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["poison"] = 1.30 // +30% Queen Ant
        };
        Assert.Equal(0.70, ZoneCombat.GeneralResistanceBonus(null, defence, "POISON"), 3);
        Assert.Equal(1d, ZoneCombat.GeneralResistanceBonus(null, defence, "HOLD"), 3); // missing → 1 until hold present
        defence["hold"] = 1.20;
        Assert.Equal(0.80, ZoneCombat.GeneralResistanceBonus(null, defence, "HOLD"), 3);
    }

    [Fact]
    public void Debuff_land_chance_scales_with_trait_mod()
    {
        double bare = ZoneCombat.DebuffLandChance(
            activateRate: 70, basicProperty: "CON", magicLevel: 20, lvlBonusRate: 1,
            targetLevel: 20, targetBaseStat: 21, traitMod: 1d);
        double resisted = ZoneCombat.DebuffLandChance(
            activateRate: 70, basicProperty: "CON", magicLevel: 20, lvlBonusRate: 1,
            targetLevel: 20, targetBaseStat: 21, traitMod: 0.70);
        Assert.True(bare > resisted, $"bare {bare} should exceed resisted {resisted}");
        Assert.True(resisted >= ZoneCombat.MinAbnormalSuccessRate);
    }

    [Fact]
    public void Debuff_land_chance_scales_with_skill_magic_level()
    {
        double low = ZoneCombat.DebuffLandChance(
            activateRate: 40, basicProperty: "MEN", magicLevel: 20, lvlBonusRate: 2,
            targetLevel: 40, targetBaseStat: 30, traitMod: 1d);
        double high = ZoneCombat.DebuffLandChance(
            activateRate: 40, basicProperty: "MEN", magicLevel: 60, lvlBonusRate: 2,
            targetLevel: 40, targetBaseStat: 30, traitMod: 1d);
        Assert.True(high > low, $"ml60 {high} should exceed ml20 {low}");
    }

    [Fact]
    public void Queen_ant_ring_feeds_poison_hold_into_player_defence_traits()
    {
        var game = GameData.LoadDefault();
        var bag = new PlayerInventory(game.Items);
        var player = new ZoneEntity
        {
            ObjectId = 1, Kind = ZoneEntityKind.Player, ClassId = 0, Level = 80, Inventory = bag
        };
        var world = new ZoneWorld(game: game);
        Assert.Null(world.PlayerDefenceTraits(player));

        bag.Equip(bag.Add(RingOfQueenAnt));
        var traits = world.PlayerDefenceTraits(player);
        Assert.NotNull(traits);
        Assert.Equal(1.30, traits!["poison"], 3);
        Assert.Equal(1.20, traits["hold"], 3);
    }
}
