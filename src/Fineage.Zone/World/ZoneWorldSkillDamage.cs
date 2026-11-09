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
        SkillCombatData combat, long now)
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
        StopForCast(player, target, now);
        FanoutSkill(player, target, cast, targetStatus: null, CasterMpStatus(player));

        player.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        session.SelectedTargetId = target.ObjectId;
        player.CastingUntilMs = now + hitTime + coolTime;
        SetPendingHit(player, new PendingSkillHit(target.ObjectId, 0, now + hitTime, combat, level));
        ZoneLog.Info($"[skill] cast {skillId} lv{level} kind={combat.Kind} hit={hitTime}ms cool={coolTime}ms reuse={reuse}ms");
        if (hitTime == 0)
            ResolvePendingHit(player, now);
        return null;
    }

    /// <summary>Damage for a Mobius attack skill at launch; consumes mpConsume and (physical) a charged soulshot.</summary>
    int RollSkillDamage(ZoneEntity player, ZoneEntity target, SkillCombatData combat, int level)
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
            if (spirit > 0)
                RechargeAutoShots(player);
            double mAtk = ps.MAtk * ZoneCombat.SpiritshotMAtkMultiplier(spirit);
            // Mobius skill magicLevel (skill-combat.json); missing table → attacker level.
            int magicLevel = combat.MagicLevelAt(level);
            if (magicLevel <= 0)
                magicLevel = player.Level;
            var (damage, outcome) = ZoneCombat.RollMagicDamage(_combatRandom, mAtk, mDef, power,
                player.Level, magicLevel, target.Level, ps.MCritRate, magicCritDmg: ps.MagicCritDmg);
            if (player.Session != null)
                MagicOutcomeNotice(player.Session, target, combat, outcome);
            return damage;
        }

        bool ss = player.SoulshotCharged;
        player.SoulshotCharged = false;
        if (ss)
            RechargeAutoShots(player);
        // Mobius calcPhysicalSkillEvasion on PhysicalAttack / FatalBlow / Backstab / EnergyAttack.
        double targetEvas = (target.Kind == ZoneEntityKind.Player ? PlayerStatMods(target) : StatMods(target))
            .Apply(CombatStat.PSkillEvasion, 0d);
        if (ZoneCombat.RollPhysicalSkillEvasion(_combatRandom, targetEvas))
        {
            if (player.Session != null)
                ShotNotice(player.Session, SysMsgSkillDodged, $"{target.Name} dodges the attack.");
            return 0;
        }
        if (combat.Kind == SkillDamageKind.Blow && combat.Blow is { } blow)
            return RollBlow(player, target, combat, level, blow, ps, ss);
        double pDef = NpcPDef(target);
        var shield = ZoneCombat.RollShield(_combatRandom, NpcShieldRate(target), ps.Weapon == Fineage.Game.WeaponType.Bow);
        if (shield == ZoneCombat.ShieldResult.Perfect)
            return 1;
        if (shield == ZoneCombat.ShieldResult.Block)
            pDef += target.ShieldDef;
        double str = StatBonus(PlayerClasses.StatBonuses, Fineage.Game.StatSlot.Str, EffectiveStr(player));
        bool crit = ZoneCombat.RollSkillCrit(_combatRandom, combat.BaseCritRate, str);
        if (crit && player.Session != null)
            ShotNotice(player.Session, SysMsgCriticalHit, "Critical hit!");
        var defenceTraits = target.Kind == ZoneEntityKind.Npc ? target.DefenceTraits : null;
        var attackTraits = AttackTraitCatalog.FromAbnormals(player.Abnormals.Select(a => (a.Id, a.SkillLevel)));
        return ZoneCombat.PhysicalSkillDamage(ps.PAtk, pDef, power, ss, crit, ps.CritDmg,
            ZoneCombat.AttackTraitBonus(ps.Weapon, defenceTraits, attackTraits));
    }

    public const int SysMsgLethalSuccess = 1668;

    /// <summary>Mobius FatalBlow / Backstab + Lethal effects for a player blow skill on a mob. 0 = blow failed (no damage).</summary>
    int RollBlow(ZoneEntity player, ZoneEntity target, SkillCombatData combat, int level, BlowEffect blow,
        PlayerCombatStats ps, bool ss)
    {
        bool front = ZoneCombat.IsInFrontOf(player.X, player.Y, target.X, target.Y, target.Heading);
        bool behind = ZoneCombat.IsBehind(player.X, player.Y, target.X, target.Y, target.Heading);
        double dex = StatBonus(PlayerClasses.StatBonuses, Fineage.Game.StatSlot.Dex, EffectiveDex(player));
        double str = StatBonus(PlayerClasses.StatBonuses, Fineage.Game.StatSlot.Str, EffectiveStr(player));
        // Backstab.calcSuccess: !isInFrontOf; both: calcBlowSuccess (+ BLOW_RATE fold).
        if ((blow == BlowEffect.Backstab && front) ||
            !ZoneCombat.RollBlowSuccess(_combatRandom, combat.BlowChance, dex, front, behind, ps.BlowRate))
        {
            ZoneLog.Info($"[skill] blow {combat.SkillId} failed front={front} behind={behind}");
            return 0;
        }
        var attackTraits = AttackTraitCatalog.FromAbnormals(player.Abnormals.Select(a => (a.Id, a.SkillLevel)));
        var (damage, outcome) = ZoneCombat.RollBlowDamage(_combatRandom, blow, ps.PAtk, NpcPDef(target), combat.PowerAt(level), ss,
            front, behind, ps.RandomDamage, combat.BaseCritRate, str, NpcShieldRate(target), target.ShieldDef, ps.CritDmg,
            ZoneCombat.AttackTraitBonus(ps.Weapon, target.DefenceTraits, attackTraits));
        int hpAfter = Math.Max(0, target.CurHp - damage);
        bool lethalable = target.NpcType is not ("RaidBoss" or "GrandBoss");
        var (extra, lethal) = ZoneCombat.RollLethal(_combatRandom, combat.FullLethal, combat.HalfLethal,
            combat.MagicLevelAt(level), target.Level, hpAfter, lethalable);
        if (player.Session != null)
        {
            if (outcome == ZoneCombat.BlowOutcome.Critical)
                ShotNotice(player.Session, SysMsgCriticalHit, "Critical hit!");
            if (lethal == ZoneCombat.LethalOutcome.Full)
                ShotNotice(player.Session, SysMsgLethalSuccess, "Your lethal strike was successful!");
            else if (lethal == ZoneCombat.LethalOutcome.Half)
                ShotNotice(player.Session, 0, "Half-Kill!");
        }
        return damage + extra;
    }

    public const int SysMsgCriticalHit = 44;
    public const int SysMsgResisted = 139;
    public const int SysMsgAttackFailed = 158;
    public const int SysMsgMagicCritical = 1280;
    /// <summary>Mobius <c>C1_DODGES_THE_ATTACK</c> (physical skill evasion).</summary>
    public const int SysMsgSkillDodged = 1999;

    void MagicOutcomeNotice(ZoneSession session, ZoneEntity target, SkillCombatData combat, ZoneCombat.MagicOutcome outcome)
    {
        switch (outcome)
        {
            case ZoneCombat.MagicOutcome.Critical:
                ShotNotice(session, SysMsgMagicCritical, "Magic Critical Hit!");
                break;
            case ZoneCombat.MagicOutcome.HalfFail:
                ShotNotice(session, SysMsgAttackFailed, "Your attack has failed.");
                break;
            case ZoneCombat.MagicOutcome.Resisted:
                ShotNotice(session, SysMsgResisted, $"{target.Name} has resisted your {combat.Name}.");
                break;
        }
    }
}
