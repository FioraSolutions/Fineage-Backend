using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Drops;
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
        {
            error = TryAttack(session, request.TargetObjectId, outbound);
            if (error == null)
            {
                session.AutoAttackTargetId = session.SelectedTargetId;
                session.NextAutoAttackUnixMs = _clock().ToUnixTimeMilliseconds() + ZoneCombat.AutoAttackIntervalMs;
            }
        }
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

        // Interlude: click own pawn → target self (AOI never lists the observer).
        if (targetObjectId == session.ObjectId)
        {
            if (player.Dead || player.CurHp <= 0)
                return "target";

            session.SelectedTargetId = targetObjectId;
            toSelf.Add(Pack(session, MsgType.MyTargetSelected, new MyTargetSelectedMsg
            {
                ObjectId = targetObjectId,
                ColorHint = 0
            }));
            return null;
        }

        // Friendly NPCs (guides, merchants) are selectable for talk UI; Attackable is attack-only.
        if (target.Kind != ZoneEntityKind.Npc || target.CurHp <= 0)
            return "target";
        if (!VisibleTo(session.ObjectId, targetObjectId))
            return "target";

        session.SelectedTargetId = targetObjectId;
        toSelf.Add(Pack(session, MsgType.MyTargetSelected, new MyTargetSelectedMsg
        {
            ObjectId = targetObjectId,
            ColorHint = InterludeStatus.TargetColor(player.Level, target.Level)
        }));
        return null;
    }

    string? TryAttack(ZoneSession session, int requestedTargetId, List<Pending> outbound)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var player) || player.Session == null)
            return "world";
        if (player.Dead)
            return InventoryProtocol.Dead;
        if (player.CastingUntilMs > _clock().ToUnixTimeMilliseconds())
            return null; // Mobius: auto-attack waits while casting

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

        // Swing now; HP drops at hitTime (Mobius onHitTimer).
        FanoutAttack(player, target, attack, outbound);
        long now = _clock().ToUnixTimeMilliseconds();
        player.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        _meleeHits.Add(new PendingMeleeHit(player.ObjectId, targetId, damage, now + ZoneCombat.PlayerMeleeHitDelayMs));

        return null;
    }

    Task? _combatLoop;

    async Task CombatLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(ZoneCombat.AutoAttackTickMs));
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                var outbound = new List<Pending>();
                lock (_gate)
                {
                    long now = _clock().ToUnixTimeMilliseconds();
                    TickPendingHits(now, outbound);
                    TickMeleeHits(now, outbound);
                    TickAutoAttacks(outbound);
                    TickMobAttacks(now, outbound);
                    TickCorpseDecay(now, outbound);
                }
                if (outbound.Count > 0)
                    await FlushOthersAsync(outbound, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    void TickAutoAttacks(List<Pending> outbound)
    {
        long now = _clock().ToUnixTimeMilliseconds();
        foreach (var player in Players().ToList())
        {
            var session = player.Session;
            if (session == null || session.AutoAttackTargetId <= 0 || now < session.NextAutoAttackUnixMs)
                continue;
            int targetId = session.AutoAttackTargetId;
            if (player.Dead || !_byId.TryGetValue(targetId, out var target) || target.CurHp <= 0 || session.SelectedTargetId != targetId)
            {
                session.AutoAttackTargetId = 0;
                continue;
            }
            if (!VisibleTo(player.ObjectId, targetId))
            {
                session.AutoAttackTargetId = 0;
                continue;
            }
            session.NextAutoAttackUnixMs = now + ZoneCombat.AutoAttackIntervalMs;
            if (!ZoneCombat.InMeleeRange(player.X, player.Y, target.X, target.Y))
                continue; // still walking in; keep the intent
            if (TryAttack(session, targetId, outbound) != null)
                session.AutoAttackTargetId = 0;
        }
    }

    static StatusUpdateMsg StatusFor(ZoneEntity entity) => new()
    {
        ObjectId = entity.ObjectId,
        AttributeIds = [InterludeStatus.CurHp, InterludeStatus.MaxHp],
        Values = [entity.CurHp, entity.MaxHp]
    };

    readonly List<PendingMeleeHit> _meleeHits = new();

    void FanoutAttack(ZoneEntity attacker, ZoneEntity target, AttackMsg attack, List<Pending> outbound)
    {
        byte[] attackBody = MessagePackSerializer.Serialize(attack);
        foreach (var watcher in Players())
        {
            if (watcher.Session == null)
                continue;
            bool cares = ReferenceEquals(watcher, attacker) ||
                         watcher.Knownlist.Contains(target.ObjectId) ||
                         watcher.Knownlist.Contains(attacker.ObjectId);
            if (cares)
                outbound.Add(new Pending(watcher.Session, MsgType.Attack, attackBody));
        }
    }

    void TickMeleeHits(long now, List<Pending> outbound)
    {
        if (_meleeHits.Count == 0)
            return;
        var due = _meleeHits.Where(h => h.DueUnixMs <= now).ToList();
        if (due.Count == 0)
            return;
        _meleeHits.RemoveAll(h => h.DueUnixMs <= now);
        foreach (var hit in due)
            ResolveMeleeHit(hit, now, outbound);
    }

    void ResolveMeleeHit(PendingMeleeHit hit, long now, List<Pending> outbound)
    {
        if (!_byId.TryGetValue(hit.AttackerId, out var attacker) || attacker.CurHp <= 0 || attacker.Dead)
            return;
        if (!_byId.TryGetValue(hit.TargetId, out var target) || target.CurHp <= 0 || target.Dead)
            return;

        target.CurHp = Math.Max(0, target.CurHp - hit.Damage);

        if (target.Kind == ZoneEntityKind.Npc)
        {
            FanoutVitals(target, StatusFor(target), outbound);
            AddHate(target, attacker);
            attacker.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
            if (target.CurHp <= 0)
            {
                RemoveDeadNpc(target, outbound, killer: attacker);
                GrantKillReward(attacker, target, outbound);
            }
            return;
        }

        target.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        FanoutVitals(target, PlayerVitalsStatus(target), outbound);
        if (target.CurHp <= 0)
        {
            attacker.AggroTargetId = 0;
            if (target.Session != null)
                target.Session.AutoAttackTargetId = 0;
            ApplyPlayerDeath(target, attacker.ObjectId, outbound);
        }
    }

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

    void RemoveDeadNpc(ZoneEntity npc, List<Pending> outbound, ZoneEntity? killer = null)
    {
        // Kill quest steps are not applied here. IQuestKillHook is the later combat entry.
        int objectId = npc.ObjectId;
        int npcId = npc.NpcId;
        int x = npc.X;
        int y = npc.Y;
        int z = npc.Z;

        var watchers = new List<ZoneEntity>();
        foreach (var player in Players())
        {
            if (player.Knownlist.Contains(objectId))
                watchers.Add(player);
        }

        // Mobius Creature.doDie -> broadcastPacket(new Die(this)): client plays the death animation.
        npc.AggroTargetId = 0;
        npc.Chasing = false;
        byte[] dieBody = MessagePackSerializer.Serialize(new DieMsg { ObjectId = objectId });
        foreach (var watcher in watchers)
        {
            if (watcher.Session == null)
                continue;
            outbound.Add(new Pending(watcher.Session, MsgType.Die, dieBody));
            if (watcher.Session.SelectedTargetId == objectId)
                watcher.Session.AutoAttackTargetId = 0;
        }

        npc.SweepItems.Clear();
        if (npc.SpoilerObjectId != 0 && _dropCatalog.TryGet(npcId, out var definition))
        {
            foreach (var stack in DropRoller.RollCorpse(definition, _dropRandom, _dropRates))
                npc.SweepItems.Add(stack);
        }

        if (npc.SweepItems.Count > 0)
        {
            SpawnDeathDrops(npcId, x, y, z, killer, watchers, outbound);
            npc.CorpseAwaitingSweep = true;
            ZoneLog.Info($"corpse object={objectId} spoiler={npc.SpoilerObjectId} sweep={npc.SweepItems.Count}");
            return;
        }

        SpawnDeathDrops(npcId, x, y, z, killer, watchers, outbound);
        // Corpse lies for DEFAULT_CORPSE_TIME, then DeleteObject (TickCorpseDecay).
        npc.DecayAtMs = _clock().ToUnixTimeMilliseconds() + ZoneCombat.CorpseDecayMs;
    }

    void DespawnNpc(ZoneEntity npc, List<Pending> outbound)
    {
        int objectId = npc.ObjectId;

        _aoi.Leave(objectId);
        _byId.Remove(objectId);
        npc.SpoilerObjectId = 0;
        npc.SweepItems.Clear();
        npc.CorpseAwaitingSweep = false;
        npc.DecayAtMs = 0;
        npc.Chasing = false;
        npc.AggroTargetId = 0;
        npc.LastAiTickMs = 0;
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
    }

    bool VisibleTo(int observerId, int otherId)
    {
        if (!SameInstance(observerId, otherId))
            return false;
        foreach (int id in _aoi.GetVisible(observerId))
        {
            if (id == otherId)
                return true;
        }

        return false;
    }
}
