using Fineage.Game.Skills;
using Fineage.Zone.Combat;
using Fineage.Protocol;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius ElementSeed charges, <c>&lt;player&gt;</c> skill-use conditions, and Distrust (monster turns on another
/// monster). Classification is by effect / condition name, never by skill id.
/// </summary>
public sealed partial class ZoneWorld
{
    const int ElementSeedRangeMs = 5_000;
    const int DistrustSearchRange = 1100;

    void AddElementSeed(ZoneEntity target, string element, long now)
    {
        if (target.Kind != ZoneEntityKind.Player || string.IsNullOrWhiteSpace(element))
            return;
        var until = target.ElementSeedUntilMs;
        if (!until.TryGetValue(element, out var list) || list == null)
        {
            list = [];
            until[element] = list;
        }
        list.RemoveAll(t => t <= now);
        list.Add(now + ElementSeedRangeMs);
    }

    internal int ElementSeedCount(ZoneEntity player, string element, long now)
    {
        if (!player.ElementSeedUntilMs.TryGetValue(element, out var list) || list == null)
            return 0;
        list.RemoveAll(t => t <= now);
        return list.Count;
    }

    int TotalElementSeeds(ZoneEntity player, long now)
    {
        int n = 0;
        foreach (var key in player.ElementSeedUntilMs.Keys.ToList())
            n += ElementSeedCount(player, key, now);
        return n;
    }

    void ApplyElementSeedsFromSkill(SkillDefinition template, ZoneEntity target, long now)
    {
        if (!_skillEffects.TryGet(template.SkillId, out var rows))
            return;
        foreach (var effect in rows)
        {
            if (!EffectNameIs(effect, "ElementSeed"))
                continue;
            string element = effect.ReadExtra("element");
            if (element.Length == 0)
                continue;
            AddElementSeed(target, element, now);
        }
    }

    /// <summary>Mobius Skill.checkCondition <c>msgId=113</c> player gates (element seeds, required stance).</summary>
    bool PlayerConditionsMet(ZoneSession session, ZoneEntity player, SkillDefinition template)
    {
        if (template.PlayerConditions.Count == 0)
            return true;
        long now = NowUnixMs();
        foreach (var cond in template.PlayerConditions)
        {
            if (!PlayerConditionMet(player, cond, now))
            {
                SelfLine(session, SysMsgUnsuitableTerms, $"{template.Name} cannot be used due to unsuitable terms.");
                return false;
            }
        }
        return true;
    }

    bool PlayerConditionMet(ZoneEntity player, SkillPlayerCondition cond, long now)
    {
        if (cond.Type.Equals("elementSeeds", StringComparison.OrdinalIgnoreCase))
            return TotalElementSeeds(player, now) >= Math.Max(1, cond.Value);
        if (cond.Type.Equals("elementSeed", StringComparison.OrdinalIgnoreCase))
            return ElementSeedCount(player, cond.Arg, now) >= Math.Max(1, cond.Value);
        if (cond.Type.Equals("activeEffectIdLvl", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(cond.Arg, out int skillId) || skillId <= 0)
                return false;
            int need = Math.Max(1, cond.Value);
            foreach (var ab in player.Abnormals)
            {
                if (ab.Id == skillId && Math.Max(ab.SkillLevel, ab.Value) >= need && ab.ExpiresAtUnixMs > now)
                    return true;
            }
            return false;
        }

        ZoneLog.Warn(
            $"skill playerCondition unsupported type={cond.Type} arg={cond.Arg} value={cond.Value} object={player.ObjectId}");
        return false;
    }

    /// <summary>
    /// Mobius Distrust: an attackable non-raid monster is pointed at another visible monster within 1100.
    /// </summary>
    void ApplyDistrust(ZoneEntity caster, ZoneEntity target)
    {
        if (target.Kind != ZoneEntityKind.Npc || !target.Attackable || target.Dead || target.CurHp <= 0)
            return;
        if (target.NpcType is "RaidBoss" or "GrandBoss" || IsTreasureBox(target.NpcId))
            return;
        bool hasSlot = false;
        foreach (var ab in target.Abnormals)
        {
            if (ab.Slot.Equals("DISTRUST", StringComparison.OrdinalIgnoreCase))
            {
                hasSlot = true;
                break;
            }
        }
        if (!hasSlot)
            return;

        var candidates = new List<ZoneEntity>();
        foreach (var entity in _byId.Values)
        {
            if (ReferenceEquals(entity, target) || entity.Kind != ZoneEntityKind.Npc || !entity.Attackable ||
                entity.Dead || entity.CurHp <= 0 || entity.NpcType is "RaidBoss" or "GrandBoss" ||
                IsTreasureBox(entity.NpcId))
                continue;
            if (!ZoneCombat.InCastRange(target.X, target.Y, entity.X, entity.Y, DistrustSearchRange))
                continue;
            if (!VisibleTo(target.ObjectId, entity.ObjectId) &&
                !ZoneCombat.Within(target.X, target.Y, entity.X, entity.Y, DistrustSearchRange))
                continue;
            candidates.Add(entity);
        }
        if (candidates.Count == 0)
            return;
        var pick = candidates[Rng(_combatRandom, target).Next(candidates.Count)];
        AddHate(target, pick, hate: 10_000, allowNpcAttacker: true);
        if (!target.Running)
        {
            target.Running = true;
            FanoutMoveType(target);
        }
    }

    internal void AddElementSeedForTest(ZoneEntity player, string element) =>
        AddElementSeed(player, element, NowUnixMs());

    internal int ElementSeedCountForTest(ZoneEntity player, string element) =>
        ElementSeedCount(player, element, NowUnixMs());
}
