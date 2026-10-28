using Fineage.Protocol;
using MessagePack;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public async Task<string?> TryReviveAsync(ZoneSession session, ReviveRequestMsg request, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";

        var toSelf = new List<Pending>();
        var toOthers = new List<Pending>();
        string? error;
        lock (_gate)
            error = TryRevive(session, request, toSelf, toOthers);
        if (error != null)
            return error;

        await FlushSelfAsync(session, toSelf, ct).ConfigureAwait(false);
        await FlushOthersAsync(toOthers, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> TryAdminKillPlayerAsync(ZoneSession session, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";

        var outbound = new List<Pending>();
        string? error;
        lock (_gate)
            error = TryAdminKillPlayer(session, outbound);
        if (error != null)
            return error;

        await FlushSelfAsync(session, outbound, ct).ConfigureAwait(false);
        return null;
    }

    string? TryAdminKillPlayer(ZoneSession session, List<Pending> outbound)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var player) || player.Session == null)
            return "world";
        if (player.Dead)
            return DeathProtocol.AlreadyDead;

        ApplyPlayerDeath(player, killerObjectId: 0, outbound);
        ZoneLog.Info($"death object={player.ObjectId} admin_kill");
        return null;
    }

    string? TryRevive(ZoneSession session, ReviveRequestMsg request, List<Pending> toSelf, List<Pending> toOthers)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var player) || player.Session == null)
            return "world";
        if (!player.Dead)
            return DeathProtocol.NotDead;
        if (request.Action != DeathProtocol.ReviveToVillage)
            return DeathProtocol.Unsupported;

        ReviveAtVillage(player, toSelf, toOthers);
        ZoneLog.Info($"revive object={player.ObjectId} -> {TalkingIslandRestart.X},{TalkingIslandRestart.Y},{TalkingIslandRestart.Z}");
        return null;
    }

    void ApplyPlayerDeath(ZoneEntity player, int killerObjectId, List<Pending> outbound)
    {
        if (player.Kind != ZoneEntityKind.Player || player.Dead)
            return;

        player.Dead = true;
        player.CurHp = 0;
        player.Sitting = false;
        player.Running = false;
        player.ActiveEnchantObjectId = 0;
        player.Abnormals.Clear();
        player.CombatUntilUnixMs = 0;

        if (player.Session != null)
            player.Session.SelectedTargetId = 0;

        FanoutPlayerDeath(player, killerObjectId, outbound);
        if (player.Session != null)
            SaveVitals(player.Session, player);
    }

    void ReviveAtVillage(ZoneEntity player, List<Pending> toSelf, List<Pending> toOthers)
    {
        player.Dead = false;
        player.CurHp = player.MaxHp;
        player.CurMp = player.MaxMp;
        player.Running = true;

        var revive = new ReviveMsg { ObjectId = player.ObjectId };
        var teleport = TeleportMsg(player.ObjectId, TalkingIslandRestart.X, TalkingIslandRestart.Y, TalkingIslandRestart.Z,
            TalkingIslandRestart.Heading);

        FanoutDeathEvent(player, MsgType.Revive, revive, toSelf, toOthers);
        Relocate(player, TalkingIslandRestart.X, TalkingIslandRestart.Y, TalkingIslandRestart.Z, toSelf, toOthers);
        FanoutDeathEvent(player, MsgType.TeleportToLocation, teleport, toSelf, toOthers);

        if (player.Session != null)
        {
            toSelf.Add(Pack(player.Session, MsgType.UserInfo, UserInfo(player)));
            FanoutVitals(player, PlayerVitalsStatus(player), toSelf);
            SaveVitals(player.Session, player);
        }
    }

    void FanoutPlayerDeath(ZoneEntity dead, int killerObjectId, List<Pending> outbound)
    {
        var die = new DieMsg { ObjectId = dead.ObjectId, KillerObjectId = killerObjectId };
        byte[] dieBody = MessagePackSerializer.Serialize(die);
        var status = new StatusUpdateMsg
        {
            ObjectId = dead.ObjectId,
            AttributeIds = [InterludeStatus.CurHp, InterludeStatus.MaxHp],
            Values = [0, dead.MaxHp]
        };
        byte[] statusBody = MessagePackSerializer.Serialize(status);

        foreach (var watcher in Players())
        {
            if (watcher.Session == null)
                continue;
            if (!ReferenceEquals(watcher, dead) && !watcher.Knownlist.Contains(dead.ObjectId))
                continue;

            outbound.Add(new Pending(watcher.Session, MsgType.Die, dieBody));
            outbound.Add(new Pending(watcher.Session, MsgType.StatusUpdate, statusBody));
        }
    }

    void FanoutDeathEvent(ZoneEntity actor, MsgType type, object payload, List<Pending> toSelf, List<Pending> toOthers)
    {
        byte[] body = MessagePackSerializer.Serialize(payload);
        var session = actor.Session;
        if (session != null)
            toSelf.Add(new Pending(session, type, body));

        foreach (var watcher in Players())
        {
            if (watcher.Session == null || ReferenceEquals(watcher, actor))
                continue;
            if (!watcher.Knownlist.Contains(actor.ObjectId))
                continue;
            toOthers.Add(new Pending(watcher.Session, type, body));
        }
    }

    static TeleportToLocationMsg TeleportMsg(int objectId, int x, int y, int z, int heading) => new()
    {
        ObjectId = objectId,
        X = x,
        Y = y,
        Z = z,
        Heading = heading
    };
}
