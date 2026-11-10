using Fineage.Game;

namespace Fineage.Zone.Combat;

/// <summary>
/// Zone adapter over <see cref="SkillStatEffectCatalog"/>: maps <see cref="ZoneAbnormalEntry"/> and
/// fail-loud-logs missing skill-stat rows. Core types live in Fineage.Game.
/// </summary>
public static class SkillStatEffects
{
    public static CombatStatMods Collect(SkillStatEffectCatalog catalog, IEnumerable<ZoneAbnormalEntry> abnormals) =>
        catalog.Collect(
            abnormals.Select(a => new SkillStatSource(a.Id, a.SkillLevel, a.Value)),
            static id => ZoneLog.Warn($"skill-stat-effects missing skillId={id} — abnormal has no combat mods"));
}
