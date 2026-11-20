using Fineage.Game;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Drops;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    // Physical stats. Players: PlayerStats (template + henna + gear + enchant + buffs/passives/penalties/sets).
    // NPCs: template fields folded through StatMods.
    int PlayerPAtkSpd(ZoneEntity player) => PlayerStats(player).PAtkSpd;

    long PlayerAttackTimeMs(int objectId) =>
        _byId.TryGetValue(objectId, out var p) ? ZoneCombat.AttackTimeMs(PlayerPAtkSpd(p)) : ZoneCombat.AutoAttackIntervalMs;

    double PlayerPAtk(ZoneEntity player) => PlayerStats(player).PAtk;

    double PlayerPDef(ZoneEntity player) => PlayerStats(player).PDef;

    int PlayerReach(ZoneEntity player) => ZoneCombat.PlayerAttackReach(PlayerStats(player).AttackRange);

    /// <summary>Mobius <c>physRange + both collision radii</c> for this attacker/target pair.</summary>
    int PlayerReach(ZoneEntity player, ZoneEntity target) => AttackReachAgainst(player, target);

    int AttackReachAgainst(ZoneEntity attacker, ZoneEntity target)
    {
        int weapon = attacker.Kind == ZoneEntityKind.Npc
            ? attacker.AttackRange
            : PlayerStats(attacker).AttackRange;
        return ZoneCombat.AttackReach(weapon, CollisionRadiusOf(attacker), CollisionRadiusOf(target));
    }

    /// <summary>
    /// Mobius <c>maybeMoveToPawn(target, skill.getCastRange())</c>. Skills with no listed castRange (self/passive)
    /// use 0, so the gate is just the two collision radii.
    /// </summary>
    int SkillCastReach(int castRange, ZoneEntity caster, ZoneEntity target) =>
        ZoneCombat.SkillCastReach(Math.Max(0, castRange), CollisionRadiusOf(caster), CollisionRadiusOf(target));

    int CollisionRadiusOf(ZoneEntity entity)
    {
        if (entity.Kind == ZoneEntityKind.Player)
            return ZoneCombat.PlayerCollisionRadius;
        if (entity.CollisionRadius > 0)
            return entity.CollisionRadius;
        if (entity.NpcId > 0)
        {
            _npcCollisionCatalog ??= Fineage.Zone.Npcs.NpcCatalog.LoadDefault();
            if (_npcCollisionCatalog.ById.TryGetValue(entity.NpcId, out var template) && template.CollisionRadius > 0)
                return (int)template.CollisionRadius;
        }
        return ZoneCombat.DefaultNpcCollision;
    }

    Fineage.Zone.Npcs.NpcCatalog? _npcCollisionCatalog;

    /// <summary>
    /// One physical swing from a player. A monster target rolls against its template P.Def / evasion / shield; a player
    /// target rolls against the same player stat pipeline an incoming mob swing uses (<see cref="PlayerStats"/>), so a
    /// duel is the same math as PvE with the target's own defence. Mobius applies the same
    /// <c>calcPhysicalDam</c>, only the stats behind it change.
    /// </summary>
    (int Damage, byte Flags) RollPlayerMelee(ZoneEntity player, PlayerCombatStats ps, ZoneEntity target, bool soulshot)
    {
        double pDef;
        double evasion;
        double shieldRate;
        int shieldDef;
        if (target.Kind == ZoneEntityKind.Npc)
        {
            pDef = NpcPDef(target);
            evasion = NpcEvasion(target);
            shieldRate = ShieldRateInArc(target, player, NpcShieldRate(target), 0);
            shieldDef = target.ShieldDef;
        }
        else
        {
            var ts = PlayerStats(target);
            pDef = Math.Max(1d, ts.PDef);
            evasion = ts.Evasion;
            shieldRate = ShieldRateInArc(target, player, ts.ShieldRate, ts.ShieldDefAngle);
            shieldDef = ts.ShieldDef;
        }

        var defenceTraits = target.Kind == ZoneEntityKind.Npc ? target.DefenceTraits : null;
        var attackTraits = AttackTraitCatalog.FromAbnormals(player.Abnormals.Select(a => (a.Id, a.SkillLevel)));
        // Mobius calcCrit: crit rate is folded through CRITICAL_RATE_POS while the target is known
        // (Focus Chance 356 → ×0.7 front, ×1.6 behind, ×1.3 side).
        var position = ZoneCombat.PositionOf(player.X, player.Y, target.X, target.Y, target.Heading);
        var positional = SkillStatEffects.CollectPositional(_statEffects, player.Abnormals, position);
        int critPermille = positional.IsEmpty
            ? ps.CritRate
            : ZoneCombat.PositionalCritPermille(ps.CritRate, positional.Apply(CombatStat.CritRatePos, 1d));
        double critDamageMod = positional.IsEmpty
            ? ps.CritDmg
            : ps.CritDmg * positional.Apply(CombatStat.CritDmgPos, 1d);
        return ZoneCombat.RollMelee(Rng(_combatRandom, player), ps.PAtk, pDef,
            ps.Accuracy, evasion, critPermille, ps.RandomDamage,
            soulshot, (int)ps.WeaponGrade, shieldRate, shieldDef, ps.Weapon == Fineage.Game.WeaponType.Bow,
            ZoneCombat.HitConditionBonus(player.X, player.Y, player.Z, target.X, target.Y, target.Z, target.Heading, IsNight),
            critDamageMod,
            ZoneCombat.AttackTraitBonus(ps.Weapon, defenceTraits, attackTraits),
            ps.CritDmgAdd);
    }

    /// <summary>
    /// NPC physical defence from the template (data/npcs.json is complete — no level×40 invent).
    /// Missing/zero PDef is a spawn/data bug: fail loud instead of silently fabricating defence.
    /// </summary>
    double NpcPDef(ZoneEntity npc)
    {
        if (npc.PDef <= 0)
            throw new InvalidOperationException(
                $"npc PDef missing npcId={npc.NpcId} object={npc.ObjectId} level={npc.Level}");
        return Math.Max(1d, StatMods(npc).Apply(CombatStat.PDef, npc.PDef));
    }

    internal double NpcPDefForTest(ZoneEntity npc) => NpcPDef(npc);

    /// <summary>Test hook: one melee roll against the player's current stats and stance (no HP change).</summary>
    internal (int Damage, byte Flags) RollPlayerMeleeForTest(ZoneEntity player, ZoneEntity target, bool soulshot = false) =>
        RollPlayerMelee(player, PlayerStats(player), target, soulshot);

}
