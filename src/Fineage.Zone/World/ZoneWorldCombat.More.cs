using Fineage.Game;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Drops;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    // Physical stats. Players: class template values (same as UserInfo); NPCs: Mobius npcs.json.
    // TODO(combat parity): STR/level/weapon/armor/buff stat pipeline.
    int PlayerPAtkSpd(ZoneEntity player) => PlayerStats(player).PAtkSpd;

    long PlayerAttackTimeMs(int objectId) =>
        _byId.TryGetValue(objectId, out var p) ? ZoneCombat.AttackTimeMs(PlayerPAtkSpd(p)) : ZoneCombat.AutoAttackIntervalMs;

    double PlayerPAtk(ZoneEntity player) => PlayerStats(player).PAtk;

    double PlayerPDef(ZoneEntity player) => PlayerStats(player).PDef;

    int PlayerReach(ZoneEntity player) => ZoneCombat.PlayerAttackReach(PlayerStats(player).AttackRange);

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
            shieldRate = NpcShieldRate(target);
            shieldDef = target.ShieldDef;
        }
        else
        {
            var ts = PlayerStats(target);
            pDef = Math.Max(1d, ts.PDef);
            evasion = ts.Evasion;
            shieldRate = ts.ShieldRate;
            shieldDef = ts.ShieldDef;
        }

        var defenceTraits = target.Kind == ZoneEntityKind.Npc ? target.DefenceTraits : null;
        var attackTraits = AttackTraitCatalog.FromAbnormals(player.Abnormals.Select(a => (a.Id, a.SkillLevel)));
        return ZoneCombat.RollMelee(_combatRandom, ps.PAtk, pDef,
            ps.Accuracy, evasion, ps.CritRate, ps.RandomDamage,
            soulshot, (int)ps.WeaponGrade, shieldRate, shieldDef, ps.Weapon == Fineage.Game.WeaponType.Bow,
            ZoneCombat.HitConditionBonus(player.X, player.Y, player.Z, target.X, target.Y, target.Z, target.Heading, IsNight),
            ps.CritDmg,
            ZoneCombat.AttackTraitBonus(ps.Weapon, defenceTraits, attackTraits));
    }

    double NpcPDef(ZoneEntity npc) =>
        Math.Max(1d, StatMods(npc).Apply(CombatStat.PDef, npc.PDef > 0 ? npc.PDef : Math.Max(1, npc.Level) * 40d));

}
