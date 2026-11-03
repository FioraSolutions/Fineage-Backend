using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius AggroList, aggressive scan, clan help and attack stance:
/// <list type="bullet">
/// <item>Attackable.addDamageHate: hate += damage·100/(level+7) (min 1); the mob attacks its most hated valid attacker.</item>
/// <item>AttackableAI.thinkActive: an aggressive mob (isAggressive, aggroRange &gt; 0) picks a live player inside aggroRange (3D).</item>
/// <item>AttackableAI.thinkAttack: same-clan mobs inside clanHelpRange that are idle join with 1 hate on the target.</item>
/// <item>AttackStanceTaskManager: AutoAttackStart on entering combat, AutoAttackStop after 15 s idle / when a mob drops its target.</item>
/// </list>
/// </summary>
public sealed partial class ZoneWorld
{
    const int AggroScanIntervalMs = 500;
    const int ClanCallIntervalMs = 1000;

    /// <summary>Mobius hate for <paramref name="damage"/> dealt to an NPC of <paramref name="npcLevel"/>.</summary>
    internal static long DamageHate(int damage, int npcLevel) => Math.Max(1, (long)damage * 100 / (Math.Max(1, npcLevel) + 7));

    void AddHate(ZoneEntity npc, ZoneEntity attacker, long hate = 1)
    {
        if (npc.Kind != ZoneEntityKind.Npc || attacker.Kind != ZoneEntityKind.Player || npc.CurHp <= 0)
            return;
        npc.HateList[attacker.ObjectId] = npc.HateList.GetValueOrDefault(attacker.ObjectId) + Math.Max(1, hate);
        int top = MostHated(npc);
        if (top == npc.AggroTargetId || top == 0)
            return;
        npc.AggroTargetId = top;
        // Mobius: first swing after attack-speed delay, not instantly.
        npc.NextMobAttackMs = _clock().ToUnixTimeMilliseconds() + ZoneCombat.HitTimeMs(NpcPAtkSpd(npc));
    }

    bool ValidHateTarget(ZoneEntity npc, int playerId) =>
        _byId.TryGetValue(playerId, out var p) && p.Kind == ZoneEntityKind.Player && !p.Dead && p.CurHp > 0 &&
        p.Session != null && SameInstance(npc.ObjectId, p.ObjectId) && ZoneCombat.InAggroRange(npc.X, npc.Y, p.X, p.Y);

    /// <summary>Most hated valid attacker (drops invalid entries); 0 when the list is empty.</summary>
    internal int MostHated(ZoneEntity npc)
    {
        int best = 0;
        long bestHate = long.MinValue;
        List<int>? stale = null;
        foreach (var (id, hate) in npc.HateList)
        {
            if (!ValidHateTarget(npc, id))
            {
                (stale ??= []).Add(id);
                continue;
            }
            if (hate > bestHate)
            {
                best = id;
                bestHate = hate;
            }
        }
        if (stale != null)
            foreach (int id in stale)
                npc.HateList.Remove(id);
        return best;
    }

    /// <summary>Mobius <c>GeoEngine.canSeeTarget(creature, target)</c>: no closed door between, then geo LOS.</summary>
    internal bool CanSeeTarget(ZoneEntity from, ZoneEntity to)
    {
        if (Doors.FirstBlocking(from.X, from.Y, from.Z, to.X, to.Y, to.Z, out _, out _, out _) != null)
            return false;
        return _geo.CanSeeTarget(from.X, from.Y, from.Z, to.X, to.Y, to.Z);
    }

    public const int SysMsgCannotSeeTarget = 181;

    void CannotSeeTarget(ZoneSession session, List<Pending> outbound)
    {
        session.AutoAttackTargetId = 0;
        session.ClearApproach();
        ShotNotice(session, SysMsgCannotSeeTarget, "Cannot see target.", outbound);
    }

    void TickAggression(long now)
    {
        List<ZoneEntity>? players = null;
        foreach (var npc in _byId.Values)
        {
            if (npc.Kind != ZoneEntityKind.Npc || !npc.Aggressive || npc.AggroRange <= 0 || !npc.Attackable ||
                npc.CurHp <= 0 || npc.AggroTargetId > 0 || npc.ReturningHome || now < npc.NextAggroScanMs)
                continue;
            npc.NextAggroScanMs = now + AggroScanIntervalMs;
            players ??= Players();
            foreach (var p in players)
            {
                if (p.Dead || p.CurHp <= 0 || p.Session == null || !SameInstance(npc.ObjectId, p.ObjectId))
                    continue;
                long dx = npc.X - (long)p.X, dy = npc.Y - (long)p.Y, dz = npc.Z - (long)p.Z;
                if (dx * dx + dy * dy + dz * dz > (long)npc.AggroRange * npc.AggroRange)
                    continue;
                // Mobius isAggressiveTowards → me.isAggressive() && GeoEngine.canSeeTarget(me, target).
                if (!CanSeeTarget(npc, p))
                    continue;
                ZoneLog.Info($"[aggro] npc={npc.ObjectId} ({npc.Name}) aggro on player={p.ObjectId} range={npc.AggroRange}");
                AddHate(npc, p);
                break;
            }
        }
    }

    /// <summary>Mobius clan help: idle same-clan mobs within clanHelpRange of <paramref name="caller"/> attack <paramref name="target"/>.</summary>
    void CallClanHelp(ZoneEntity caller, ZoneEntity target, long now)
    {
        if (caller.Clans == null || caller.ClanHelpRange <= 0 || now < caller.NextClanCallMs)
            return;
        caller.NextClanCallMs = now + ClanCallIntervalMs;
        foreach (var other in _byId.Values)
        {
            if (other.Kind != ZoneEntityKind.Npc || ReferenceEquals(other, caller) || other.CurHp <= 0 || !other.Attackable ||
                other.AggroTargetId > 0 || other.Clans == null || !other.Clans.Any(c => caller.Clans.Contains(c)) ||
                (caller.IgnoreNpcIds?.Contains(other.NpcId) ?? false) ||
                !ZoneCombat.Within(caller.X, caller.Y, other.X, other.Y, caller.ClanHelpRange) ||
                !SameInstance(caller.ObjectId, other.ObjectId))
                continue;
            ZoneLog.Info($"[aggro] clan help npc={other.ObjectId} ({other.Name}) for npc={caller.ObjectId} on player={target.ObjectId}");
            AddHate(other, target);
        }
    }

    /// <summary>Fans Net AutoAttackStart/Stop when an entity's stance changes (players: combat window; NPCs: has a target).</summary>
    void TickAttackStance(long now)
    {
        if (NetBridge == null)
            return;
        List<ZoneEntity>? players = null;
        foreach (var e in _byId.Values)
        {
            bool inStance = e.Kind switch
            {
                ZoneEntityKind.Player => !e.Dead && e.CombatUntilUnixMs > now,
                ZoneEntityKind.Npc => e.CurHp > 0 && e.AggroTargetId > 0,
                _ => false
            };
            if (inStance == e.StanceShown)
                continue;
            e.StanceShown = inStance;
            // Self + every player that knows the entity (NPC knownlists are not tracked).
            players ??= Players();
            var watchers = new List<int>();
            foreach (var w in players)
            {
                if (w.Session != null && (ReferenceEquals(w, e) || w.Knownlist.Contains(e.ObjectId)))
                    watchers.Add(w.ObjectId);
            }
            if (watchers.Count > 0)
                NetBridge.FanoutAttackStance(watchers, e.ObjectId, inStance);
        }
    }
}
