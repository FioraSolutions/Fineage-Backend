using Fineage.Game.Skills;
using Fineage.Protocol;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    /// <summary>
    /// Apply a skill abnormal. Debuffs roll Mobius <c>calcEffectSuccess</c> (activateRate × trait resist)
    /// unless <paramref name="force"/> (admin packs). Returns false when resisted (no abnormal written).
    /// </summary>
    bool ApplyAbnormalFromSkill(
        SkillDefinition template,
        SkillLevelData levelData,
        ZoneEntity target,
        long nowUnixMs,
        ZoneEntity? attacker = null,
        bool force = false,
        int? durationSeconds = null)
    {
        if (!force && template.IsDebuff &&
            !RollDebuffLand(attacker, target, template, levelData, out _))
            return false;

        int duration = durationSeconds ?? template.AbnormalTimeSeconds;
        ZoneAbnormals.Apply(
            target.Abnormals,
            template.SkillId,
            levelData.AbnormalLevel,
            duration,
            nowUnixMs,
            levelData.Level,
            template.AbnormalType);
        if (target.Abnormals.Count > 0)
            _withAbnormals.Add(target.ObjectId);
        FanoutAbnormal(target);
        ApplyControlFromSlot(target, template.AbnormalType);
        return true;
    }

    /// <summary>Mobius calcEffectSuccess for debuffs; jewelry DefenceTrait lands in <paramref name="traitMod"/>.</summary>
    bool RollDebuffLand(
        ZoneEntity? attacker,
        ZoneEntity target,
        SkillDefinition template,
        SkillLevelData levelData,
        out double chance)
    {
        chance = 100d;
        if (!template.IsDebuff)
            return true;
        if (template.ActivateRate < 0 || string.IsNullOrWhiteSpace(template.BasicProperty))
            return true;

        double traitMod = ZoneCombat.GeneralResistanceBonus(
            attackTraits: null,
            defenceTraits: TargetDefenceTraits(target),
            trait: template.Trait);

        double mAtkMod = 1d;
        if (template.MagicType == 1 && attacker != null)
            mAtkMod = DebuffMAtkMod(attacker, target);

        if (levelData.MagicLevel <= 0)
            throw new InvalidOperationException(
                $"skill {template.SkillId} lv{levelData.Level} missing magicLevel for calcEffectSuccess");
        chance = ZoneCombat.DebuffLandChance(
            template.ActivateRate,
            template.BasicProperty,
            levelData.MagicLevel,
            template.LvlBonusRate,
            target.Level,
            TargetBaseStat(target, template.BasicProperty),
            traitMod,
            mAtkMod);

        bool landed = ZoneCombat.RollDebuffLand(_combatRandom, chance);
        ZoneLog.Info(
            $"[debuff-land] skill={template.SkillId} lv={levelData.Level} ml={levelData.MagicLevel} " +
            $"rate={template.ActivateRate} trait={template.Trait} traitMod={traitMod:F3} mAtkMod={mAtkMod:F3} " +
            $"chance={chance:F1} landed={landed} atk={attacker?.ObjectId ?? 0} tgt={target.ObjectId}");
        if (!landed)
        {
            if (attacker?.Session != null)
                ShotNotice(attacker.Session, SysMsgResisted,
                    $"{target.Name} has resisted your {template.Name}.");
            else if (target.Session != null)
                ShotNotice(target.Session, SysMsgResisted,
                    $"You have resisted {template.Name}.");
        }
        return landed;
    }

    /// <summary>Player jewelry/set DefenceTrait map; NPC race/weapon traits from the spawn template.</summary>
    IReadOnlyDictionary<string, double>? TargetDefenceTraits(ZoneEntity target) =>
        target.Kind == ZoneEntityKind.Player
            ? PlayerDefenceTraits(target)
            : target.DefenceTraits;

    int TargetBaseStat(ZoneEntity target, string basicProperty)
    {
        if (target.Kind != ZoneEntityKind.Player)
            return 0;
        return basicProperty.ToUpperInvariant() switch
        {
            "STR" => EffectiveStr(target),
            "DEX" => EffectiveDex(target),
            "CON" => EffectiveCon(target),
            "INT" => EffectiveAttributes(target).Int,
            "MEN" => EffectiveMen(target),
            "WIT" => EffectiveAttributes(target).Wit,
            _ => 0
        };
    }

    /// <summary>Mobius calcEffectSuccess mAtkMod for magic debuffs: sqrt(mAtk)/mDef·11 (no BSS yet).</summary>
    double DebuffMAtkMod(ZoneEntity attacker, ZoneEntity target)
    {
        double mAtk = attacker.Kind == ZoneEntityKind.Player
            ? PlayerStats(attacker).MAtk
            : Math.Max(1d, attacker.MAtk);
        double mDef = target.Kind == ZoneEntityKind.Player
            ? PlayerStats(target).MDef
            : Math.Max(1d, target.MDef > 0 ? target.MDef : target.Level * 30d);
        return Math.Sqrt(Math.Max(0, mAtk)) / Math.Max(1d, mDef) * 11d;
    }

    void ApplyAbnormalPlaytest(
        ZoneEntity target,
        int effectId,
        int value,
        int durationSeconds,
        long nowUnixMs)
    {
        ZoneAbnormals.Apply(target.Abnormals, effectId, value, durationSeconds, nowUnixMs);
        if (target.Abnormals.Count > 0)
            _withAbnormals.Add(target.ObjectId);
        FanoutAbnormal(target);
    }
}
