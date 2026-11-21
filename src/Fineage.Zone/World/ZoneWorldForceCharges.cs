using Fineage.Game.Skills;
using Fineage.Protocol;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius force / sonic charges: EtcStatusUpdate.charges, FocusEnergy, and cast-condition <c>player charges</c>.
/// </summary>
public sealed partial class ZoneWorld
{
    const int SonicFocusSkillId = 8;
    const int FocusForceSkillId = 50;

    internal int MaxForceCharges(ZoneEntity player)
    {
        int cap = 0;
        if (player.KnownSkills.TryGetValue(SonicFocusSkillId, out int sonic))
            cap = Math.Max(cap, sonic);
        if (player.KnownSkills.TryGetValue(FocusForceSkillId, out int force))
            cap = Math.Max(cap, force);
        return Math.Clamp(cap, 0, 7);
    }

    void ApplyFocusEnergy(ZoneEntity player, int skillId, int level)
    {
        int cap = MaxForceCharges(player);
        if (cap <= 0)
        {
            ZoneLog.Warn($"FocusEnergy skill={skillId} lv={level} refused: no Sonic Focus / Focus Force level.");
            return;
        }

        if (player.ForceCharges >= cap)
            return;

        player.ForceCharges++;
        BroadcastForceCharges(player);
        ZoneLog.Info($"force-charge object={player.ObjectId} skill={skillId} charges={player.ForceCharges}/{cap}");
    }

    void SpendForceCharges(ZoneEntity player, int amount)
    {
        if (amount <= 0)
            return;
        if (player.ForceCharges < amount)
        {
            ZoneLog.Warn(
                $"force-charge spend underflow object={player.ObjectId} have={player.ForceCharges} need={amount}");
            player.ForceCharges = 0;
        }
        else
            player.ForceCharges -= amount;

        BroadcastForceCharges(player);
    }

    void BroadcastForceCharges(ZoneEntity player)
    {
        if (player.Session == null)
            return;
        NetBridge?.FanoutEtcStatusUpdate([player.ObjectId], EtcStatusOf(player));
    }

    static bool HasFocusEnergy(SkillEffectCatalog.Effect[] rows)
    {
        foreach (var row in rows)
        {
            if (row.Name.Equals("FocusEnergy", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    string? TrySkillUseFocusEnergy(
        ZoneSession session,
        ZoneEntity player,
        SkillUseIntent request,
        SkillDefinition template,
        SkillLevelData levelData,
        int level,
        long now)
    {
        if (!template.TargetType.Equals("SELF", StringComparison.OrdinalIgnoreCase))
        {
            SelfLine(session, SysMsgUnsuitableTerms, $"{template.Name} cannot be used due to unsuitable terms.");
            return null;
        }

        ConsumeSkillMp(player, template, levelData.MpConsume);
        var (hitMs, reuseMs) = ScaledSkillTimes(player, template, level);
        if (reuseMs > 0)
            player.SkillReuseUntilMs[request.SkillId] = now + reuseMs;

        var cast = BuildCast(player, player, request.SkillId, level, hitMs, reuseMs);
        StopForCast(player, player, now);
        FanoutSkill(player, player, cast, targetStatus: null, CasterMpStatus(player));
        player.CastingUntilMs = now + Math.Max(0, hitMs);
        ScheduleEffect(player, player, template, level, now, hitMs,
            (_, _) => ApplyFocusEnergy(player, request.SkillId, level));
        return null;
    }
}