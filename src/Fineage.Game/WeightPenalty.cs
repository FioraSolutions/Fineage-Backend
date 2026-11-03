namespace Fineage.Game;

/// <summary>
/// Inventory overload (Mobius <c>Player.refreshOverloaded</c>, <c>Player.java:1970-2013</c>) and the load limit it is
/// measured against (<c>Creature.getMaxLoad</c>, <c>Creature.java:6278-6288</c>).
/// <para>
/// <c>weightproc = (load - bonusWeightPenalty) * 1000 / maxLoad</c> (Java integer division, truncating toward zero),
/// then the level by threshold: <c>&lt; 500 → 0</c>, <c>&lt; 666 → 1</c>, <c>&lt; 800 → 2</c>, <c>&lt; 1000 → 3</c>,
/// else <c>4</c>. Level 0 means no penalty; 1–4 are skill 4270's levels
/// (<c>data/stats/skills/04200-04299.xml:1329-1346</c>: <c>regHp</c>/<c>regMp</c> ×0.5 on every level,
/// <c>runSpd</c> ×1, ×0.5, ×0.5, ×0).
/// </para>
/// <para>
/// The bonus comes from <c>Stat.WEIGHT_PENALTY</c> (<c>Creature.java:6290-6297</c>) — armor sets add ~5760–5795 so a
/// heavy set starts its penalty later. The limit is <c>Stat.WEIGHT_LIMIT</c> over
/// <c>floor(CON bonus * 69000 * AltWeightLimit)</c>; both stats are folded by the ordinary passive/buff pipeline,
/// so <see cref="BaseMaxLoad"/> is only the pre-skill base.
/// </para>
/// </summary>
public static class WeightPenalty
{
    /// <summary>Skill id Mobius adds at levels 1–4 (<c>Player.java:2005</c>: <c>getSkill(4270, newWeightPenalty)</c>).</summary>
    public const int SkillId = 4270;

    /// <summary>Highest skill level (the skill XML declares <c>levels="4"</c>).</summary>
    public const int MaxLevel = 4;

    /// <summary>Mobius <c>AltWeightLimit</c> default (<c>Config.java:1654</c>, <c>Character.ini</c>).</summary>
    public const double DefaultAltWeightLimit = 1;

    /// <summary>Weight unit base in <c>getMaxLoad</c>: <c>floor(CON bonus * 69000 * AltWeightLimit)</c>.</summary>
    public static int BaseMaxLoad(double conBonus, double altWeightLimit = DefaultAltWeightLimit)
    {
        if (conBonus < 0)
            throw new ArgumentOutOfRangeException(nameof(conBonus));
        if (altWeightLimit < 0)
            throw new ArgumentOutOfRangeException(nameof(altWeightLimit));
        return (int)Math.Floor(conBonus * 69000 * altWeightLimit);
    }

    /// <summary>
    /// Mobius <c>getWeightPenalty()</c> level for <paramref name="load"/>. A non-positive <paramref name="maxLoad"/>
    /// skips the whole check there (<c>Player.java:1973</c>), which leaves the level at 0.
    /// </summary>
    public static int Level(long load, int maxLoad, long bonusWeightPenalty = 0)
    {
        if (maxLoad <= 0)
            return 0;

        long weightproc = (load - bonusWeightPenalty) * 1000 / maxLoad;
        if (weightproc < 500)
            return 0;
        if (weightproc < 666)
            return 1;
        if (weightproc < 800)
            return 2;
        if (weightproc < 1000)
            return 3;
        return MaxLevel;
    }

    /// <summary>Mobius <c>setOverloaded(getCurrentLoad() &gt; maxLoad)</c> (<c>Player.java:2004</c>).</summary>
    public static bool IsOverloaded(long load, int maxLoad) => maxLoad > 0 && load > maxLoad;

    /// <summary>
    /// <c>weightproc</c> as used by <see cref="Level"/> (0 when there is no limit). Exposed for diagnostics and tests;
    /// the game only needs the level.
    /// </summary>
    public static long WeightPercent(long load, int maxLoad, long bonusWeightPenalty = 0) =>
        maxLoad <= 0 ? 0 : (load - bonusWeightPenalty) * 1000 / maxLoad;
}
