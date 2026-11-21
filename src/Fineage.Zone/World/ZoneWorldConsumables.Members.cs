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
        var point = _mapRegions.TownRespawn(player.X, player.Y, Rng(_respawnRandom, player));
        return (point.X, point.Y, point.Z);
    }

    /// <summary>Mobius <c>teleToLocation(CLANHALL)</c> for the clan's owned hideout.</summary>
    (int X, int Y, int Z)? ClanHallDestination(ZoneEntity player)
    {
        int hallId = PlayerHallId(player);
        if (hallId <= 0)
            return null;
        if (!_clanHallSpawns.TryResolve(hallId, player.Karma > 0, Rng(_respawnRandom, player), out var point))
            return null;
        return (point.X, point.Y, point.Z);
    }

    int PlayerHallId(ZoneEntity player) =>
        _clan.TryGet(ClanIdFor(player.ObjectId), out var clan) ? clan.D.HallId : 0;

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

            player.ItemReuseUntilMs[item.ItemId] = NowUnixMs() + Math.Max(0, reuseDelayMs);
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

    /// <summary>Mobius clan-hall Scroll of Escape (skills 2040 / 2177): cast + <c>teleToLocation(CLANHALL)</c> at hitTime.</summary>
    string? TryUseEscapeClanHall(
        ZoneSession session,
        ZoneEntity player,
        ItemInstance item,
        ItemSkillEffect effect,
        int reuseDelayMs,
        long now)
    {
        bool hasHall = PlayerHallId(player) > 0;
        if (ConsumableRules.EscapeClanHallError(player.Dead, now < player.CastingUntilMs, hasHall) is { } escapeError)
            return escapeError;

        int skillId = effect.SkillId > 0 ? effect.SkillId : 2040;
        int level = Math.Max(1, effect.SkillLevel);
        int hitTimeMs = ConsumableRules.EscapeHitTimeMs(skillId);
        var dest = ClanHallDestination(player);
        if (dest is not { } loc)
            return ConsumableRules.EscapeNoClanHall;

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
            effect.Name ?? "Scroll of Escape: Clan Hall",
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

        ScheduleEffect(player, player, template, level, now, hitTimeMs, (_, __) =>
        {
            if (!player.Inventory!.TryReduce(item, 1, out bool removedEntirely))
                return;

            player.ItemReuseUntilMs[item.ItemId] = NowUnixMs() + Math.Max(0, reuseDelayMs);
            NetBridge?.FanoutInventoryUpdate([session.ObjectId], InventoryMessages.Update(
                    removedEntirely ? InventoryChange.Remove : InventoryChange.Modify, item));
            Relocate(player, loc.X, loc.Y, loc.Z);
            SelfUserInfo(player);
            SaveVitals(session, player);
            SaveInventory(session, player.Inventory);
            ZoneLog.Info($"escape_clanhall object={session.ObjectId} item={item.ItemId} skill={skillId} -> {loc.X},{loc.Y},{loc.Z}");
        });

        return null;
    }

    /// <summary>Mobius <c>Heal</c> with a level table: power grows with the caster level (HP potions 1061 … 1065).</summary>
    static int HealByLevel(int level, int basePower, int step) =>
        Math.Max(0, basePower) + Math.Max(0, step) * Math.Max(0, level - 1);
}
