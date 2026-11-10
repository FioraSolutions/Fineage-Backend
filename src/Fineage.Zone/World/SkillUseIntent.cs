namespace Fineage.Zone.World;

/// <summary>
/// Zone-local skill-use command. Replaces Protocol <c>SkillUseRequestMsg</c> inside World so
/// Net and tests pass primitives / this intent instead of MessagePack-era DTOs.
/// <paramref name="CtrlPressed"/> is Mobius <c>forceUse</c> (ctrl held on RequestMagicSkillUse).
/// </summary>
readonly record struct SkillUseIntent(int SkillId, int TargetObjectId, bool CtrlPressed = false);
