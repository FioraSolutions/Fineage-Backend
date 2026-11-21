using Fineage.Game;

namespace Fineage.Game.Skills;

/// <summary>Mobius <c>SkillTreeData.getTransferSkillTree</c> / <c>getAvailableTransferSkills</c>.</summary>
public static class TransferSkills
{
    /// <summary>
    /// Transfer trees are keyed on the second-class healer id (16 Bishop, 30 Elven Elder, 43 Shillien Elder).
    /// Third-class saints use their parent's tree (Mobius <c>playerClass.level() == 3 → getParent()</c>).
    /// </summary>
    public static int ResolveTreeClassId(ClassCatalog? classes, int classId)
    {
        if (classes == null || !classes.TryGet(classId, out var template) || template == null)
            return classId;
        if (template.ClassLevel >= 3 && template.ParentClassId >= 0)
            return template.ParentClassId;
        return classId;
    }

    public static TransferSkillEntry? FindEntry(
        TransferSkillTreeCatalog trees,
        int treeClassId,
        int skillId,
        int level)
    {
        var tree = trees.FindClass(treeClassId);
        if (tree == null)
            return null;
        foreach (var row in tree.Skills)
        {
            if (row.SkillId == skillId && row.Level == level)
                return row;
        }

        return null;
    }

    /// <summary>Transfer skills the player does not know yet (Mobius: no previous-level requirement).</summary>
    public static IReadOnlyList<TransferSkillEntry> Available(
        TransferSkillTreeCatalog trees,
        SkillCatalog skills,
        ClassCatalog? classes,
        int classId,
        int level,
        IReadOnlyDictionary<int, int> known)
    {
        int treeClassId = ResolveTreeClassId(classes, classId);
        var tree = trees.FindClass(treeClassId);
        if (tree == null)
            return [];

        var list = new List<TransferSkillEntry>();
        foreach (var row in tree.Skills)
        {
            if (row.GetLevel > level || skills.Find(row.SkillId) == null)
                continue;
            if (known.ContainsKey(row.SkillId))
                continue;
            list.Add(row);
        }

        return list;
    }

    public static TransferSkillEntry? NextForCertificate(
        TransferSkillTreeCatalog trees,
        SkillCatalog skills,
        ClassCatalog? classes,
        int classId,
        int playerLevel,
        int certificateItemId,
        IReadOnlyDictionary<int, int> known)
    {
        if (!trees.TryCertificateClass(certificateItemId, out int certClass))
            return null;
        int treeClassId = ResolveTreeClassId(classes, classId);
        if (treeClassId != certClass)
            return null;
        foreach (var row in Available(trees, skills, classes, classId, playerLevel, known))
        {
            if (row.CertificateItemId == certificateItemId)
                return row;
        }

        return null;
    }
}