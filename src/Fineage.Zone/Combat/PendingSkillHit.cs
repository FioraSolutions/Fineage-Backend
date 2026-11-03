namespace Fineage.Zone.Combat;

/// <summary>
/// Cast in flight. Mobius attack skills (<see cref="Combat"/> set) compute damage at hitTime (onMagicHitTimer);
/// non-damage skills carry <see cref="Apply"/> (buff / debuff / heal / spoil effect) run at hitTime after the launch checks.
/// </summary>
public sealed record PendingSkillHit(int TargetId, int Damage, long DueUnixMs, SkillCombatData? Combat = null, int Level = 0)
{
    public int SkillId { get; init; }
    /// <summary>Mobius isMagic of the skill (0 physical, 1 magic, 2 static) for non-combat casts.</summary>
    public int MagicType { get; init; }
    /// <summary>Escape range checked at launch (≤ 0 = none, e.g. self).</summary>
    public int EscapeRange { get; init; }
    /// <summary>Target must be a live NPC (bad skills) or a live player (friendly skills).</summary>
    public bool TargetIsNpc { get; init; }
    /// <summary>ZoneWorld-owned effect delegate (target, now, outbound) run at hitTime.</summary>
    public Delegate? Apply { get; init; }
}
