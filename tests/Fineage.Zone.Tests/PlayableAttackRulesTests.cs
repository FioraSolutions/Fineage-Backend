using Fineage.Zone.Combat;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius <c>PlayableAI</c>: blessing + karma + level gap, and cursed weapons vs level 20.</summary>
public sealed class PlayableAttackRulesTests
{
    [Fact]
    public void Blessing_blocks_a_higher_pk_and_the_reverse()
    {
        Assert.True(PlayableAttackRules.Refused(true, 40, 20, 1, 0, false, true, false, false, false));
        Assert.False(PlayableAttackRules.Refused(true, 29, 20, 1, 0, false, true, false, false, false));
        Assert.True(PlayableAttackRules.Refused(true, 20, 40, 0, 1, true, false, false, false, true));
        Assert.False(PlayableAttackRules.Refused(true, 40, 20, 0, 0, false, true, false, false, false));
        Assert.False(PlayableAttackRules.Refused(false, 40, 20, 1, 0, false, true, false, false, false));
    }

    [Fact]
    public void Cursed_weapon_blocks_level_20_and_under()
    {
        Assert.True(PlayableAttackRules.Refused(true, 20, 80, 0, 0, false, false,
            attackerCursed: false, targetCursed: true, badSkill: false));
        Assert.True(PlayableAttackRules.Refused(true, 80, 20, 0, 0, false, false, true, false, false));
        Assert.False(PlayableAttackRules.Refused(true, 21, 80, 0, 0, false, false, false, true, false));
        Assert.True(PlayableAttackRules.Refused(true, 40, 20, 0, 0, false, false, false, true, badSkill: true));
        Assert.False(PlayableAttackRules.Refused(true, 21, 21, 0, 0, false, false, false, true, badSkill: true));
    }
}
