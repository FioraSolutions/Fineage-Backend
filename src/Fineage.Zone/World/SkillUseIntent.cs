namespace Fineage.Zone.World;

/// <summary>
/// Zone-local skill-use command. Replaces Protocol <c>SkillUseRequestMsg</c> inside World so
/// Net and tests pass primitives / this intent instead of MessagePack-era DTOs.
/// </summary>
readonly record struct SkillUseIntent(int SkillId, int TargetObjectId);
