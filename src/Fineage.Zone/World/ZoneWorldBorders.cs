using Fineage.Game.Quests;
using Fineage.Zone.Combat;
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

    /// <summary>Set once the first publish has copied every region's border. Later ticks skip the copy while nothing moved.</summary>
    bool _bordersPublished;

    /// <summary>At the end-of-tick barrier, after the handoff: what each region shows its neighbours next step.</summary>
    void PublishBorders(TickContext tick)
    {
        // Boot marks every region dirty (each spawn is an add). After that an idle world has nothing to republish:
        // rebuilding the snapshot walked every region and allocated a presence list per border every 200 ms.
        if (_bordersPublished && !_byId.BordersPending)
            return;
        _borderSeen.Clear();
        _borders.Publish(tick.Tick, _byId.BorderMembers(BorderReach)
            .Select(r => (r.Region, BorderPresence(r.Region, r.Members))));
        _bordersPublished = true;
        PruneBorderPresence();
    }

    readonly HashSet<RegionId> _borderSeen = [];
    readonly List<RegionId> _borderGone = [];

    /// <summary>
    /// Drops cached conversions for regions the publish no longer yielded (the container emptied and was dropped,
    /// or the region has no border members any more). Without this every region that ever held a border entity
    /// kept its member and presence lists alive for the life of the process.
    /// </summary>
    void PruneBorderPresence()
    {
        if (_borderPresence.Count == _borderSeen.Count)
            return;
        _borderGone.Clear();
        foreach (var region in _borderPresence.Keys)
        {
            if (!_borderSeen.Contains(region))
                _borderGone.Add(region);
        }

        foreach (var region in _borderGone)
            _borderPresence.Remove(region);
    }

    /// <summary>Cached border conversions (tests/diagnostics).</summary>
    internal int BorderPresenceCacheCount => _borderPresence.Count;

    readonly Dictionary<RegionId, (List<ZoneEntity> Members, IReadOnlyList<ZonePresence> Presence)> _borderPresence = [];

    /// <summary>
    /// A clean region hands back the same member list; when it holds no player its presences cannot have changed
    /// (no member relocated, and <see cref="ZonePresence.AggroTarget"/> is false for every non-player), so the
    /// previous conversion is reused. Any republish used to re-convert every border entity of every region.
    /// </summary>
    IReadOnlyList<ZonePresence> BorderPresence(RegionId region, List<ZoneEntity> members)
    {
        _borderSeen.Add(region);
        if (_borderPresence.TryGetValue(region, out var cached) && ReferenceEquals(cached.Members, members)
            && !members.Exists(static m => m.Kind == ZoneEntityKind.Player))
            return cached.Presence;

        IReadOnlyList<ZonePresence> presence = members.ConvertAll(PresenceOf);
        _borderPresence[region] = (members, presence);
        return presence;
    }

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
            if (npc.Kind != ZoneEntityKind.Npc || !npc.Aggressive || (!npc.Attackable && !guard) ||
                npc.CurHp <= 0 || npc.AggroTargetId > 0 || npc.ReturningHome || now < npc.NextAggroScanMs ||
                IsTreasureBox(npc.NpcId) || !TryGetProactiveAggroRange(npc, out int aggroRange))
                continue;
            // _globalAggro starts at -10 and climbs once per think. A hit zeroes it.
            if (npc.GlobalAggro < 0)
                continue;
            npc.NextAggroScanMs = now + AggroScanIntervalMs;

            owned.Clear();
            candidates.Clear();
            _byId.OwnedNear(region, npc.X, npc.Y, aggroRange, owned);
            foreach (var entity in owned)
            {
                if (entity.Kind == ZoneEntityKind.Player)
                    candidates.Add(PresenceOf(entity));
            }

            _borders.NeighboursNear(region, npc.X, npc.Y, aggroRange, candidates);
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
                if (!ZoneCombat.WithinProactiveAggroRange(npc.X, npc.Y, npc.Z, p.X, p.Y, p.Z, aggroRange))
                    continue;
                // Mobius isAggressiveTowards → me.isAggressive() && GeoEngine.canSeeTarget(me, target).
                if (!CanSeePoint(npc, p.X, p.Y, p.Z))
                    continue;
                ZoneHotPathLogs.AggroOnPlayer(npc.ObjectId, npc.Name, p.ObjectId, aggroRange);
                Post(new HateMessage(npc.ObjectId, p.ObjectId, 1));
                break;
            }

            if (!guard || !ZoneNpcSettings.GuardAttackAggroMob)
                continue;

            owned.Sort(static (a, b) => a.ObjectId.CompareTo(b.ObjectId));
            foreach (var monster in owned)
            {
                if (ReferenceEquals(monster, npc) || monster.Kind != ZoneEntityKind.Npc || !monster.Aggressive ||
                    !monster.Attackable || monster.CurHp <= 0 || monster.InstanceId != npc.InstanceId ||
                    IsGuard(monster) || !ZoneCombat.IsCombatNpcType(monster.NpcType))
                    continue;
                if (!ZoneCombat.WithinProactiveAggroRange(npc.X, npc.Y, npc.Z, monster.X, monster.Y, monster.Z, aggroRange))
                    continue;
                if (!CanSeePoint(npc, monster.X, monster.Y, monster.Z))
                    continue;
                ZoneHotPathLogs.GuardAggroMonster(npc.ObjectId, npc.Name, monster.ObjectId, aggroRange);
                Post(new HateMessage(npc.ObjectId, monster.ObjectId, 1, AllowNpcAttacker: true));
                break;
            }
        }
    }

    /// <summary>Aggression reads its region and the border snapshots only; it may run on the parallel executor.</summary>
    sealed class AggressionStage(ZoneWorld world) : IRegionStage
    {
        public string Name => "aggression";

        // The body wakes only its own region's NPCs; the hate it raises travels as HateMessage to the barrier.
        public bool Parallel => true;

        public void Collect(TickContext tick, RegionWorkCollector work) => world.CollectAggression(work);

        public void RunRegion(TickContext tick, RegionId region, IReadOnlyList<int> objectIds) =>
            world.ScanAggression(region, objectIds, tick.NowUnixMs);
    }
}
