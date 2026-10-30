namespace Fineage.Zone.Combat;

/// <summary>Mobius <c>Creature.onHitTimer</c>: melee damage lands at hitTime (~half the swing), not on swing start.</summary>
public sealed record PendingMeleeHit(int AttackerId, int TargetId, int Damage, long DueUnixMs);
