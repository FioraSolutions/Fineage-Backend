using Fineage.Accounts;
using Fineage.Game.Skills;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    /// <summary>
    /// Mobius <c>storeEffect</c>: persist restorable abnormals (remaining seconds frozen offline)
    /// and skill reuse timestamps (systime, which continues while logged out).
    /// </summary>
    void SaveEffects(ZoneSession session, ZoneEntity entity)
    {
        if (entity.Kind != ZoneEntityKind.Player)
            return;
        long now = _clock().ToUnixTimeMilliseconds();
        var rows = new List<StoredEffect>();
        var storedSkillIds = new HashSet<int>();

        foreach (var ab in entity.Abnormals)
        {
            int rem = ZoneAbnormals.RemainingSeconds(now, ab);
            if (rem <= 0 || ab.Id <= 0)
                continue;
            var template = _game.Skills.Find(ab.Id);
            if (!SkillPersistRules.CanStoreEffect(template, ab.Id, _herbs.IsHerbSkill(ab.Id)))
                continue;
            int level = ab.SkillLevel > 0 ? ab.SkillLevel : Math.Max(1, ab.Value);
            long reuseDelay = 0;
            long systime = 0;
            if (entity.SkillReuseUntilMs.TryGetValue(ab.Id, out long until) && until > now)
            {
                reuseDelay = until - now;
                systime = until;
            }

            rows.Add(new StoredEffect(ab.Id, level, rem, reuseDelay, systime, RestoreType: 0));
            storedSkillIds.Add(ab.Id);
        }

        foreach (var (skillId, until) in entity.SkillReuseUntilMs)
        {
            if (until <= now || skillId <= 0 || storedSkillIds.Contains(skillId))
                continue;
            int level = 1;
            if (entity.LearnedSkills.TryGetValue(skillId, out int learned) && learned > 0)
                level = learned;
            else if (_game.Skills.Find(skillId) != null)
                level = 1;
            rows.Add(new StoredEffect(skillId, level, RemainingSeconds: -1, until - now, until, RestoreType: 1));
            storedSkillIds.Add(skillId);
        }

        _persist.ReplaceEffects(session.Account, session.ObjectId, rows);
    }

    /// <summary>
    /// Mobius <c>restoreEffects</c>: re-apply saved abnormals with the stored remaining time
    /// (offline time does not shrink buffs) and re-arm reuse from systime (offline time does).
    /// </summary>
    void RestoreStoredEffects(ZoneEntity entity, CharacterRecord? stored)
    {
        if (entity.Kind != ZoneEntityKind.Player || stored?.Effects == null || stored.Effects.Count == 0)
            return;
        long now = _clock().ToUnixTimeMilliseconds();
        foreach (var e in stored.Effects)
        {
            if (e == null || !e.IsValid)
                continue;

            if (e.IsReuse && e.SystimeMs > now + 10)
                entity.SkillReuseUntilMs[e.SkillId] = e.SystimeMs;

            if (!e.IsEffect)
                continue;
            var template = _game.Skills.Find(e.SkillId);
            if (!SkillPersistRules.CanStoreEffect(template, e.SkillId, _herbs.IsHerbSkill(e.SkillId)))
                continue;
            string slot = template?.AbnormalType ?? "";
            ZoneAbnormals.Apply(
                entity.Abnormals,
                e.SkillId,
                value: e.Level,
                durationSeconds: e.RemainingSeconds,
                nowUnixMs: now,
                skillLevel: e.Level,
                slot: slot);
        }

        if (entity.Abnormals.Count > 0)
            _withAbnormals.Add(entity.ObjectId);
        RefreshRunSpeed(entity);
    }
}
