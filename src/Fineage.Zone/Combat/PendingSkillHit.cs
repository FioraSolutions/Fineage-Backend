namespace Fineage.Zone.Combat;

public sealed record PendingSkillHit(int TargetId, int Damage, long DueUnixMs);
