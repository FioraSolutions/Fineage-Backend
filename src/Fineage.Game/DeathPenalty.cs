namespace Fineage.Game;

/// <summary>
/// Death Penalty debuff, Mobius skill <c>5076</c> (<c>SkillTable</c>, <c>Player._deathPenaltyBuffLevel</c>).
/// A death from a monster rolls <see cref="DeathRuleCatalog.DeathPenaltyChance"/>; on success
/// <c>Player.increaseDeathPenaltyBuffLevel</c> (<c>Player.java:11718-11730</c>) raises one level (capped at
/// <see cref="DeathRuleCatalog.MaxDeathPenaltyLevel"/>) and adds the skill.
/// <para>
/// Every level 1–15 scales <c>pAtk</c>, <c>pAtkSpd</c>, <c>mAtk</c>, <c>mAtkSpd</c> and <c>runSpd</c> down,
/// <c>pDef</c> and <c>mDef</c> down, and subtracts <c>accCombat</c> / <c>rEvas</c>; see
/// <c>data/skill-passives.json</c>. The zone folds it as a virtual passive source, so the level has to reach
/// <see cref="Fineage.Zone.World.ZoneEntity.DeathPenaltyLevel"/>.
/// </para>
/// </summary>
public static class DeathPenalty
{
    /// <summary>Mobius <c>DEATH_PENALTY</c> skill id.</summary>
    public const int SkillId = 5076;

    /// <summary>Highest level the skill table defines, and the clamp in <c>increaseDeathPenaltyBuffLevel</c>.</summary>
    public const int MaxLevel = DeathRuleCatalog.MaxDeathPenaltyLevel;
}
