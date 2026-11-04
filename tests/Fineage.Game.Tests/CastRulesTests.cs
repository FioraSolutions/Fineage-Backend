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
}
