using Fineage.Game.Skills;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Npcs;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius <c>EffectPoint</c> signets: the Battle Force symbols (454–460) and their cousins plant an invulnerable NPC
/// that keeps re-applying one AURA buff (5123–5129) to everyone standing inside <c>affectRange</c>.
/// <para>
/// Mobius itself leaves this half-done — <c>SummonNpc.java</c> carries
/// <c>case "EffectPoint": // TODO: Implement proper signet skills.</c> — so the aura cadence comes from the template:
/// the signet NPCs declare <c>&lt;param name="skill_delay"&gt;</c> (5s) and their skill list holds the AURA buff with
/// its <c>affectRange</c>. That is what runs here.
/// </para>
/// </summary>
public sealed partial class ZoneWorld
{
    readonly HashSet<int> _signets = [];

    internal bool IsSignet(ZoneEntity npc) => _signets.Contains(npc.ObjectId);

    /// <summary>
    /// Signet aura pulse. A signet has no hate target, so <see cref="TickMobAttacks"/> never reaches it; its own timer
    /// fires <c>skill_delay</c> seconds after the plant and then keeps repeating while the NPC lives.
    /// </summary>
    void TickSignets(long now)
    {
        if (_signets.Count == 0)
            return;
        var catalog = NpcCatalog.LoadDefault();
        foreach (int id in _signets.ToArray())
        {
            if (!_byId.TryGetValue(id, out var signet) || signet.Dead || signet.CurHp <= 0)
            {
                _signets.Remove(id);
                continue;
            }
            if (!catalog.ById.TryGetValue(signet.NpcId, out var template))
                continue;
            int delay = template.SkillDelaySeconds > 0 ? template.SkillDelaySeconds : 5;
            if (signet.LastAiTickMs > 0 && now < signet.LastAiTickMs + delay * 1000L)
                continue;
            signet.LastAiTickMs = now;
            var support = _npcAiSupport.Get(signet.NpcId);
            if (support == null)
            {
                ZoneHotPathLogs.SignetMissingAiSupport(signet.NpcId);
                continue;
            }
            if (support.Value.Buff is { Count: > 0 } buffs)
            {
                foreach (var skill in buffs)
                    PulseSignetAura(signet, skill, now);
            }
            foreach (var skill in EnumerateSignetHostileSkills(support.Value))
                PulseSignetHostile(signet, skill, now);
        }
    }

    static IEnumerable<NpcAiSupportSkill> EnumerateSignetHostileSkills(NpcAiSupportCatalog.NpcAiSupportRow row)
    {
        if (row.Debuff is { Count: > 0 } debuffs)
        {
            foreach (var skill in debuffs)
                yield return skill;
        }
        if (row.Immobilize is { Count: > 0 } immobilize)
        {
            foreach (var skill in immobilize)
                yield return skill;
        }
        if (row.Silence is { Count: > 0 } silence)
        {
            foreach (var skill in silence)
                yield return skill;
        }
    }

    void PulseSignetAura(ZoneEntity signet, NpcAiSupportSkill skill, long now)
    {
        if (skill.AbnormalTimeSeconds <= 0)
            return;
        int range = skill.AffectRange > 0 ? skill.AffectRange : 200;
        var targets = new List<ZoneEntity>(4) { signet };
        foreach (var entity in Players())
        {
            if (entity.Session == null || entity.Dead || entity.CurHp <= 0)
                continue;
            if (!SameInstance(signet.ObjectId, entity.ObjectId))
                continue;
            // Mobius canAOE(AURA): everyone visible inside affectRange, attackable NPCs skipped (a signet never
            // buffs monsters that are fighting).
            if (entity.ObjectId == signet.ObjectId ||
                ZoneCombat.InCastRange(signet.X, signet.Y, entity.X, entity.Y, range))
                targets.Add(entity);
        }

        FanoutNpcMagicSkillUse(signet, signet, skill.SkillId, skill.Level, 0, 0);
        int abnormal = skill.AbnormalLevel > 0 ? skill.AbnormalLevel : skill.Level;
        string slot = skill.Slot;
        if (slot.Length == 0)
            slot = _game.Skills.Find(skill.SkillId)?.AbnormalType ?? "";
        foreach (var target in targets)
        {
            ZoneAbnormals.Apply(target.Abnormals, skill.SkillId, abnormal, skill.AbnormalTimeSeconds, now,
                skill.Level, slot);
            FanoutAbnormal(target);
        }
        ZoneHotPathLogs.SignetPulse(skill.SkillId, skill.Level, signet.NpcId, range, targets.Count);
    }

    /// <summary>Mobius EffectPoint debuff scopes: pulse debuffs / damage on attackable NPCs inside <c>affectRange</c>.</summary>
    void PulseSignetHostile(ZoneEntity signet, NpcAiSupportSkill skill, long now)
    {
        var owner = QuestSpawnOwner(signet);
        if (owner == null)
            return;
        var template = _game.Skills.Find(skill.SkillId);
        SkillLevelData? levelData = null;
        if (template != null)
        {
            if (!template.TryLevelData(skill.Level, out var found))
            {
                ZoneHotPathLogs.SignetMissingSkillLevel(skill.SkillId, skill.Level, signet.NpcId);
                return;
            }
            levelData = found;
        }

        bool catalogAbnormal = template is { AppliesAbnormal: true } && levelData != null;
        bool supportAbnormal = skill.AbnormalTimeSeconds > 0;
        if (!catalogAbnormal && !supportAbnormal)
        {
            if (template == null)
                ZoneHotPathLogs.SignetMissingSkillCatalog(skill.SkillId, signet.NpcId);
            return;
        }

        int range = skill.AffectRange > 0 ? skill.AffectRange : 200;
        FanoutNpcMagicSkillUse(signet, signet, skill.SkillId, skill.Level, 0, 0);
        int hits = 0;
        foreach (var entity in _byId.Values.ToList())
        {
            if (entity.Dead || entity.CurHp <= 0)
                continue;
            if (entity.ObjectId == signet.ObjectId || entity.ObjectId == owner.ObjectId)
                continue;
            if (!SameInstance(signet.ObjectId, entity.ObjectId))
                continue;
            if (!ZoneCombat.InCastRange(signet.X, signet.Y, entity.X, entity.Y, range))
                continue;
            if (entity.Kind == ZoneEntityKind.Npc && !entity.Attackable)
                continue;

            if (skill.Power > 0 && entity.Kind == ZoneEntityKind.Npc && levelData != null)
            {
                double mAtk = Math.Max(1d, owner.Kind == ZoneEntityKind.Player ? PlayerStats(owner).MAtk : owner.MAtk);
                double mDef = entity.MDef > 0 ? entity.MDef : entity.Level * 30d;
                var (damage, _) = ZoneCombat.RollMagicDamage(
                    Rng(_combatRandom, entity), mAtk, Math.Max(1d, mDef), skill.Power,
                    owner.Level, magicLevel: levelData.MagicLevel > 0 ? levelData.MagicLevel : owner.Level,
                    entity.Level, mCritRate: 5);
                if (damage > 0)
                    DamageNpcBy(owner, entity, damage, now);
            }

            if (catalogAbnormal && template != null && levelData != null &&
                ApplyAbnormalFromSkill(template, levelData, entity, now, attacker: owner))
                hits++;
            else if (supportAbnormal && ApplySignetSupportAbnormal(skill, entity, now, template?.AbnormalType))
                hits++;
        }
        ZoneHotPathLogs.SignetHostilePulse(skill.SkillId, skill.Level, signet.NpcId, range, hits);
    }

    /// <summary>npc-ai-support debuff pulses when the skill id is not in <c>skills.json</c> yet.</summary>
    bool ApplySignetSupportAbnormal(NpcAiSupportSkill skill, ZoneEntity target, long now, string? abnormalType)
    {
        int abnormal = skill.AbnormalLevel > 0 ? skill.AbnormalLevel : skill.Level;
        string slot = skill.Slot.Length > 0 ? skill.Slot : abnormalType ?? "";
        if (!ZoneAbnormals.Apply(
                target.Abnormals, skill.SkillId, abnormal, skill.AbnormalTimeSeconds, now, skill.Level, slot))
            return false;
        if (target.Abnormals.Count > 0)
            _byId.Join(target, ActiveSets.WithAbnormals);
        FanoutAbnormal(target);
        return true;
    }

    /// <summary>Test hook: run the signet aura timers now.</summary>
    internal void TickSignetsForTest(long? now = null)
    {
        using (_affinity.Step())
            TickSignets(now ?? NowUnixMs());
    }

    /// <summary>Test hook: every live NPC in the world (signets included).</summary>
    internal List<ZoneEntity> SpawnedNpcsForTest()
    {
        using (_affinity.Step())
            return _byId.Values.Where(e => e.Kind == ZoneEntityKind.Npc).ToList();
    }
}
