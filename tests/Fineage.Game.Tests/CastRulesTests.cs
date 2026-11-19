using Fineage.Game;
using Fineage.Game.Combat;
using Xunit;

namespace Fineage.Game.Tests;

public sealed class CastRulesTests
{
    [Fact]
    public void Attack_break_rate_follows_mobius_calcAtkBreak()
    {
        Assert.Equal(15 + Math.Sqrt(13 * 100), CastRules.AttackBreakRate(100, 1.0), 6);
        Assert.Equal(15 + Math.Sqrt(13 * 100) - 20, CastRules.AttackBreakRate(100, 1.2), 6); // high MEN resists
        Assert.Equal(99, CastRules.AttackBreakRate(100_000, 1.0));
        Assert.Equal(1, CastRules.AttackBreakRate(1, 2.0));
        Assert.False(CastRules.RollAttackBreak(new Random(1), 0, 1.0));
        Assert.True(CastRules.Interruptible(magic: true));
        Assert.False(CastRules.Interruptible(magic: false));
        Assert.Equal(1100, CastRules.EscapeRange(600, 1100));
        Assert.Equal(600, CastRules.EscapeRange(600, 0));
    }

    [Fact]
    public void Attack_break_rate_applies_cancel_funcs_before_the_clamp()
    {
        // Concentration: <sub stat="cancel">. Mobius folds ATTACK_CANCEL onto init, then clamps 1..99.
        var mods = new CombatStatMods();
        mods.Add(new SkillStatFunc(CombatStat.Cancel, 's', [18]), 1);
        double raw = CastRules.AttackBreakRate(100, 1.0);
        double lowered = CastRules.AttackBreakRate(100, 1.0, init => mods.Apply(CombatStat.Cancel, init));
        Assert.Equal(raw - 18, lowered, 6);
    }
}
