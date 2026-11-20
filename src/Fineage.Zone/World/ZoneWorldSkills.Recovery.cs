using Fineage.Game.Skills;
using Fineage.Protocol;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius instant recovery effect handlers (<c>Heal</c>, <c>HealPercent</c>, <c>CpHeal</c>, <c>CpHealPercent</c>,
/// <c>ManaHeal</c>, <c>ManaHealPercent</c>, <c>ManaHealByLevel</c>), driven by the skill's own
/// <c>skill-effects.json</c> rows. Braveheart (440 CpHeal 1000), Body To Mind (1157 ManaHeal), Recharge (1013
/// ManaHealByLevel), Pa'agrio's Honor (1305 CpHeal) … all go through this one path; nothing is keyed on a skill id.
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>Must stay in lock-step with <c>scripts/sync_recovery_effect_powers_from_mobius.py</c>.</summary>
    internal static readonly HashSet<string> RecoveryEffectNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Heal", "HealPercent", "CpHeal", "CpHealPercent", "ManaHeal", "ManaHealPercent", "ManaHealByLevel"
    };

    /// <summary>Mobius S1_HP_HAS_BEEN_RESTORED / S2_HP_HAS_BEEN_RESTORED_BY_S1.</summary>
    public const int SysMsgHpRestoredAmount = 1066, SysMsgHpRestoredBy = 1067;

    /// <summary>Mobius S1_MP_HAS_BEEN_RESTORED / S2_MP_HAS_BEEN_RESTORED_BY_S1.</summary>
    public const int SysMsgMpRestored = 1068, SysMsgMpRestoredBy = 1069;

    /// <summary>Mobius S1_CPS_HAVE_BEEN_RESTORED / S1_RESTORES_S2_CP.</summary>
    public const int SysMsgCpRestored = 1405, SysMsgCpRestoredBy = 1406;

    static bool HasRecoveryRow(SkillEffectCatalog.Effect[] rows)
    {
        foreach (var row in rows)
        {
            if (RecoveryEffectNames.Contains(row.Name))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Run every recovery row of the skill on <paramref name="target"/> (Mobius applies each instant effect in
    /// datapack order) and send one StatusUpdate for the bars that moved. Returns false when the skill has no
    /// recovery amount at this level (neither a row power nor the legacy <c>Heal</c> power in skills.json).
    /// </summary>
    internal bool ApplyRecoveryEffects(ZoneEntity caster, ZoneEntity target, SkillDefinition template,
        SkillLevelData levelData, int level)
    {
        var rows = EffectsOf(template.SkillId);
        bool any = false, hp = false, mp = false, cp = false;
        foreach (var row in rows)
        {
            if (!RecoveryEffectNames.Contains(row.Name))
                continue;
            if (target.Dead)
                break;
            double power = row.PowerAt(level);
            // skills.json power is the Heal amount the support path always used (Mobius Heal power = #amount).
            if (row.Name.Equals("Heal", StringComparison.OrdinalIgnoreCase) && levelData.Power > 0)
                power = levelData.Power;
            if (power <= 0)
                continue;
            any = true;
            switch (row.Name.ToLowerInvariant())
            {
                case "heal":
                    hp |= RestoreHp(caster, target, HealedAmount(target, (int)power));
                    break;
                case "healpercent":
                    hp |= RestoreHp(caster, target, PercentOf(target.MaxHp, power));
                    break;
                case "cpheal":
                    cp |= RestoreCp(caster, target, (int)power);
                    break;
                case "cphealpercent":
                    cp |= RestoreCp(caster, target, PercentOf(target.MaxCp, power));
                    break;
                case "manaheal":
                    mp |= RestoreMp(caster, target, (int)power);
                    break;
                case "manahealpercent":
                    mp |= RestoreMp(caster, target, PercentOf(target.MaxMp, power));
                    break;
                case "manahealbylevel":
                    mp |= RestoreMp(caster, target,
                        (int)(power * ManaHealByLevelFactor(target.Level, levelData.MagicLevel)));
                    break;
            }
        }
        if (hp || mp || cp)
            FanoutVitals(target, target.Kind == ZoneEntityKind.Player ? PlayerVitalsStatus(target) : StatusFor(target));
        return any;
    }

    /// <summary>True when the skill has a recovery row with a power at this level, or a legacy Heal power.</summary>
    internal bool HasRecoveryAmount(SkillDefinition template, SkillLevelData levelData, int level)
    {
        foreach (var row in EffectsOf(template.SkillId))
        {
            if (!RecoveryEffectNames.Contains(row.Name))
                continue;
            if (row.PowerAt(level) > 0)
                return true;
            if (row.Name.Equals("Heal", StringComparison.OrdinalIgnoreCase) && levelData.Power > 0)
                return true;
        }
        return levelData.Power > 0;
    }

    /// <summary>Mobius percent heals: <c>power == 100</c> restores the full bar, otherwise <c>max · power / 100</c>.</summary>
    static int PercentOf(int max, double power) =>
        power >= 100d ? max : (int)(max * power / 100d);

    /// <summary>
    /// Mobius ManaHealByLevel: when the target is more than 5 levels above the skill's magic level, each extra level
    /// takes 10 % off (diff 6 → 90 %, …, diff 14 → 10 %), and from diff 15 nothing is restored.
    /// </summary>
    internal static double ManaHealByLevelFactor(int targetLevel, int magicLevel)
    {
        int diff = targetLevel - magicLevel;
        if (magicLevel <= 0 || diff <= 5)
            return 1d;
        if (diff >= 15)
            return 0d;
        return 1d - (diff - 5) * 0.1;
    }

    bool RestoreHp(ZoneEntity caster, ZoneEntity target, int amount)
    {
        amount = Math.Max(0, Math.Min(amount, target.MaxHp - target.CurHp));
        if (amount > 0)
            target.CurHp += amount;
        RecoveryNotice(caster, target, amount, SysMsgHpRestoredAmount, SysMsgHpRestoredBy, "HP");
        return amount > 0;
    }

    bool RestoreMp(ZoneEntity caster, ZoneEntity target, int amount)
    {
        amount = Math.Max(0, Math.Min(amount, target.MaxMp - target.CurMp));
        if (amount > 0)
            target.CurMp += amount;
        RecoveryNotice(caster, target, amount, SysMsgMpRestored, SysMsgMpRestoredBy, "MP");
        return amount > 0;
    }

    bool RestoreCp(ZoneEntity caster, ZoneEntity target, int amount)
    {
        amount = Math.Max(0, Math.Min(amount, target.MaxCp - target.CurCp));
        if (amount > 0)
            target.CurCp += amount;
        if (target.CurCp >= target.MaxCp)
            target.RegenCarryCp = 0;
        RecoveryNotice(caster, target, amount, SysMsgCpRestored, SysMsgCpRestoredBy, "CP");
        return amount > 0;
    }

    void RecoveryNotice(ZoneEntity caster, ZoneEntity target, int amount, int selfId, int byId, string bar)
    {
        if (target.Kind != ZoneEntityKind.Player)
            return;
        if (caster.ObjectId == target.ObjectId || string.IsNullOrEmpty(caster.Name))
            NotifyLine(target.ObjectId, selfId, $"{amount} {bar} has been restored.", amount.ToString());
        else if (bar == "CP")
            NotifyLine(target.ObjectId, byId, $"{caster.Name} restores {amount} CP.", caster.Name, amount.ToString());
        else
            NotifyLine(target.ObjectId, byId, $"{amount} {bar} has been restored by {caster.Name}.",
                caster.Name, amount.ToString());
    }
}
