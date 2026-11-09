using Fineage.Protocol;

namespace Fineage.Zone.Skills;

/// <summary>
/// Pure Mobius skill-use prechecks (dead / mute / known / passive / reuse / MP).
/// World owns templates, casting queue, and combat path selection under the gate.
/// </summary>
public static class SkillUseRules
{
    public static string? PrecheckError(
        bool dead,
        bool actionBlockedOrSilenced,
        int skillId,
        bool skillKnown,
        bool templateFound,
        bool passive,
        bool levelDataOk,
        bool onReuse,
        int curMp,
        int mpConsume)
    {
        if (dead)
            return SkillProtocol.Dead;
        if (actionBlockedOrSilenced)
            return SkillProtocol.Muted;
        if (skillId <= 0)
            return "bad_request";
        if (!skillKnown || !templateFound || !levelDataOk)
            return SkillProtocol.Unknown;
        if (passive)
            return SkillProtocol.Passive;
        if (onReuse)
            return SkillProtocol.Reuse;
        if (curMp < mpConsume)
            return SkillProtocol.Mp;
        return null;
    }
}
