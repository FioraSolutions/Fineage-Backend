using Fineage.Game;
using Fineage.Game.Services;
using Fineage.Game.Skills;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Inventory;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
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

        var skill = ItemCastSkill(item, effect);
        int level = Math.Max(1, effect.SkillLevel);
        var (x, y, z) = TownDestination(player);

        // Consume + teleport land together (Mobius destroys the scroll when the cast succeeds).
        BeginTownTeleport(player, skill, level, reuseDelayMs, now, () =>
        {
            if (!player.Inventory!.TryReduce(item, 1, out bool removedEntirely))
                return;

            player.ItemReuseUntilMs[item.ItemId] = NowUnixMs() + Math.Max(0, reuseDelayMs);
            Relocate(player, x, y, z);
            SelfUserInfo(player);
            SaveVitals(session, player);
            SaveInventory(session, player.Inventory);
            ZoneLog.Info($"escape object={session.ObjectId} item={item.ItemId} skill={skill.SkillId} -> {x},{y},{z}");
        });

        return null;
    }

    /// <summary>
    /// The Mobius skill an item casts (ItemSkills): its template must be in <c>skills.json</c>, because the cast's
    /// hit time and name come from it. A missing template is a datapack gap, not something to improvise around.
    /// </summary>
    SkillDefinition ItemCastSkill(ItemInstance item, ItemSkillEffect effect) =>
        CatalogCastSkill(effect.SkillId, $"item {item.ItemId} ({effect.Name})");

    /// <summary>A skill the zone casts on the player's behalf; throws naming the skill and its caster when missing.</summary>
    SkillDefinition CatalogCastSkill(int skillId, string castBy)
    {
        if (skillId <= 0)
            throw new InvalidOperationException($"[town-teleport] {castBy} names no skill id in item-skills.json");
        try
        {
            return _game.Skills.Require(skillId);
        }
        catch (Fineage.Game.RequiredData.RequiredDataException ex)
        {
            throw new InvalidOperationException(
                $"[town-teleport] skill {skillId} cast by {castBy} is missing from skills.json; " +
                "import it from Mobius with tools/import_skills.py --add-missing",
                ex);
        }
    }

    /// <summary>
    /// Shared town teleport: <c>MagicSkillUse</c> now, <paramref name="onLand"/> at the skill's hit time.
    /// Scroll of Escape and the unstuck user command both land through this path.
    /// </summary>
    void BeginTownTeleport(
        ZoneEntity player,
        SkillDefinition skill,
        int level,
        int reuseDelayMs,
        long now,
        Action onLand)
    {
        int skillId = skill.SkillId;
        int hitTimeMs = skill.HitTimeMs;
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
            skill.Name,
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
        ScheduleEffect(player, player, template, level, now, hitTimeMs, (_, __) => onLand());
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

        var skill = ItemCastSkill(item, effect);
        int skillId = skill.SkillId;
        int level = Math.Max(1, effect.SkillLevel);
        int hitTimeMs = skill.HitTimeMs;
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
            skill.Name,
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
