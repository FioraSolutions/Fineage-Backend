using Fineage.Game;

namespace Fineage.Zone.Combat;

/// <summary>
/// Thin bridge from live <c>ZoneAbnormalEntry</c> rows to <see cref="SkillStatEffectCatalog"/>.
/// </summary>
public static class SkillStatEffects
{
    /// <summary>
    /// Folds the owner's abnormals for one attack. <paramref name="position"/> is where the owner stands relative to the
    /// current target, which is what Mobius <c>calcStat(stat, …, target, skill)</c> resolves
    /// <c>&lt;player front|behind&gt;</c> against (Focus Chance 356 / Focus Power 357).
    /// </summary>
    public static CombatStatMods Collect(
        SkillStatEffectCatalog catalog,
        IEnumerable<ZoneAbnormalEntry> abnormals,
        SkillPosition position = SkillPosition.None,
        Func<string[], bool>? wearing = null) =>
        catalog.Collect(
            abnormals.Select(a => new SkillStatSource(a.Id, a.SkillLevel, a.Value)),
            static id => ZoneLog.Warn($"skill-stat-effects missing skillId={id} — abnormal has no combat mods"),
            position,
            wearing);

    /// <summary>Only the <c>&lt;player front|behind&gt;</c> funcs, for the side a swing is rolled from.</summary>
    public static CombatStatMods CollectPositional(
        SkillStatEffectCatalog catalog,
        IEnumerable<ZoneAbnormalEntry> abnormals,
        SkillPosition position,
        Func<string[], bool>? wearing = null) =>
        position == SkillPosition.None
            ? CombatStatMods.None
            : catalog.CollectPositional(
                abnormals.Select(a => new SkillStatSource(a.Id, a.SkillLevel, a.Value)),
                position,
                wearing);
}
