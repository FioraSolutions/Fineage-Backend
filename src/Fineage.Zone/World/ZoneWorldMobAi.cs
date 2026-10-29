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
            RemoveDeadNpc(target, outbound);
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
                continue;
            }
            if (now < npc.NextMobAttackMs)
                continue;
            npc.NextMobAttackMs = now + ZoneCombat.MobAttackIntervalMs;
            if (!ZoneCombat.InMeleeRange(npc.X, npc.Y, player.X, player.Y))
                continue;

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
}
