using Fineage.Protocol;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public Task<string?> TryBuffRequestAsync(ZoneSession session, BuffRequestMsg request, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");

        string? error;
        lock (_gate)
            error = TryBuffRequest(session, targetId: request.TargetObjectId > 0 ? request.TargetObjectId : session.ObjectId, request);
        if (error != null)
            return Task.FromResult<string?>(error);

        return Task.FromResult<string?>(null);
    }

    string? TryBuffRequest(ZoneSession session, int targetId, BuffRequestMsg request)
    {
        if (!_byId.TryGetValue(targetId, out var target))
            return "target";
        if (target.CurHp <= 0 && target.Kind == ZoneEntityKind.Npc)
            return "target";
        if (targetId != session.ObjectId && !VisibleTo(session.ObjectId, targetId))
            return "target";

        long now = _clock().ToUnixTimeMilliseconds();
        if (request.Remove)
        {
            if (request.EffectId <= 0)
                target.Abnormals.Clear();
            else
            {
                for (int i = target.Abnormals.Count - 1; i >= 0; i--)
                {
                    if (target.Abnormals[i].Id == request.EffectId)
                        target.Abnormals.RemoveAt(i);
                }
            }
        }
        else
        {
            int effectId = request.EffectId > 0 ? request.EffectId : ZoneBuffs.SampleWindWalk;
            int value = ResolveAbnormalValue(effectId, ZoneBuffs.SampleEffectValue);
            int duration = ResolveAbnormalDurationSeconds(effectId, ZoneBuffs.SampleDurationSeconds);
            ApplyAbnormalPlaytest(target, effectId, value, duration, now);
            ZoneLog.Info($"buff object={targetId} count={target.Abnormals.Count} remove={request.Remove}");
            return null;
        }

        FanoutAbnormal(target);
        ZoneLog.Info($"buff object={targetId} count={target.Abnormals.Count} remove={request.Remove}");
        return null;
    }
}
