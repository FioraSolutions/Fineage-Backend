using Fineage.Protocol;
using Fineage.Zone.Combat;
using MessagePack;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius-style hit timing and mob retaliation.
/// Mobius: Creature.onMagicHitTimer applies skill damage at hitTime;
/// Attackable.addDamageHate + AttackableAI sets intention ATTACK on the attacker, the mob swings at Pspd until target dies or leaves.
/// </summary>
public sealed partial class ZoneWorld
{
    internal async Task RunCombatTickAsync(CancellationToken ct)
    {
        var outbound = new List<Pending>();
        lock (_gate)
        {
            long now = _clock().ToUnixTimeMilliseconds();
            TickPendingHits(now, outbound);
            TickMobAttacks(now, outbound);
            TickCorpseDecay(now, outbound);
        }
        await FlushOthersAsync(outbound, ct).ConfigureAwait(false);
    }

    void TickPendingHits(long now, List<Pending> outbound)
    {
        foreach (var player in Players().ToList())
        {
            if (player.PendingHit != null && player.PendingHit.DueUnixMs <= now)
                ResolvePendingHit(player, now, outbound);
        }
    }

    void ResolvePendingHit(ZoneEntity player, long now, List<Pending> outbound)
    {
        var hit = player.PendingHit;
        player.PendingHit = null;
        if (hit == null || player.Dead)
            return;
        if (!_byId.TryGetValue(hit.TargetId, out var target) || target.Kind != ZoneEntityKind.Npc || target.CurHp <= 0)
            return;

        target.CurHp = Math.Max(0, target.CurHp - hit.Damage);
        FanoutVitals(target, StatusFor(target), outbound);
        AddHate(target, player);
        player.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;

        if (target.CurHp <= 0)
        {
            RemoveDeadNpc(target, outbound, killer: player);
            GrantKillReward(player, target, outbound);
        }
    }

    void AddHate(ZoneEntity npc, ZoneEntity attacker)
    {
        if (npc.Kind != ZoneEntityKind.Npc || attacker.Kind != ZoneEntityKind.Player || npc.CurHp <= 0)
            return;
        if (npc.AggroTargetId == attacker.ObjectId)
            return;
        npc.AggroTargetId = attacker.ObjectId;
        // Mobius: first swing after attack-speed delay, not instantly.
        npc.NextMobAttackMs = _clock().ToUnixTimeMilliseconds() + ZoneCombat.MobAttackIntervalMs / 2;
    }

    void TickMobAttacks(long now, List<Pending> outbound)
    {
        foreach (var npc in _byId.Values.Where(e => e.Kind == ZoneEntityKind.Npc && e.AggroTargetId > 0).ToList())
        {
            if (npc.CurHp <= 0 || !_byId.TryGetValue(npc.AggroTargetId, out var player) ||
                player.Kind != ZoneEntityKind.Player || player.Dead || player.Session == null ||
                !SameInstance(npc.ObjectId, player.ObjectId))
            {
                npc.AggroTargetId = 0;
                continue;
            }
            if (!ZoneCombat.InAggroRange(npc.X, npc.Y, player.X, player.Y))
            {
                npc.AggroTargetId = 0; // Mobius: target out of MAX_DRIFT/aggro -> return home, forget
                npc.Chasing = false;
                continue;
            }
            long dtMs = npc.LastAiTickMs > 0 ? Math.Clamp(now - npc.LastAiTickMs, 0, 1000) : ZoneCombat.AutoAttackTickMs;
            npc.LastAiTickMs = now;
            if (!ZoneCombat.InMobMeleeRange(npc.X, npc.Y, player.X, player.Y))
            {
                // Mobius AttackableAI.thinkAttack -> moveToPawn(target, range): mob runs to the attacker.
                ChaseStep(npc, player, dtMs, outbound);
                continue;
            }
            if (npc.Chasing)
            {
                npc.Chasing = false;
                // Arrived: Mobius sends a final MoveToLocation/StopMove at the reached spot.
                BroadcastNpcMove(npc, npc.X, npc.Y, npc.Z, outbound);
            }
            if (now < npc.NextMobAttackMs)
                continue;
            npc.NextMobAttackMs = now + ZoneCombat.MobAttackIntervalMs;

            int damage = ZoneCombat.MobMeleeDamage(npc.Level);
            player.CurHp = Math.Max(0, player.CurHp - damage);
            player.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;

            var attack = new AttackMsg
            {
                AttackerObjectId = npc.ObjectId,
                TargetObjectId = player.ObjectId,
                Damage = damage,
                AttackerX = npc.X, AttackerY = npc.Y, AttackerZ = npc.Z,
                TargetX = player.X, TargetY = player.Y, TargetZ = player.Z
            };
            FanoutCombat(npc, player, attack, PlayerVitalsStatus(player), outbound);

            if (player.CurHp <= 0)
            {
                npc.AggroTargetId = 0;
                if (player.Session != null)
                    player.Session.AutoAttackTargetId = 0;
                ApplyPlayerDeath(player, npc.ObjectId, outbound);
            }
        }
    }

    /// <summary>
    /// One AI tick of chase. Server position advances at NPC run speed; the client gets a
    /// MoveToLocation (MoveToAck) toward the attacker only when the chase starts or the pawn moved,
    /// so it animates a continuous run instead of 5 Hz hops.
    /// </summary>
    void ChaseStep(ZoneEntity npc, ZoneEntity target, long dtMs, List<Pending> outbound)
    {
        double dx = target.X - npc.X;
        double dy = target.Y - npc.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        double stopAt = ZoneCombat.MobMeleeRange - 20;
        if (dist <= stopAt)
            return;

        // Destination: point at stopAt from the target on the npc side (Mobius moveToPawn offset).
        int destX = (int)Math.Round(target.X - dx / dist * stopAt);
        int destY = (int)Math.Round(target.Y - dy / dist * stopAt);
        int destZ = target.Z;

        long ddx = destX - (long)npc.ChaseDestX, ddy = destY - (long)npc.ChaseDestY;
        if (!npc.Chasing || ddx * ddx + ddy * ddy > 40L * 40L)
        {
            npc.Chasing = true;
            npc.ChaseDestX = destX;
            npc.ChaseDestY = destY;
            BroadcastNpcMove(npc, destX, destY, destZ, outbound);
        }

        float speed = npc.RunSpeed > 0 ? npc.RunSpeed : ZoneCombat.DefaultNpcRunSpeed;
        double step = speed * dtMs / 1000.0;
        double remain = dist - stopAt;
        if (step >= remain)
        {
            npc.X = destX;
            npc.Y = destY;
        }
        else
        {
            npc.X = (int)Math.Round(npc.X + dx / dist * step);
            npc.Y = (int)Math.Round(npc.Y + dy / dist * step);
        }
        npc.Z = destZ;
        _aoi.Move(npc.ObjectId, npc.X, npc.Y);
    }

    void BroadcastNpcMove(ZoneEntity npc, int destX, int destY, int destZ, List<Pending> outbound)
    {
        byte[] body = MessagePackSerializer.Serialize(new MoveToMsg
        {
            ObjectId = npc.ObjectId,
            DestX = destX, DestY = destY, DestZ = destZ,
            OriginX = npc.X, OriginY = npc.Y, OriginZ = npc.Z
        });
        foreach (var watcher in Players())
        {
            if (watcher.Session != null && watcher.Knownlist.Contains(npc.ObjectId))
                outbound.Add(new Pending(watcher.Session, MsgType.MoveToAck, body));
        }
    }

    /// <summary>Mobius DecayTaskManager: corpse stays (death animation) then DeleteObject + respawn timer.</summary>
    void TickCorpseDecay(long now, List<Pending> outbound)
    {
        foreach (var npc in _byId.Values.Where(e => e.Kind == ZoneEntityKind.Npc && e.DecayAtMs > 0 && e.DecayAtMs <= now).ToList())
        {
            npc.DecayAtMs = 0;
            if (npc.CorpseAwaitingSweep)
                continue;
            DespawnNpc(npc, outbound);
        }
    }
}
