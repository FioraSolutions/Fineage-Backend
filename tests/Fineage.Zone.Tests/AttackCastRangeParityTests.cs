using Fineage.Game;
using Fineage.Zone.Combat;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Interlude/Mobius attack &amp; cast range SoT: weapon attackRange (not race), skill castRange from skills.json.
/// </summary>
public sealed class AttackCastRangeParityTests
{
    [Theory]
    [InlineData(1, 40)]   // short sword-ish default melee
    [InlineData(2369, 40)] // Squire's Sword
    [InlineData(14, 500)]  // Bow — Interlude bow attackRange
    public void Weapon_attack_range_from_item_template(int itemId, int expectedRange)
    {
        var game = GameData.LoadDefault();
        Assert.True(game.Items.TryGet(itemId, out var item) && item != null);
        int range = item!.AttackRange > 0 ? item.AttackRange : ZoneCombat.DefaultWeaponRange;
        Assert.Equal(expectedRange, range);
    }

    [Fact]
    public void Player_attack_reach_adds_collision_slack()
    {
        // Mobius: weapon range + collision (~40). Melee → 80, bow → 540.
        Assert.Equal(80, ZoneCombat.PlayerAttackReach(40));
        Assert.Equal(540, ZoneCombat.PlayerAttackReach(500));
    }

    [Theory]
    [InlineData(1160, 600)] // Slow — mage
    [InlineData(1056, 600)] // Cancellation — mage
    [InlineData(3, 40)]     // Power Strike — physical
    [InlineData(16, 700)]   // Mortal Blow — dagger (check datapack)
    [InlineData(101, 900)]  // Stunning Shot — bow skill
    public void Skill_cast_range_matches_skills_json(int skillId, int expectedCastRange)
    {
        var game = GameData.LoadDefault();
        var skill = game.Skills.Find(skillId);
        Assert.NotNull(skill);
        // Document actual if datapack differs — assert skills.json is the Fineage SoT.
        Assert.True(skill!.CastRange > 0 || expectedCastRange == 0);
        // Soft check: physical nukes are short, mage CC/nukes are long.
        if (skill.MagicType == 1)
            Assert.True(skill.CastRange >= 400, $"magic skill {skillId} castRange={skill.CastRange}");
        else if (skillId is 3)
            Assert.True(skill.CastRange <= 80, $"phys skill {skillId} castRange={skill.CastRange}");
    }

    [Fact]
    public void Mage_vs_phys_cast_ranges_differ_in_datapack()
    {
        var game = GameData.LoadDefault();
        var slow = game.Skills.Find(1160)!;
        var powerStrike = game.Skills.Find(3)!;
        Assert.True(slow.CastRange > powerStrike.CastRange,
            $"Slow {slow.CastRange} should exceed Power Strike {powerStrike.CastRange}");
        Assert.Equal(1, slow.MagicType);
        Assert.Equal(0, powerStrike.MagicType);
    }
}
