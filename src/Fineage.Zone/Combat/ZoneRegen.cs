namespace Fineage.Zone.Combat;

/// <summary>
/// Mobius <c>Formulas.calcHpRegen</c> / <c>calcMpRegen</c> (<c>Formulas.java:245</c>, <c>:342</c>) for a player.
/// </summary>
/// <remarks>
/// <para>
/// <c>init</c> is the class template's per-level <c>hpRegen</c> / <c>mpRegen</c> row. Every Interlude class shares one
/// table (all 89 templates in the Mobius datapack carry the same 80 rows), so it lives here once instead of widening
/// <c>classes.json</c> by 89 × 160 redundant numbers.
/// </para>
/// <para>
/// Mobius then scales it, in this order:
/// <list type="number">
/// <item><c>init *= getLevelMod() * BaseStat.CON.calcBonus()</c> for HP, MEN for MP. Level mod is <c>(level + 89) / 100</c>,
/// the CON/MEN bonus is the same stat-bonus table the maxima use.</item>
/// <item>a movement multiplier: sitting ×1.5, standing still ×1.1, running ×0.7. Mobius checks <c>isSitting</c> first,
/// then <c>!isMoving</c>, then <c>isRunning</c>, so a moving walker gets no multiplier at all.</item>
/// <item><c>calcStat(REGENERATE_HP_RATE, max(1, init))</c> — buffs, masteries, Weight Penalty 4270. That fold happens
/// in <c>ZoneWorldRegen</c> so it reuses the shared stat pipeline.</item>
/// </list>
/// </para>
/// <para>
/// Combat is <b>not</b> a multiplier and does not pause regeneration: Mobius only calls <c>stopHpMpRegeneration()</c> on
/// death and when a bar reaches its maximum (<c>CreatureStatus.setCurrentHp</c>, <c>PlayerStatus.reduceHp</c>).
/// </para>
/// </remarks>
public static class ZoneRegen
{
    /// <summary>Mobius <c>Formulas.getRegeneratePeriod</c> (<c>Formulas.java:96</c>).</summary>
    public const int DefaultTickMs = 3000;

    // Mobius datapack dist/game/data/stats/chars/baseStats/*.xml, <lvlUpgainData><level val="1..80">. Identical in
    // all 89 class templates; tools/import_class_templates.py verifies that and fails the import if it ever diverges.
    static readonly double[] BaseHpRegen =
    [
        2.0, 2.05, 2.1, 2.15, 2.2, 2.25, 2.3, 2.35, 2.4, 2.45,
        2.5, 2.6, 2.7, 2.8, 2.9, 3.0, 3.1, 3.2, 3.3, 3.4,
        3.5, 3.6, 3.7, 3.8, 3.9, 4.0, 4.1, 4.2, 4.3, 4.4,
        4.5, 4.6, 4.7, 4.8, 4.9, 5.0, 5.1, 5.2, 5.3, 5.4,
        5.5, 5.6, 5.7, 5.8, 5.9, 6.0, 6.1, 6.2, 6.3, 6.4,
        6.5, 6.6, 6.7, 6.8, 6.9, 7.0, 7.1, 7.2, 7.3, 7.4,
        7.5, 7.6, 7.7, 7.8, 7.9, 8.0, 8.1, 8.2, 8.3, 8.4,
        8.5, 8.6, 8.7, 8.8, 8.9, 9.0, 9.1, 9.2, 9.3, 9.4
    ];

    static readonly double[] BaseMpRegen =
    [
        0.9, 0.9, 0.9, 0.9, 0.9, 0.9, 0.9, 0.9, 0.9, 0.9,
        1.2, 1.2, 1.2, 1.2, 1.2, 1.2, 1.2, 1.2, 1.2, 1.2,
        1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5,
        1.8, 1.8, 1.8, 1.8, 1.8, 1.8, 1.8, 1.8, 1.8, 1.8,
        2.1, 2.1, 2.1, 2.1, 2.1, 2.1, 2.1, 2.1, 2.1, 2.1,
        2.4, 2.4, 2.4, 2.4, 2.4, 2.4, 2.4, 2.4, 2.4, 2.4,
        2.7, 2.7, 2.7, 2.7, 2.7, 2.7, 2.7, 2.7, 2.7, 2.7,
        3.0, 3.0, 3.0, 3.0, 3.0, 3.0, 3.0, 3.0, 3.0, 3.0
    ];

    /// <summary>How the creature is postured this tick. Mirrors the Mobius <c>isSitting</c> / <c>isMoving</c> / <c>isRunning</c> order.</summary>
    public enum Posture
    {
        /// <summary>Moving and not running: Mobius applies no multiplier.</summary>
        Walking,
        /// <summary>Standing still: ×1.1.</summary>
        Standing,
        /// <summary>Running: ×0.7.</summary>
        Running,
        /// <summary>Sitting: ×1.5.</summary>
        Sitting
    }

    /// <summary>Mobius <c>Creature.getLevelMod()</c>: <c>(level + 89) / 100</c>.</summary>
    public static double LevelMod(int level) => (Math.Max(1, level) + 89) / 100d;

    public static double PostureMultiplier(Posture posture) => posture switch
    {
        Posture.Sitting => 1.5,
        Posture.Standing => 1.1,
        Posture.Running => 0.7,
        _ => 1.0
    };

    /// <summary>Base <c>hpRegen</c> row for the level, before the level mod, CON bonus and posture.</summary>
    public static double BaseHpRate(int level) => BaseHpRegen[Index(level)];

    /// <summary>Base <c>mpRegen</c> row for the level, before the level mod, MEN bonus and posture.</summary>
    public static double BaseMpRate(int level) => BaseMpRegen[Index(level)];

    /// <summary>
    /// Mobius <c>calcHpRegen</c> init term: template row × level mod × CON bonus, then the posture multiplier, and
    /// <c>max(1, …)</c>. The caller folds <c>REGENERATE_HP_RATE</c> on top.
    /// </summary>
    public static double HpPerTick(int level, double conBonus, Posture posture)
    {
        double init = BaseHpRate(level) * LevelMod(level) * conBonus;
        return Math.Max(1d, init) * PostureMultiplier(posture);
    }

    /// <summary>Mobius <c>calcMpRegen</c> init term: template row × level mod × MEN bonus, posture, <c>max(1, …)</c>.</summary>
    public static double MpPerTick(int level, double menBonus, Posture posture)
    {
        double init = BaseMpRate(level) * LevelMod(level) * menBonus;
        return Math.Max(1d, init) * PostureMultiplier(posture);
    }

    static int Index(int level) => Math.Clamp(level, 1, BaseHpRegen.Length) - 1;
}
