using Fineage.Game.Skills;
using Fineage.Protocol;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius <c>Creature.checkDoCastConditions</c> / <c>useMagic</c> item gates and force-charge spend at real cast start.
/// </summary>
public sealed partial class ZoneWorld
{
    int ChargeCostOf(SkillDefinition template) => SkillCastConditionCharges.Required(template.CastCondition);

    void SpendChargesAtCastStart(ZoneEntity player, int amount)
    {
        if (amount > 0)
            SpendForceCharges(player, amount);
    }

    PendingSkillHit ArmCastResources(
        ZoneEntity player,
        PendingSkillHit hit,
        SkillDefinition template,
        SkillLevelData levelData)
    {
        int charge = ChargeCostOf(template);
        if (charge > 0)
            SpendChargesAtCastStart(player, charge);
        return hit with
        {
            ChargeSpend = charge,
            ItemConsumeId = levelData.ItemConsumeId,
            ItemConsumeCount = levelData.ItemConsumeCount
        };
    }

    /// <summary>Mobius launch: destroy consumables before the effect runs. Returns false when the bag is short.</summary>
    bool LaunchConsumeItems(ZoneSession? session, ZoneEntity player, PendingSkillHit hit)
    {
        if (hit.ItemConsumeId <= 0 || hit.ItemConsumeCount <= 0)
            return true;
        if (session == null)
            return (player.Inventory?.CountOf(hit.ItemConsumeId) ?? 0) >= hit.ItemConsumeCount;
        var levelData = new SkillLevelData(
            hit.Level, 0, 0, 0, 0, itemConsumeId: hit.ItemConsumeId, itemConsumeCount: hit.ItemConsumeCount);
        return ConsumeSkillItems(session, player, levelData);
    }
}