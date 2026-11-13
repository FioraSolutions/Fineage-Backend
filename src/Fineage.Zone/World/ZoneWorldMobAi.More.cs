using Fineage.Game.Combat;
using Fineage.Protocol;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    /// <summary>
    /// One AI tick of chase (Mobius moveToPawn). Only (re)issues a MoveToLocation when the chase starts or the
    /// pawn drifted; the actual travel is done by <see cref="TickNpcMovement"/> on the shared speed-tick mover.
    /// </summary>
    /// <summary>Mobius <c>TURN_FLEE</c> / <c>onEvtAfraid</c> — slot comes from skills.json abnormalType.</summary>
    static bool IsFeared(ZoneEntity entity)
    {
        for (int i = 0; i < entity.Abnormals.Count; i++)
        {
            if (string.Equals(entity.Abnormals[i].Slot, "TURN_FLEE", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>Mobius <c>CreatureAI.FEAR_RANGE</c> and <c>EffectTickRatio</c> 666 × Fear.getTicks() 5.</summary>
    const int FearRange = 500;
    const int FearStepMs = 5 * 666;

    /// <summary>
    /// Mobius <c>onEvtAfraid</c>: run <see cref="FearRange"/> along the angle from the caster (start)
    /// or along the current heading (later ticks). Geo clips the dest in <see cref="StartTimedMove"/>.
    /// </summary>
    void StartFearFlee(ZoneEntity actor, ZoneEntity? from, bool alongHeading)
    {
        long now = _clock().ToUnixTimeMilliseconds();
        actor.FearFleeAtUnixMs = now + FearStepMs;
        if (actor.Session is { } session)
            session.FearFleeAtUnixMs = actor.FearFleeAtUnixMs;
        if (actor.Dead || actor.Sitting || IsImmobilized(actor))
            return;

        double degrees = !alongHeading && from != null && (from.X != actor.X || from.Y != actor.Y)
            ? ZoneCombat.AngleFrom(from.X, from.Y, actor.X, actor.Y)
            : actor.Heading / 182.044444444;
        double rad = degrees * Math.PI / 180.0;
        int destX = (int)Math.Round(actor.X + FearRange * Math.Cos(rad));
        int destY = (int)Math.Round(actor.Y + FearRange * Math.Sin(rad));
        actor.Heading = ZoneCombat.HeadingFrom(actor.X, actor.Y, destX, destY);
        if (!actor.Running)
        {
            actor.Running = true;
            FanoutMoveType(actor);
        }

        if (actor.Kind == ZoneEntityKind.Npc)
        {
            actor.Chasing = false;
            StartNpcMove(actor, destX, destY, actor.Z, now);
            return;
        }

        if (actor.Session is not { } live)
            return;
        live.AutoAttackTargetId = 0;
        live.ClearApproach();
        StartTimedMove(actor, live, new MoveToMsg
        {
            ObjectId = actor.ObjectId,
            DestX = destX,
            DestY = destY,
            DestZ = actor.Z,
            OriginX = actor.X,
            OriginY = actor.Y,
            OriginZ = actor.Z
        });
    }

    /// <summary>Mobius Fear <c>onActionTime</c>: keep fleeing along the heading while TURN_FLEE is up.</summary>
    void TickFear(long now)
    {
        foreach (var player in Players().ToList())
        {
            var session = player.Session;
            if (session == null || !IsFeared(player) || now < player.FearFleeAtUnixMs)
                continue;
            StartFearFlee(player, from: null, alongHeading: true);
        }
    }

    /// <summary>Mobius <c>Fear.onStart</c>: abort only when <c>canAbortCast</c> (magic / static).</summary>
    void AbortInterruptibleCast(ZoneEntity actor)
    {
        var hit = actor.PendingHit;
        if (hit == null)
            return;
        bool interruptible = hit.Combat != null
            ? CastRules.Interruptible(hit.Combat.Magic)
            : CastRules.Interruptible(hit.MagicType);
        if (interruptible)
            AbortCast(actor, "fear", notify: false);
    }

    void ChaseStep(ZoneEntity npc, ZoneEntity target, long now)
    {
        double dx = target.X - npc.X;
        double dy = target.Y - npc.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        double stopAt = ZoneCombat.MobAttackReach(npc.AttackRange) - ZoneCombat.ApproachSlack;
        if (dist <= stopAt)
            return;

        // Destination: point at stopAt from the target on the npc side (Mobius moveToPawn offset).
        int destX = (int)Math.Round(target.X - dx / dist * stopAt);
        int destY = (int)Math.Round(target.Y - dy / dist * stopAt);
        int destZ = target.Z;

        long ddx = destX - (long)npc.ChaseDestX, ddy = destY - (long)npc.ChaseDestY;
        if (!npc.Chasing || !npc.MoveActive || ddx * ddx + ddy * ddy > 40L * 40L)
        {
            if (!npc.HomeSet)
            {
                npc.HomeSet = true;
                (npc.HomeX, npc.HomeY, npc.HomeZ) = (npc.X, npc.Y, npc.Z);
            }
            SetReturningHome(npc, false);
            if (!npc.Running)
            {
                // Mobius AttackableAI: setRunning() on attack intention -> ChangeMoveType(run).
                npc.Running = true;
                FanoutMoveType(npc);
            }
            npc.Chasing = true;
            npc.ChaseDestX = destX;
            npc.ChaseDestY = destY;
            StartNpcMove(npc, destX, destY, destZ, now);
        }
    }

    void BroadcastNpcMove(ZoneEntity npc, int destX, int destY, int destZ)
    {
        List<int>? netWatchers = null;
        foreach (var watcher in Players())
        {
            if (watcher.Session != null && watcher.Knownlist.Contains(npc.ObjectId))
            {
                netWatchers ??= [];
                netWatchers.Add(watcher.ObjectId);
            }
        }

        if (netWatchers is { Count: > 0 } && NetBridge != null)
        {
            NetBridge.FanoutMoveState(
                netWatchers,
                npc.ObjectId,
                npc.X, npc.Y, npc.Z,
                destX, destY, destZ,
                npc.Running);
        }
    }

    /// <summary>Mobius DecayTaskManager: corpse stays (death animation) then DeleteObject + respawn timer.</summary>
    void TickCorpseDecay(long now)
    {
        if (_decaying.Count == 0)
            return;
        foreach (int id in _decaying.ToArray())
        {
            if (!_byId.TryGetValue(id, out var npc) || npc.Kind != ZoneEntityKind.Npc || npc.DecayAtMs <= 0)
            {
                _decaying.Remove(id);
                continue;
            }
            if (npc.DecayAtMs > now)
                continue;
            SetDecayAtMs(npc, 0);
            DespawnNpc(npc);
        }
    }
}
