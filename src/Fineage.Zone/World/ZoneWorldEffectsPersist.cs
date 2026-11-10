using Fineage.Accounts;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    /// <summary>Mobius storeEffect: persist remaining abnormal seconds on leave.</summary>
    void SaveEffects(ZoneSession session, ZoneEntity entity)
    {
        if (entity.Kind != ZoneEntityKind.Player)
            return;
        long now = _clock().ToUnixTimeMilliseconds();
        var rows = new List<StoredEffect>();
        foreach (var ab in entity.Abnormals)
        {
            int rem = ZoneAbnormals.RemainingSeconds(now, ab);
            if (rem <= 0 || ab.Id <= 0)
                continue;
            int level = ab.SkillLevel > 0 ? ab.SkillLevel : Math.Max(1, ab.Value);
            rows.Add(new StoredEffect(ab.Id, level, rem));
        }

        _persist.ReplaceEffects(session.Account, session.ObjectId, rows);
    }

    /// <summary>Mobius restoreEffects: re-apply saved abnormals on enter.</summary>
    void RestoreStoredEffects(ZoneEntity entity, CharacterRecord? stored)
    {
        if (entity.Kind != ZoneEntityKind.Player || stored?.Effects == null || stored.Effects.Count == 0)
            return;
        long now = _clock().ToUnixTimeMilliseconds();
        foreach (var e in stored.Effects)
        {
            if (e == null || !e.IsValid)
                continue;
            string slot = _game.Skills.Find(e.SkillId)?.AbnormalType ?? "";
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
