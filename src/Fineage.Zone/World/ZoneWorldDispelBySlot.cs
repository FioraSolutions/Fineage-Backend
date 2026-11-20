using Fineage.Game.Skills;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Skills;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius <c>DispelBySlot</c> self/party cleanses: Escape Shackle (453, ROOT_PHYSICALLY ×3), Break Duress (461),
/// Cure Poison (1012), Purify (1018), Servitor Cure / Blessing (1300 / 1301), Vitalize (1020 heal + cleanse).
/// <para>
/// The effect removes up to <c>count</c> abnormals of one slot per pair; a level table
/// (<c>#dispelAbnormals</c>) carries a different list per skill level. Routing this as a heal threw on the
/// missing <c>power</c> before, so the skill healed nothing and cleared nothing.
/// </para>
/// </summary>
public sealed partial class ZoneWorld
{
    string? TrySkillUseDispelBySlot(
        ZoneSession session,
        ZoneEntity player,
        SkillUseIntent request,
        SkillDefinition template,
        SkillLevelData levelData,
        int level,
        long now,
        IReadOnlyList<SkillEffectCatalog.Effect> rows)
    {
        int targetId = ResolveCleanseTargetId(session, player, request, template);
        if (targetId <= 0 || !_byId.TryGetValue(targetId, out var target))
        {
            IncorrectTarget(session);
            return null;
        }
        if (target.Dead)
        {
            IncorrectTarget(session);
            return null;
        }
        if (targetId != player.ObjectId && !VisibleTo(player.ObjectId, targetId))
        {
            IncorrectTarget(session);
            return null;
        }
        if (template.CastRange > 0 &&
            targetId != player.ObjectId &&
            !InSkillCastRange(player, target, template.CastRange))
            return SkillProtocol.Range;

        // One level's worth of pairs per cast: the table index is the skill level, not the target.
        var pairs = CleansedSlots(rows, level);
        if (pairs.Count == 0)
            return null;

        ConsumeSkillMp(player, template, levelData.MpConsume);
        var (hitMs, reuseMs) = ScaledSkillTimes(player, template, level);
        if (reuseMs > 0)
            player.SkillReuseUntilMs[request.SkillId] = now + reuseMs;

        var cast = BuildCast(player, target, request.SkillId, level, hitMs, reuseMs);
        StopForCast(player, target, now);
        FanoutSkill(player, target, cast, targetStatus: null, CasterMpStatus(player));
        // Mobius onMagicFinalizer: a cleanse is a positive support skill → no attack stance.
        EnterCombatStanceOnCast(player, template, level, now);
        player.CastingUntilMs = now + Math.Max(0, hitMs);
        session.SelectedTargetId = targetId;

        // Mobius DispelBySlot.onStart at hitTime: slot-by-slot removal, then one abnormal refresh.
        var slots = pairs.ToArray();
        ScheduleEffect(player, target, template, level, now, hitMs, (t, _) =>
        {
            int removed = 0;
            foreach (var (slot, count) in slots)
                removed += ZoneAbnormals.DispelBySlot(t.Abnormals, slot, count);
            if (removed > 0)
                FanoutAbnormal(t);
        });
        return null;
    }

    /// <summary>Self / servitor / friendly single target, the target types the clean-up skills use.</summary>
    int ResolveCleanseTargetId(ZoneSession session, ZoneEntity player, SkillUseIntent request, SkillDefinition template)
    {
        if (string.Equals(template.TargetType, "SELF", StringComparison.OrdinalIgnoreCase))
            return player.ObjectId;
        if (string.Equals(template.TargetType, "SERVITOR", StringComparison.OrdinalIgnoreCase))
            return player.Summon != null ? player.SummonObjectId : 0;
        return ResolveFriendlyTargetId(session, player, request, template);
    }

    /// <summary>
    /// <c>dispel="SLOT,count;SLOT,count"</c> for the cast level. Rows without a value keep nothing, so a skill whose
    /// datapack table failed to expand is a silent no-op rather than a crash.
    /// </summary>
    static List<(string Slot, int Count)> CleansedSlots(
        IReadOnlyList<SkillEffectCatalog.Effect> rows, int level)
    {
        var pairs = new List<(string, int)>();
        foreach (var row in rows)
        {
            string value = row.DispelAt(level);
            if (string.IsNullOrWhiteSpace(value))
                continue;
            foreach (string pair in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split(',', StringSplitOptions.TrimEntries);
                if (parts.Length == 0 || parts[0].Length == 0)
                    continue;
                int count = parts.Length > 1 && int.TryParse(parts[1], out int parsed) ? parsed : 1;
                if (count > 0)
                    pairs.Add((parts[0].ToUpperInvariant(), count));
            }
        }
        return pairs;
    }
}
