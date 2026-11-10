using Fineage.Game;
using Fineage.Game.Services;
using Fineage.Game.Skills;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Inventory;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    /// <summary>
    /// Mobius <c>MapRegionManager.getTeleToLocation(creature, TOWN)</c>: the town square of the region that owns the
    /// player's grid cell, or the Talking Island restart when the cell belongs to no region.
    /// </summary>
    (int X, int Y, int Z) TownDestination(ZoneEntity player)
    {
        var point = _mapRegions.TownRespawn(player.X, player.Y);
        return (point.X, point.Y, point.Z);
    }

    /// <summary>
    /// Interlude item skills 2013 (SOE) / 2036 (Blessed SOE) are not in <c>skills.json</c>; hit times match Mobius XML.
    /// </summary>
    /// <summary>
    /// Mobius ItemSkills for Scroll of Escape: <c>MagicSkillUse</c> now, consume + <c>teleToLocation(TOWN)</c> at hitTime.
    /// </summary>
    string? TryUseEscapeTown(
        ZoneSession session,
        ZoneEntity player,
        ItemInstance item,
        ItemSkillEffect effect,
        int reuseDelayMs,
        long now)
    {
        if (ConsumableRules.EscapeTownError(player.Dead, now < player.CastingUntilMs) is { } escapeError)
            return escapeError;

        int skillId = effect.SkillId > 0 ? effect.SkillId : 2013;
        int level = Math.Max(1, effect.SkillLevel);
        int hitTimeMs = ConsumableRules.EscapeHitTimeMs(skillId);
        var (x, y, z) = TownDestination(player);

        // Fan the cast gesture first so the client starts long-cast / short-cast before the teleport lands.
        var cast = new MagicSkillUseMsg
        {
            CasterObjectId = player.ObjectId,
            TargetObjectId = player.ObjectId,
            SkillId = skillId,
            SkillLevel = level,
            HitTimeMs = hitTimeMs,
            ReuseDelayMs = Math.Max(0, reuseDelayMs)
        };
        FanoutSkill(player, player, cast, targetStatus: null, CasterMpStatus(player));
        player.CastingUntilMs = now + hitTimeMs;

        var template = new SkillDefinition(
            skillId,
            effect.Name ?? "Scroll of Escape",
            passive: false,
            operateType: "A1",
            targetType: "SELF",
            castRange: 0,
            hitTimeMs: hitTimeMs,
            reuseDelayMs: Math.Max(0, reuseDelayMs),
            abnormalTimeSeconds: 0,
            isDebuff: false,
            levels: [new SkillLevelData(level, mpConsume: 0, power: 0)])
        {
            MagicType = 1
        };

        // Consume + teleport land together (Mobius destroys the scroll when the cast succeeds).
        ScheduleEffect(player, player, template, level, now, hitTimeMs, (_, __) =>
        {
            if (!player.Inventory!.TryReduce(item, 1, out bool removedEntirely))
                return;

            player.ItemReuseUntilMs[item.ItemId] = _clock().ToUnixTimeMilliseconds() + Math.Max(0, reuseDelayMs);
            NetBridge?.FanoutInventoryUpdate([session.ObjectId], InventoryMessages.Update(
                    removedEntirely ? InventoryChange.Remove : InventoryChange.Modify, item));
            Relocate(player, x, y, z);
            SelfUserInfo(player);
            SaveVitals(session, player);
            SaveInventory(session, player.Inventory);
            ZoneLog.Info($"escape object={session.ObjectId} item={item.ItemId} skill={skillId} -> {x},{y},{z}");
        });

        return null;
    }

    /// <summary>Mobius <c>Heal</c> with a level table: power grows with the caster level (HP potions 1061 … 1065).</summary>
    static int HealByLevel(int level, int basePower, int step) =>
        Math.Max(0, basePower) + Math.Max(0, step) * Math.Max(0, level - 1);
}
