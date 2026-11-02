using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Skills;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius attack skills: mpInitialConsume at cast start, mpConsume at launch; hit/cool time scaled by Cspd (magic)
/// or Pspd (physical) via calcAtkSpd; reuse ·333/speed; damage computed at hitTime (onMagicHitTimer).
/// </summary>
public sealed partial class ZoneWorld
{
    string? CastMobiusAttack(ZoneSession session, ZoneEntity player, ZoneEntity target, int skillId, int level,
        SkillCombatData combat, long now, List<Pending> outbound)
    {
        int mpInitial = combat.MpInitialAt(level);
        if (player.CurMp < mpInitial + combat.MpAt(level))
            return SkillProtocol.Mp;

        var ps = PlayerStats(player);
        int hitTime = ZoneCombat.SkillTimeMs(combat.HitTimeMs, combat.Magic, ps.MAtkSpd, ps.PAtkSpd);
        int coolTime = ZoneCombat.SkillTimeMs(combat.CoolTimeMs, combat.Magic, ps.MAtkSpd, ps.PAtkSpd);
        int reuse = ZoneCombat.SkillReuseMs(combat.ReuseDelayMs, combat.Magic, ps.MAtkSpd, ps.PAtkSpd);

        player.CurMp = Math.Max(0, player.CurMp - mpInitial);
        if (reuse > 0)
            player.SkillReuseUntilMs[skillId] = now + reuse;

        var cast = new MagicSkillUseMsg
        {
            CasterObjectId = player.ObjectId,
            TargetObjectId = target.ObjectId,
            SkillId = skillId,
            SkillLevel = level,
            HitTimeMs = hitTime,
            ReuseDelayMs = reuse
        };
        FanoutSkill(player, target, cast, targetStatus: null, CasterMpStatus(player), outbound);

        player.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        session.SelectedTargetId = target.ObjectId;
        player.CastingUntilMs = now + hitTime + coolTime;
        player.PendingHit = new PendingSkillHit(target.ObjectId, 0, now + hitTime, combat, level);
        ZoneLog.Info($"[skill] cast {skillId} lv{level} kind={combat.Kind} hit={hitTime}ms cool={coolTime}ms reuse={reuse}ms");
        if (hitTime == 0)
            ResolvePendingHit(player, now, outbound);
        return null;
    }

    /// <summary>Damage for a Mobius attack skill at launch; consumes mpConsume and (physical) a charged soulshot.</summary>
    int RollSkillDamage(ZoneEntity player, ZoneEntity target, SkillCombatData combat, int level, List<Pending>? outbound = null)
    {
        player.CurMp = Math.Max(0, player.CurMp - combat.MpAt(level));
        var ps = PlayerStats(player);
        double power = combat.PowerAt(level);
        if (!combat.IsPhysical)
        {
            double mDef = StatMods(target).Apply(CombatStat.MDef, target.MDef > 0 ? target.MDef : Math.Max(1, target.Level) * 30d);
            // Mobius calcMagicDam: blessed spiritshot ×4 M.Atk, spiritshot ×2; the charge fires on this cast.
            int spirit = player.SpiritshotCharge;
            player.SpiritshotCharge = 0;
            if (spirit > 0 && outbound != null)
                RechargeAutoShots(player, outbound);
            double mAtk = ps.MAtk * ZoneCombat.SpiritshotMAtkMultiplier(spirit);
            return ZoneCombat.MagicDamage(mAtk, mDef, power);
        }

        bool ss = player.SoulshotCharged;
        player.SoulshotCharged = false;
        if (ss && outbound != null)
            RechargeAutoShots(player, outbound);
        double pDef = NpcPDef(target);
        var shield = ZoneCombat.RollShield(_combatRandom, NpcShieldRate(target), ps.Weapon == Fineage.Game.WeaponType.Bow);
        if (shield == ZoneCombat.ShieldResult.Perfect)
            return 1;
        if (shield == ZoneCombat.ShieldResult.Block)
            pDef += target.ShieldDef;
        return ZoneCombat.PhysicalSkillDamage(ps.PAtk, pDef, power, ss);
    }
}
