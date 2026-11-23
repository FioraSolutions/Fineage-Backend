using Fineage.Game.Skills;
using Fineage.Protocol;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius force / sonic charges: EtcStatusUpdate.charges, FocusEnergy, and cast-condition <c>player charges</c>.
/// </summary>
public sealed partial class ZoneWorld
{
    const int FocusEnergyIncrement = 1;

    internal bool TryResolveFocusEnergyCap(
        int skillId,
        int skillLevel,
        SkillEffectCatalog.Effect[] rows,
        out int cap)
    {
        cap = 0;
        if (!ForceChargeRules.TryFindFocusEnergy(rows, out var row))
            return false;
        if (ForceChargeRules.TryResolveCap(row, skillId, skillLevel, out cap))
            return true;
        ZoneLog.Warn($"FocusEnergy skill={skillId} lv={skillLevel} missing or invalid charge param");
        return false;
    }

    void IncreaseForceCharges(ZoneSession? session, ZoneEntity player, int count, int cap)
    {
        if (cap <= 0)
            return;

        if (player.ForceCharges >= cap)
        {
            if (session != null)
                SelfLine(session, ForceChargeRules.SysMsgForceMaxCapacity, "Your force has reached maximum capacity.");
            return;
        }

        int next = Math.Min(cap, player.ForceCharges + Math.Max(1, count));
        player.ForceCharges = next;

        if (session != null)
        {
            if (player.ForceCharges >= cap)
                SelfLine(session, ForceChargeRules.SysMsgForceMaxCapacity, "Your force has reached maximum capacity.");
            else
                SelfLine(session, ForceChargeRules.SysMsgForceIncreased,
                    $"Your force has increased to {player.ForceCharges} level.",
                    player.ForceCharges.ToString());
        }

        RestartForceChargeDecay(player);
        BroadcastForceCharges(player);
        ZoneLog.Info($"force-charge object={player.ObjectId} charges={player.ForceCharges}/{cap}");
    }

    void ApplyFocusEnergy(ZoneSession? session, ZoneEntity player, int skillId, int level)
    {
        if (!_skillEffects.TryGet(skillId, out var rows)
            || !TryResolveFocusEnergyCap(skillId, level, rows, out int cap))
            return;

        IncreaseForceCharges(session, player, FocusEnergyIncrement, cap);
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

        RestartForceChargeDecay(player);
        BroadcastForceCharges(player);
    }

    void RestartForceChargeDecay(ZoneEntity player)
    {
        if (player.ForceCharges <= 0)
        {
            player.ForceChargeResetAtUnixMs = 0;
            return;
        }

        player.ForceChargeResetAtUnixMs = NowUnixMs() + ForceChargeRules.DecayMs;
    }

    internal void ClearForceCharges(ZoneEntity player)
    {
        if (player.ForceCharges <= 0 && player.ForceChargeResetAtUnixMs <= 0)
            return;
        player.ForceCharges = 0;
        player.ForceChargeResetAtUnixMs = 0;
        BroadcastForceCharges(player);
    }

    void TickForceChargeDecay(long now)
    {
        foreach (var player in Players())
        {
            if (player.ForceCharges <= 0 || player.ForceChargeResetAtUnixMs <= 0)
                continue;
            if (now >= player.ForceChargeResetAtUnixMs)
                ClearForceCharges(player);
        }
    }

    void BroadcastForceCharges(ZoneEntity player)
    {
        if (player.Session == null)
            return;
        NetBridge?.FanoutEtcStatusUpdate([player.ObjectId], EtcStatusOf(player, InDangerArea(player)));
    }

    static bool HasFocusEnergy(SkillEffectCatalog.Effect[] rows) =>
        ForceChargeRules.TryFindFocusEnergy(rows, out _);

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

        if (!_skillEffects.TryGet(request.SkillId, out var rows)
            || !TryResolveFocusEnergyCap(request.SkillId, level, rows, out int cap))
        {
            SelfLine(session, SysMsgUnsuitableTerms, $"{template.Name} cannot be used due to unsuitable terms.");
            return null;
        }

        if (player.ForceCharges >= cap)
        {
            SelfLine(session, ForceChargeRules.SysMsgForceMaxCapacity, "Your force has reached maximum capacity.");
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
            (_, _) => ApplyFocusEnergy(session, player, request.SkillId, level));
        return null;
    }

    internal void ApplyFocusEnergyAfterCombatHit(ZoneEntity player, int skillId, int level)
    {
        if (player.Kind != ZoneEntityKind.Player)
            return;
        if (!_skillEffects.TryGet(skillId, out var rows) || !HasFocusEnergy(rows))
            return;
        ApplyFocusEnergy(player.Session, player, skillId, level);
    }
}