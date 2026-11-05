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
        ClassCatalog? classes = null,
        bool includeNpcSkills = true)
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
                if (!entry.AutoGet && !(entry.LearnedByNpc && includeNpcSkills))
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

/// <summary>One row a trainer can teach now (Mobius <c>SkillTreeData.getAvailableSkills</c>: next level only, getLevel reached).</summary>
public readonly record struct LearnableSkill(
    int SkillId,
    int Level,
    int GetLevel,
    int LevelUpSp,
    IReadOnlyList<SkillLearnItem>? RequiredItems = null,
    IReadOnlyList<SkillLearnPrerequisite>? PreReqSkills = null)
{
    public IReadOnlyList<SkillLearnItem> Items => RequiredItems ?? [];

    public IReadOnlyList<SkillLearnPrerequisite> Prerequisites => PreReqSkills ?? [];
}

/// <summary>Mobius <c>RequestAcquireSkill.checkPlayerSkill</c> outcome; the zone turns it into SysMsg 276/278.</summary>
public enum LearnBlock
{
    None = 0,
    /// <summary>Mobius <c>YOU_DO_NOT_MEET_THE_SKILL_LEVEL_REQUIREMENTS</c> (a forged request below getLevel).</summary>
    Level = 1,
    /// <summary>SystemMessage 278: the SP price is above the player's SP.</summary>
    Sp = 2,
    /// <summary>SystemMessage 276: a prerequisite skill is missing or too low.</summary>
    Prerequisite = 3,
    /// <summary>SystemMessage 276: required items are missing from the bag.</summary>
    Items = 4
}

public static class TrainerSkills
{
    /// <summary>Next-level NPC-learned rows for the class chain whose getLevel &lt;= level and whose previous level is known.</summary>
    public static IReadOnlyList<LearnableSkill> Available(
        SkillTreeCatalog trees,
        SkillCatalog skills,
        int classId,
        int level,
        IReadOnlyDictionary<int, int> known,
        ClassCatalog? classes = null)
    {
        var best = new Dictionary<int, LearnableSkill>();
        IReadOnlyList<int> chain = classes != null && classes.TryGet(classId, out _) ? classes.ParentChain(classId) : [classId];
        foreach (int chainClassId in chain)
        {
            var tree = trees.FindClass(chainClassId);
            if (tree == null)
                continue;
            foreach (var e in tree.Skills)
            {
                if (!e.LearnedByNpc || e.GetLevel > level || skills.Find(e.SkillId) == null)
                    continue;
                int have = known.TryGetValue(e.SkillId, out int k) ? k : 0;
                if (e.Level != have + 1)
                    continue;
                if (!best.ContainsKey(e.SkillId))
                    best[e.SkillId] = new LearnableSkill(e.SkillId, e.Level, e.GetLevel, e.LevelUpSp,
                        e.RequiredItems, e.PreReqSkills);
            }
        }

        return best.Values.OrderBy(s => s.SkillId).ToList();
    }

    /// <summary>
    /// Mobius <c>RequestAcquireSkill.checkPlayerSkill</c> in wire order: level, SP, prerequisites, then items.
    /// <paramref name="countOf"/> is the bag count for an item id (the zone passes the live inventory).
    /// </summary>
    public static LearnBlock Check(
        LearnableSkill row,
        int playerLevel,
        int playerSp,
        IReadOnlyDictionary<int, int> known,
        Func<int, int> countOf)
    {
        ArgumentNullException.ThrowIfNull(known);
        ArgumentNullException.ThrowIfNull(countOf);
        if (row.GetLevel > playerLevel)
            return LearnBlock.Level;
        if (row.LevelUpSp > 0 && row.LevelUpSp > playerSp)
            return LearnBlock.Sp;
        foreach (var prereq in row.Prerequisites)
        {
            int have = known.TryGetValue(prereq.SkillId, out int level) ? level : 0;
            if (have < prereq.Level)
                return LearnBlock.Prerequisite;
        }

        foreach (var item in row.Items)
        {
            if (countOf(item.ItemId) < item.Count)
                return LearnBlock.Items;
        }

        return LearnBlock.None;
    }

    /// <summary>NPC-learned rows still above the character level (shown as "coming at level N").</summary>
    public static int NextLearnLevel(SkillTreeCatalog trees, int classId, int level, ClassCatalog? classes = null)
    {
        int next = 0;
        IReadOnlyList<int> chain = classes != null && classes.TryGet(classId, out _) ? classes.ParentChain(classId) : [classId];
        foreach (int chainClassId in chain)
        {
            var tree = trees.FindClass(chainClassId);
            if (tree == null)
                continue;
            foreach (var e in tree.Skills)
            {
                if (e.LearnedByNpc && e.GetLevel > level && (next == 0 || e.GetLevel < next))
                    next = e.GetLevel;
            }
        }

        return next;
    }
}
