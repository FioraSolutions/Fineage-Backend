namespace Fineage.Zone.Combat;

/// <summary>Cast in flight. Mobius skills (<see cref="Combat"/> set) compute damage at hitTime (onMagicHitTimer).</summary>
public sealed record PendingSkillHit(int TargetId, int Damage, long DueUnixMs, SkillCombatData? Combat = null, int Level = 0);
