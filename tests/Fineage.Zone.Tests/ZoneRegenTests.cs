using Fineage.Login;
using Fineage.Zone.Combat;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius <c>Formulas.calcHpRegen</c> / <c>calcMpRegen</c> (<c>Formulas.java:245</c>, <c>:342</c>) for a player:
/// template row (one table shared by all 89 Interlude classes) × level mod × CON/MEN bonus × posture multiplier.
/// </summary>
public sealed class ZoneRegenFormulaTests
{
    // data/stat-bonuses.json: CON 43 (Human Fighter) is 1.58, MEN 25 is 1.28.
    const double ConBonus = 1.58;
    const double MenBonus = 1.28;

    [Fact]
    public void Level_one_standing_uses_the_template_row_and_level_mod()
    {
        // 2.0 HP row × (1 + 89)/100 = 0.9 level mod × 1.58 CON = 2.844 → ×1.1 standing = 3.1284.
        double hp = ZoneRegen.HpPerTick(level: 1, ConBonus, ZoneRegen.Posture.Standing);
        Assert.Equal(2.0 * 0.9 * ConBonus * 1.1, hp, 6);

        // 0.9 MP row × 0.9 × 1.28 MEN = 1.0368 → ×1.1 standing = 1.14048.
        double mp = ZoneRegen.MpPerTick(level: 1, MenBonus, ZoneRegen.Posture.Standing);
        Assert.Equal(0.9 * 0.9 * MenBonus * 1.1, mp, 6);
    }

    [Fact]
    public void Posture_multipliers_match_Mobius()
    {
        double standing = ZoneRegen.HpPerTick(level: 20, ConBonus, ZoneRegen.Posture.Standing);
        double sitting = ZoneRegen.HpPerTick(level: 20, ConBonus, ZoneRegen.Posture.Sitting);
        double running = ZoneRegen.HpPerTick(level: 20, ConBonus, ZoneRegen.Posture.Running);
        double walking = ZoneRegen.HpPerTick(level: 20, ConBonus, ZoneRegen.Posture.Walking);

        // Sitting ×1.5 (2.0), standing still ×1.1 (3.0), running ×0.7 (4.0), and a moving walker gets none.
        Assert.Equal(standing / 1.1 * 1.5, sitting, 6);
        Assert.Equal(standing / 1.1 * 0.7, running, 6);
        Assert.Equal(standing / 1.1, walking, 6);
    }

    [Fact]
    public void Level_mod_is_the_Mobius_linear_ramp()
    {
        Assert.Equal(0.90, ZoneRegen.LevelMod(1), 6);
        Assert.Equal(1.00, ZoneRegen.LevelMod(11), 6);
        Assert.Equal(1.69, ZoneRegen.LevelMod(80), 6);
    }

    [Fact]
    public void Base_rows_follow_the_datapack_plateaus()
    {
        // HP climbs 0.05 a level through 11, then 0.1.
        Assert.Equal(2.0, ZoneRegen.BaseHpRate(1));
        Assert.Equal(2.45, ZoneRegen.BaseHpRate(10));
        Assert.Equal(2.5, ZoneRegen.BaseHpRate(11));
        Assert.Equal(9.4, ZoneRegen.BaseHpRate(80));

        // MP steps every ten levels.
        Assert.Equal(0.9, ZoneRegen.BaseMpRate(10));
        Assert.Equal(1.2, ZoneRegen.BaseMpRate(11));
        Assert.Equal(1.5, ZoneRegen.BaseMpRate(21));
        Assert.Equal(3.0, ZoneRegen.BaseMpRate(80));
    }

    [Fact]
    public void A_rate_below_one_floors_at_one_before_the_posture_multiplier()
    {
        // Level 1 mystic MEN bonus can push the MP row under 1; Mobius takes max(1, init) first.
        double idle = ZoneRegen.MpPerTick(level: 1, menBonus: 0.5, ZoneRegen.Posture.Walking);
        Assert.Equal(1.0, idle, 6);
    }
}
