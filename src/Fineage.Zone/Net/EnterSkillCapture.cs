using Fineage.Game;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

/// <summary>Skill-book fields copied on the simulation thread; row merge runs during enter encode.</summary>
sealed class EnterSkillCapture
{
    public required IReadOnlyDictionary<int, int> KnownSkills { get; init; }
    public required IReadOnlyDictionary<int, int> ClanSkills { get; init; }
    public required IReadOnlyDictionary<int, int> GearSkills { get; init; }
    public required IReadOnlyDictionary<int, int> CursedSkills { get; init; }
    public required IReadOnlyDictionary<int, long> SkillReuseUntilMs { get; init; }
    public long NowUnixMs { get; init; }

    public static EnterSkillCapture From(ZoneEntity entity, long nowUnixMs) =>
        new()
        {
            KnownSkills = new Dictionary<int, int>(entity.KnownSkills),
            ClanSkills = new Dictionary<int, int>(entity.ClanSkills),
            GearSkills = new Dictionary<int, int>(entity.GearSkills),
            CursedSkills = new Dictionary<int, int>(entity.CursedSkills),
            SkillReuseUntilMs = new Dictionary<int, long>(entity.SkillReuseUntilMs),
            NowUnixMs = nowUnixMs
        };

    public SkillListMsg ToSkillListMsg()
    {
        var rows = new List<SkillListEntryMsg>();
        foreach (var pair in PlayerPassiveSources.EnumerateOrdered(KnownSkills, ClanSkills, GearSkills, CursedSkills))
        {
            int reuse = 0;
            if (SkillReuseUntilMs.TryGetValue(pair.Key, out long until) && until > NowUnixMs)
                reuse = (int)Math.Min(int.MaxValue, until - NowUnixMs);
            rows.Add(new SkillListEntryMsg
            {
                SkillId = pair.Key,
                Level = pair.Value,
                ReuseDelayMs = reuse
            });
        }

        return new SkillListMsg { Skills = rows.ToArray() };
    }
}