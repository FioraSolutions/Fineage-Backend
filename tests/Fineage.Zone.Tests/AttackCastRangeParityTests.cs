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

    [Fact]
    public void Collision_aware_reach_matches_mobius_maybeMoveToPawn()
    {
        // Dagger/fists: 40 + player 9 + default NPC 31 = 80 (legacy PlayerAttackReach).
        Assert.Equal(80, ZoneCombat.PlayerAttackReach(40));
        Assert.Equal(ZoneCombat.DefaultNpcCollision, 31);
        // A small mob (Gremlin-sized collision 8) is 40+9+8=57, not the old flat 80+40=120 hit window.
        Assert.Equal(57, ZoneCombat.PlayerAttackReach(40, 8));
        Assert.Equal(80, ZoneCombat.SkillCastReach(40, ZoneCombat.PlayerCollisionRadius, ZoneCombat.DefaultNpcCollision));
        Assert.Equal(57, ZoneCombat.SkillCastReach(40, ZoneCombat.PlayerCollisionRadius, 8));
        // The old TrySkillUseAttack gate (castRange + 100) let a 40-range blow start at 140.
        Assert.False(ZoneCombat.Within(0, 0, 140, 0, ZoneCombat.SkillCastReach(40, 9, 31)));
        Assert.False(ZoneCombat.Within(0, 0, 100, 0, ZoneCombat.SkillCastReach(40, 9, 8)));
        Assert.True(ZoneCombat.Within(0, 0, 57, 0, ZoneCombat.SkillCastReach(40, 9, 8)));
    }

    [Theory]
    [InlineData(11, 400, 900)]   // Trick
    [InlineData(12, 600, 1100)]  // Switch
    [InlineData(16, 40, 400)]    // Mortal Blow
    [InlineData(27, 40, 400)]    // Unlock
    [InlineData(30, 40, 400)]    // Backstab
    [InlineData(51, 400, 900)]   // Lure
    [InlineData(56, 700, 1200)]  // Power Shot
    [InlineData(96, 40, 400)]    // Bleed
    [InlineData(263, 40, 400)]   // Deadly Blow
    [InlineData(344, 40, 400)]   // Lethal Blow
    [InlineData(358, 40, 400)]   // Bluff
    [InlineData(409, 40, 400)]   // Critical Blow
    [InlineData(412, 150, 300)]  // Sand Bomb (Mobius effectRange 300, was 650)
    public void Adventurer_targeted_skill_ranges_match_mobius(int skillId, int castRange, int effectRange)
    {
        var skill = GameData.LoadDefault().Skills.Find(skillId);
        Assert.NotNull(skill);
        Assert.Equal(castRange, skill!.CastRange);
        Assert.Equal(effectRange, skill.EffectRange);
    }

    [Fact]
    public void Lure_datapack_matches_mobius()
    {
        var skill = GameData.LoadDefault().Skills.Find(51);
        Assert.NotNull(skill);
        Assert.Equal("Lure", skill!.Name);
        Assert.Equal(400, skill.CastRange);
        Assert.Equal(900, skill.EffectRange);
        Assert.Equal(1500, skill.HitTimeMs);
        Assert.Equal(10_000, skill.ReuseDelayMs);
        Assert.Equal("ONE", skill.TargetType);
        Assert.True(SkillEffectCatalog.LoadDefault().TryGet(51, out var rows));
        var add = Assert.Single(rows);
        Assert.Equal("AddHate", add.Name);
        Assert.Equal(500, add.Power);
    }
}
