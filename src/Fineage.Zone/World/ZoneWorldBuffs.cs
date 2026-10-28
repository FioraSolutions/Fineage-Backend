using Fineage.Protocol;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public async Task<string?> TryBuffRequestAsync(ZoneSession session, BuffRequestMsg request, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";

        var outbound = new List<Pending>();
        string? error;
        lock (_gate)
            error = TryBuffRequest(session, targetId: request.TargetObjectId > 0 ? request.TargetObjectId : session.ObjectId, request, outbound);
        if (error != null)
            return error;

        await FlushSelfAsync(session, outbound, ct).ConfigureAwait(false);
        return null;
    }

    string? TryBuffRequest(ZoneSession session, int targetId, BuffRequestMsg request, List<Pending> outbound)
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
            ApplyAbnormalPlaytest(target, effectId, value, duration, now, outbound);
            ZoneLog.Info($"buff object={targetId} count={target.Abnormals.Count} remove={request.Remove}");
            return null;
        }

        FanoutAbnormal(target, outbound);
        ZoneLog.Info($"buff object={targetId} count={target.Abnormals.Count} remove={request.Remove}");
        return null;
    }
}
