using Fineage.Protocol;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public async Task<string?> TryAdminSpoilAsync(ZoneSession session, AdminSpoilTargetMsg request, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";

        var outbound = new List<Pending>();
        string? error;
        lock (_gate)
            error = TryAdminSpoil(session, request.TargetObjectId, outbound);
        if (error != null)
            return error;

        await FlushSelfAsync(session, outbound, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> TryAdminSweepAsync(ZoneSession session, AdminSweepCorpseMsg request, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";

        var outbound = new List<Pending>();
        string? error;
        lock (_gate)
            error = TryAdminSweep(session, request.TargetObjectId, outbound);
        if (error != null)
            return error;

        await FlushSelfAsync(session, outbound, ct).ConfigureAwait(false);
        return null;
    }

    string? TryAdminSpoil(ZoneSession session, int targetObjectId, List<Pending> outbound)
    {
        if (!_byId.TryGetValue(session.ObjectId, out _))
            return "world";
        if (targetObjectId <= 0)
            return "bad_request";
        if (!_byId.TryGetValue(targetObjectId, out var target))
            return "target";
        if (target.Kind != ZoneEntityKind.Npc || target.CurHp <= 0 || !target.Attackable)
            return "target";
        if (target.SpoilerObjectId != 0)
            return "target";
        if (!VisibleTo(session.ObjectId, targetObjectId))
            return "target";

        target.SpoilerObjectId = session.ObjectId;
        ZoneLog.Info($"spoil object={targetObjectId} spoiler={session.ObjectId}");
        return null;
    }

    string? TryAdminSweep(ZoneSession session, int targetObjectId, List<Pending> outbound)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var player) || player.Inventory == null)
            return "world";
        if (targetObjectId <= 0)
            return "bad_request";
        if (!_byId.TryGetValue(targetObjectId, out var target))
            return "sweep";
        if (target.Kind != ZoneEntityKind.Npc || !target.CorpseAwaitingSweep || target.SweepItems.Count == 0)
            return "sweep";
        if (target.SpoilerObjectId != session.ObjectId)
            return "sweep";
        if (!VisibleTo(session.ObjectId, targetObjectId))
            return "sweep";

        foreach (var stack in target.SweepItems)
        {
            var added = player.Inventory.Add(stack.ItemId, stack.Count);
            outbound.Add(Pack(session, MsgType.InventoryUpdate,
                InventoryMessages.Update(InventoryChange.Add, added)));
        }

        DespawnNpc(target, outbound);
        ZoneLog.Info($"sweep object={targetObjectId} spoiler={session.ObjectId}");
        return null;
    }
}
