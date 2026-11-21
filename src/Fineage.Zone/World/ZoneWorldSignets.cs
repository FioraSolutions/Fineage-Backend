using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Npcs;

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
            if (_npcAiSupport.Get(signet.NpcId)?.Buff is not { Count: > 0 } buffs)
                continue;
            foreach (var skill in buffs)
                PulseSignetAura(signet, skill, now);
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
        ZoneLog.Info($"[signet] pulse {skill.SkillId} lv{skill.Level} npc={signet.NpcId} range={range} " +
                     $"hit={targets.Count}");
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
