using Fineage.Game.Skills;
using Fineage.Protocol;

namespace Fineage.Zone.Skills;

internal static class SkillMessages
{
    public static SkillListMsg List(IReadOnlyList<KnownSkill> skills, IReadOnlyDictionary<int, long> reuseUntilMs, long nowMs)
    {
        var rows = new SkillListEntryMsg[skills.Count];
        for (int i = 0; i < skills.Count; i++)
        {
            var skill = skills[i];
            reuseUntilMs.TryGetValue(skill.SkillId, out long until);
            int remaining = until > nowMs ? (int)Math.Min(int.MaxValue, until - nowMs) : 0;
            rows[i] = new SkillListEntryMsg
            {
                SkillId = skill.SkillId,
                Level = skill.Level,
                ReuseDelayMs = remaining
            };
        }

        return new SkillListMsg { Skills = rows };
    }
}
