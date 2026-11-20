using Fineage.Game.Quests;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

/// <summary>What a region body may know about an entity it does not own (a border snapshot record).</summary>
/// <param name="AggroTarget">A live, in-world player that is not playing dead (Mobius <c>checkAggroCondition</c>).</param>
internal sealed record ZonePresence(
    int ObjectId,
    int InstanceId,
    int X,
    int Y,
    int Z,
    ZoneEntityKind Kind,
    bool AggroTarget,
    int Karma,
    FactionAlliance Alliance) : IRegionPresence;

/// <summary>
/// Border snapshots and the aggression stage built on them (step 8 of <c>docs/architecture/zone-simulation.md</c>):
/// a region body sees its own entities live and its neighbours' as of the end of the previous step.
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>How far from its edge a region publishes; queries up to this minus the handoff margin are complete.</summary>
    internal const int BorderReach = 6000;

    BorderSnapshots<ZonePresence> _borders;

    internal BorderSnapshots<ZonePresence> BorderSnapshotsForTests => _borders;

    static ZonePresence PresenceOf(ZoneEntity entity) => new(
        entity.ObjectId,
        entity.InstanceId,
        entity.X,
        entity.Y,
        entity.Z,
        entity.Kind,
        AggroTarget: entity.Kind == ZoneEntityKind.Player && entity.Session is { InWorld: true } && !entity.Dead &&
                     entity.CurHp > 0 && !entity.FakeDeath,
        entity.Karma,
        QuestMachine.AllianceOf(entity.Inventory));

    /// <summary>At the end-of-tick barrier, after the handoff: what each region shows its neighbours next step.</summary>
    void PublishBorders(TickContext tick) =>
        _borders.Publish(tick.Tick, _byId.BorderMembers(BorderReach)
            .Select(r => (r.Region, (IReadOnlyList<ZonePresence>)r.Members.ConvertAll(PresenceOf))));

    void CollectAggression(RegionWorkCollector work)
    {
        // Mobius thinkActive only matters when players exist nearby; empty world → no scan.
        if (Players().Count == 0)
            return;
        _byId.Collect(ActiveSets.Thinking, work);
    }

    /// <summary>
    /// Mobius <c>AttackableAI.thinkActive</c> for the thinking NPCs of one region: each due aggressive NPC takes the
    /// first valid player in ascending id order within its aggro range, from its own region live and from the
    /// neighbours' border snapshots, and asks for the hate by message so it lands at the barrier.
    /// </summary>
    void ScanAggression(RegionId region, IReadOnlyList<int> npcIds, long now)
    {
        var owned = new List<ZoneEntity>();
        var candidates = new List<ZonePresence>();
        foreach (int id in npcIds)
        {
            if (!_byId.TryGetValue(id, out var npc))
                continue;
            // Mobius AttackableAI.autoAttackCondition: a Guard (attackable=false in the datapack) only auto-aggroes
            // players carrying karma, inside its template aggro range and line of sight.
            bool guard = IsGuard(npc);
            if (npc.Kind != ZoneEntityKind.Npc || !npc.Aggressive || npc.AggroRange <= 0 || (!npc.Attackable && !guard) ||
                npc.CurHp <= 0 || npc.AggroTargetId > 0 || npc.ReturningHome || now < npc.NextAggroScanMs ||
                IsTreasureBox(npc.NpcId))
                continue;
            // _globalAggro starts at -10 and climbs once per think. A hit zeroes it.
            if (npc.GlobalAggro < 0)
                continue;
            npc.NextAggroScanMs = now + AggroScanIntervalMs;

            owned.Clear();
            candidates.Clear();
            _byId.OwnedNear(region, npc.X, npc.Y, npc.AggroRange, owned);
            foreach (var entity in owned)
            {
                if (entity.Kind == ZoneEntityKind.Player)
                    candidates.Add(PresenceOf(entity));
            }

            _borders.NeighboursNear(region, npc.X, npc.Y, npc.AggroRange, candidates);
            candidates.Sort(static (a, b) => a.ObjectId.CompareTo(b.ObjectId));
            foreach (var p in candidates)
            {
                if (!p.AggroTarget || p.InstanceId != npc.InstanceId)
                    continue;
                if (guard && p.Karma <= 0)
                    continue;
                // Mobius VarkaKetra.onNpcHate: Ketra / Varka monsters leave their allies alone.
                if (QuestMachine.IsAlliedWith(npc.NpcId, p.Alliance))
                    continue;
                long dx = npc.X - (long)p.X, dy = npc.Y - (long)p.Y, dz = npc.Z - (long)p.Z;
                if (dx * dx + dy * dy + dz * dz > (long)npc.AggroRange * npc.AggroRange)
                    continue;
                // Mobius isAggressiveTowards → me.isAggressive() && GeoEngine.canSeeTarget(me, target).
                if (!CanSeePoint(npc, p.X, p.Y, p.Z))
                    continue;
                ZoneLog.Info($"[aggro] npc={npc.ObjectId} ({npc.Name}) aggro on player={p.ObjectId} range={npc.AggroRange}");
                Post(new HateMessage(npc.ObjectId, p.ObjectId, 1));
                break;
            }
        }
    }

    /// <summary>Aggression reads its region and the border snapshots only; it stays sequential until AI soaks parallel.</summary>
    sealed class AggressionStage(ZoneWorld world) : IRegionStage
    {
        public string Name => "aggression";

        public void Collect(TickContext tick, RegionWorkCollector work) => world.CollectAggression(work);

        public void RunRegion(TickContext tick, RegionId region, IReadOnlyList<int> objectIds) =>
            world.ScanAggression(region, objectIds, tick.NowUnixMs);
    }
}
