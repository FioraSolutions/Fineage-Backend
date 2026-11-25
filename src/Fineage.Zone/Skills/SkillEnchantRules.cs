using Fineage.Zone.Parity;

namespace Fineage.Zone.Skills;

/// <summary>Skill enchant (+) trainer flow behind <c>FINEAGE_ALLOW_SKILL_ENCHANT</c>.</summary>
public static class SkillEnchantRules
{
    public static bool TrainerEnchantEnabled => ClientHeavyFeatures.SkillEnchantEnabled;

    public static bool CanEnchant(int skillId, int nextLevel) =>
        TrainerEnchantEnabled && skillId > 0 && nextLevel > 0;
}