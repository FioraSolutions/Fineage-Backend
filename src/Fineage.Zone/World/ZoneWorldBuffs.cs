using Fineage.Protocol;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public Task<string?> TryBuffRequestAsync(
        ZoneSession session, int targetObjectId, int effectId, bool remove, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");

        int targetId = targetObjectId > 0 ? targetObjectId : session.ObjectId;
        string? error;
        lock (_gate)
            error = TryBuffRequest(session, targetId, effectId, remove);
        if (error != null)
            return Task.FromResult<string?>(error);

        return Task.FromResult<string?>(null);
    }

    string? TryBuffRequest(ZoneSession session, int targetId, int effectId, bool remove)
    {
        if (!_byId.TryGetValue(targetId, out var target))
            return "target";
        if (target.CurHp <= 0 && target.Kind == ZoneEntityKind.Npc)
            return "target";
        if (targetId != session.ObjectId && !VisibleTo(session.ObjectId, targetId))
            return "target";

        long now = _clock().ToUnixTimeMilliseconds();
        if (remove)
        {
            if (effectId <= 0)
                target.Abnormals.Clear();
            else
            {
                for (int i = target.Abnormals.Count - 1; i >= 0; i--)
                {
                    if (target.Abnormals[i].Id == effectId)
                        target.Abnormals.RemoveAt(i);
                }
            }
        }
        else
        {
            int resolvedEffectId = effectId > 0 ? effectId : ZoneBuffs.SampleWindWalk;
            int value = ResolveAbnormalValue(resolvedEffectId, ZoneBuffs.SampleEffectValue);
            int duration = ResolveAbnormalDurationSeconds(resolvedEffectId, ZoneBuffs.SampleDurationSeconds);
            ApplyAbnormalPlaytest(target, resolvedEffectId, value, duration, now);
            ZoneLog.Info($"buff object={targetId} count={target.Abnormals.Count} remove={remove}");
            return null;
        }

        FanoutAbnormal(target);
        ZoneLog.Info($"buff object={targetId} count={target.Abnormals.Count} remove={remove}");
        return null;
    }
}
