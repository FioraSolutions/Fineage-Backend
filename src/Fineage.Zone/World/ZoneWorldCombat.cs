using Fineage.Protocol;
using Fineage.Zone.Combat;
using MessagePack;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public async Task<string?> TrySelectTargetAsync(ZoneSession session, SelectTargetMsg select, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";

        var toSelf = new List<Pending>();
        string? error;
        lock (_gate)
            error = TrySelectTarget(session, select.TargetObjectId, toSelf);
        if (error != null)
            return error;

        await FlushSelfAsync(session, toSelf, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> TryAttackAsync(ZoneSession session, AttackRequestMsg request, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";

        var outbound = new List<Pending>();
        string? error;
        lock (_gate)
            error = TryAttack(session, request.TargetObjectId, outbound);
        if (error != null)
            return error;

        await FlushSelfAsync(session, outbound, ct).ConfigureAwait(false);
        return null;
    }

    string? TrySelectTarget(ZoneSession session, int targetObjectId, List<Pending> toSelf)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var player))
            return "world";
        if (!_byId.TryGetValue(targetObjectId, out var target))
            return "target";
        if (target.Kind != ZoneEntityKind.Npc || !target.Attackable || target.CurHp <= 0)
            return "target";
        if (!VisibleTo(session.ObjectId, targetObjectId))
            return "target";

        session.SelectedTargetId = targetObjectId;
        toSelf.Add(Pack(session, MsgType.MyTargetSelected, new MyTargetSelectedMsg
        {
            ObjectId = targetObjectId,
            ColorHint = InterludeStatus.TargetColorHostile
        }));
        return null;
    }

    string? TryAttack(ZoneSession session, int requestedTargetId, List<Pending> outbound)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var player) || player.Session == null)
            return "world";

        int targetId = requestedTargetId > 0 ? requestedTargetId : session.SelectedTargetId;
        if (targetId <= 0)
            return "attack";
        if (requestedTargetId > 0)
            session.SelectedTargetId = targetId;

        if (!_byId.TryGetValue(targetId, out var target))
            return "attack";
        if (target.Kind != ZoneEntityKind.Npc || !target.Attackable || target.CurHp <= 0)
            return "attack";
        if (!VisibleTo(player.ObjectId, targetId))
            return "attack";
        if (!ZoneCombat.InMeleeRange(player.X, player.Y, target.X, target.Y))
            return "attack";

        int damage = ZoneCombat.SampleMeleeDamage;
        target.CurHp = Math.Max(0, target.CurHp - damage);

        var attack = new AttackMsg
        {
            AttackerObjectId = player.ObjectId,
            TargetObjectId = targetId,
            Damage = damage,
            AttackerX = player.X,
            AttackerY = player.Y,
            AttackerZ = player.Z,
            TargetX = target.X,
            TargetY = target.Y,
            TargetZ = target.Z
        };
        var status = StatusFor(target);

        FanoutCombat(player, target, attack, status, outbound);

        if (target.CurHp <= 0)
            RemoveDeadNpc(target, outbound);

        return null;
    }

    static StatusUpdateMsg StatusFor(ZoneEntity entity) => new()
    {
        ObjectId = entity.ObjectId,
        AttributeIds = [InterludeStatus.CurHp, InterludeStatus.MaxHp],
        Values = [entity.CurHp, entity.MaxHp]
    };

    void FanoutCombat(ZoneEntity attacker, ZoneEntity target, AttackMsg attack, StatusUpdateMsg status, List<Pending> outbound)
    {
        byte[] attackBody = MessagePackSerializer.Serialize(attack);
        byte[] statusBody = MessagePackSerializer.Serialize(status);

        foreach (var watcher in Players())
        {
            if (watcher.Session == null)
                continue;
            bool cares = ReferenceEquals(watcher, attacker) ||
                         watcher.Knownlist.Contains(target.ObjectId) ||
                         watcher.Knownlist.Contains(attacker.ObjectId);
            if (!cares)
                continue;

            outbound.Add(new Pending(watcher.Session, MsgType.Attack, attackBody));
            outbound.Add(new Pending(watcher.Session, MsgType.StatusUpdate, statusBody));
        }
    }

    void RemoveDeadNpc(ZoneEntity npc, List<Pending> outbound)
    {
        int objectId = npc.ObjectId;
        int x = npc.X;
        int y = npc.Y;
        int z = npc.Z;

        var watchers = new List<ZoneEntity>();
        foreach (var player in Players())
        {
            if (player.Knownlist.Contains(objectId))
                watchers.Add(player);
        }

        _aoi.Leave(objectId);
        _byId.Remove(objectId);
        ScheduleRespawn(objectId);

        byte[] deleteBody = MessagePackSerializer.Serialize(new DeleteObjectMsg { ObjectId = objectId });
        foreach (var player in Players())
        {
            if (player.Session == null)
                continue;
            if (player.Knownlist.Remove(objectId))
                outbound.Add(new Pending(player.Session, MsgType.DeleteObject, deleteBody));
            if (player.Session.SelectedTargetId == objectId)
                player.Session.SelectedTargetId = 0;
        }

        SpawnGremlinDrop(x, y, z, watchers, outbound);
    }

    bool VisibleTo(int observerId, int otherId)
    {
        foreach (int id in _aoi.GetVisible(observerId))
        {
            if (id == otherId)
                return true;
        }

        return false;
    }
}
