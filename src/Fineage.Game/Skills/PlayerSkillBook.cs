using Fineage.Game;

namespace Fineage.Game.Skills;

/// <summary>
/// Resolves which skills a character knows from Mobius auto-get trees and expertise.
/// NPC-learned skills are not granted until a learn path exists.
/// </summary>
public static class PlayerSkillBook
{
    public static IReadOnlyList<KnownSkill> AutoSkills(
        SkillTreeCatalog trees,
        SkillCatalog skills,
        int classId,
        int level,
        ClassCatalog? classes = null)
    {
        ArgumentNullException.ThrowIfNull(trees);
        ArgumentNullException.ThrowIfNull(skills);
        if (level < 1)
            level = 1;

        var best = new Dictionary<int, int>();
        IReadOnlyList<int> chain = classes != null && classes.TryGet(classId, out _)
            ? classes.ParentChain(classId)
            : [classId];
        foreach (int chainClassId in chain)
        {
            var tree = trees.FindClass(chainClassId);
            if (tree == null)
                continue;
            foreach (var entry in tree.Skills)
            {
                if (entry.GetLevel > level)
                    continue;
                // Mobius autoGet on level-up. NPC-tree rows are included at level until
                // RequestAcquireSkill / trainer wire lands (Backend #137 follow-up).
                if (!entry.AutoGet && !entry.LearnedByNpc)
                    continue;
                if (best.TryGetValue(entry.SkillId, out int current))
                    best[entry.SkillId] = Math.Max(current, entry.Level);
                else
                    best[entry.SkillId] = entry.Level;
            }
        }

        int expertiseLevel = Expertise.ForLevel(level);
        if (expertiseLevel > 0 && skills.Find(Expertise.SkillId) != null)
            best[Expertise.SkillId] = expertiseLevel;

        var rows = new List<KnownSkill>(best.Count);
        foreach (var pair in best.OrderBy(p => p.Key))
        {
            if (skills.Find(pair.Key) == null)
                continue;
            rows.Add(new KnownSkill(pair.Key, pair.Value));
        }

        return rows;
    }
}

public readonly record struct KnownSkill(int SkillId, int Level);
