using Fineage.Protocol;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public async Task<string?> TryBuffRequestAsync(ZoneSession session, BuffRequestMsg request, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";

        int targetId = request.TargetObjectId > 0 ? request.TargetObjectId : session.ObjectId;
        if (request.EffectId <= 0 && !request.Remove)
            return "bad_request";

        var outbound = new List<Pending>();
        string? error;
        lock (_gate)
            error = TryBuffRequest(session, targetId, request, outbound);
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
        if (!VisibleTo(session.ObjectId, targetId))
            return "target";

        if (request.Remove)
        {
            if (request.EffectId <= 0)
                target.AbnormalEffects.Clear();
            else
                target.AbnormalEffects.RemoveAll(e => e.Id == request.EffectId);
        }
        else
        {
            int effectId = request.EffectId > 0 ? request.EffectId : ZoneBuffs.SampleWindWalk;
            var effect = new AbnormalEffectMsg
            {
                Id = effectId,
                Value = ZoneBuffs.SampleEffectValue,
                DurationSeconds = ZoneBuffs.SampleDurationSeconds
            };
            int index = target.AbnormalEffects.FindIndex(e => e.Id == effectId);
            if (index >= 0)
                target.AbnormalEffects[index] = effect;
            else
                target.AbnormalEffects.Add(effect);
        }

        FanoutAbnormal(target, outbound);
        ZoneLog.Info($"buff object={targetId} count={target.AbnormalEffects.Count} remove={request.Remove}");
        return null;
    }

    void FanoutAbnormal(ZoneEntity target, List<Pending> outbound)
    {
        var update = AbnormalFor(target);
        foreach (var watcher in Players())
        {
            if (watcher.Session == null)
                continue;
            bool cares = ReferenceEquals(watcher, target) || watcher.Knownlist.Contains(target.ObjectId);
            if (!cares)
                continue;
            outbound.Add(Pack(watcher.Session, MsgType.AbnormalStatusUpdate, update));
        }
    }

    static AbnormalStatusUpdateMsg AbnormalFor(ZoneEntity entity) => new()
    {
        ObjectId = entity.ObjectId,
        Effects = entity.AbnormalEffects.ToArray()
    };
}
