using Fineage.Game.Skills;

namespace Fineage.Zone.Skills;

public static class SkillEnchantService
{
    public const int SpellbookItemId = SkillEnchantCatalog.SpellbookItemId;

    public static bool RequiresSpellbook(int targetLevel) => targetLevel is 101 or 141;

    /// <summary>True when the player may attempt this enchant target (route exists and prerequisites met).</summary>
    public static bool TryResolveRoute(
        SkillEnchantCatalog catalog,
        IReadOnlyDictionary<int, int> knownSkills,
        int playerLevel,
        int classId,
        int skillId,
        int targetLevel,
        out SkillEnchantRoute route)
    {
        route = default!;
        if (!SkillEnchantRules.TrainerEnchantEnabled || !catalog.IsLoaded)
            return false;
        if (playerLevel < SkillEnchantCatalog.MinPlayerLevel || classId < SkillEnchantCatalog.MinClassId)
            return false;
        var row = catalog.Find(skillId, targetLevel);
        if (row == null)
            return false;
        if (!knownSkills.TryGetValue(skillId, out int have) || have >= targetLevel)
            return false;
        if (have != row.MinSkillLevel)
            return false;
        route = row;
        return true;
    }

    public static IReadOnlyList<SkillEnchantRoute> ListOffers(
        SkillEnchantCatalog catalog,
        IReadOnlyDictionary<int, int> knownSkills,
        int playerLevel,
        int classId)
    {
        if (!SkillEnchantRules.TrainerEnchantEnabled || !catalog.IsLoaded)
            return Array.Empty<SkillEnchantRoute>();
        if (playerLevel < SkillEnchantCatalog.MinPlayerLevel || classId < SkillEnchantCatalog.MinClassId)
            return Array.Empty<SkillEnchantRoute>();
        return catalog.Available(knownSkills, playerLevel);
    }
}