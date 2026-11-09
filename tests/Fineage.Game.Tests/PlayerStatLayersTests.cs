using Fineage.Game;
using Xunit;

namespace Fineage.Game.Tests;

public sealed class PlayerStatLayersTests
{
    [Fact]
    public void Effective_attributes_add_henna_before_bonus()
    {
        var catalog = GameData.LoadDefault().Classes!;
        Assert.True(catalog.TryGet(0, out var fighter) && fighter != null);
        var attrs = PlayerEffectiveAttributes.From(
            fighter, new HennaStatBonus(Str: 1, Con: -3, Dex: 0, Int: 0, Men: 0, Wit: 0), catalog.StatBonuses);
        Assert.Equal(fighter.Str + 1, attrs.Str);
        Assert.Equal(fighter.Con - 3, attrs.Con);
        Assert.Equal(catalog.StatBonuses.Bonus(StatSlot.Str, fighter.Str + 1), attrs.StrBonus);
    }

    [Fact]
    public void LevelMod_matches_mobius()
    {
        Assert.Equal(0.90d, PlayerGearBases.LevelMod(1), 5);
        Assert.Equal(1.09d, PlayerGearBases.LevelMod(20), 5);
    }
}
