using System.Diagnostics.CodeAnalysis;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

/// <summary>
/// The zone tick pipeline as a <see cref="RegionTickScheduler"/>: combat runs every step; regen and abnormals run per
/// region at period <see cref="ZoneSimulationSettings.SlowPeriodTicks"/>; the remaining systems are world stages in
/// the order the loops always ran them. See <c>docs/architecture/zone-simulation.md</c> for the migration plan.
/// </summary>
public sealed partial class ZoneWorld
{
    RegionTickScheduler _simulationTicks;
    IRegionStage _regenPlayersStage;
    IRegionStage _regenNpcsStage;
    IRegionStage _abnormalsStage;

    internal RegionTickScheduler SimulationTicks => _simulationTicks;

    /// <summary>One full fixed step on demand (tests and admin diagnostics), same body the loop runs.</summary>
    internal void StepSimulation() => RunUnderGate(_simulationTicks.Step);

    /// <summary>
    /// Runs <paramref name="body"/> under <c>_gate</c> with one time sample; inventory/warehouse Postgres writes the
    /// stages dirtied are flushed after the lock is released.
    /// </summary>
    void RunUnderGate(Action<long> body) =>
        WithDeferredInventoryPersist(() =>
        {
            body(_clock().ToUnixTimeMilliseconds());
            return 0;
        });

    [MemberNotNull(nameof(_simulationTicks), nameof(_regenPlayersStage), nameof(_regenNpcsStage), nameof(_abnormalsStage))]
    void InitSimulation(IRegionPartition partition, ISimulationObserver observer)
    {
        _regenPlayersStage = new RegionStage("regen.players", CollectRegenPlayers, (_, id) => RegenPlayer(id));
        _regenNpcsStage = new RegionStage("regen.npcs", CollectRegenNpcs, (_, id) => RegenNpc(id));
        _abnormalsStage = new RegionStage("abnormals", CollectAbnormals, (tick, id) => TickEntityAbnormals(id, tick.NowUnixMs));

        int slow = ZoneSimulationSettings.SlowPeriodTicks;
        // Region bodies still fan out through world-wide Players() scans and party state, so regions run one after
        // another under _gate until that state is region-owned.
        var ticks = new RegionTickScheduler(
            ZoneSimulationSettings.StepMilliseconds, partition, SequentialRegionExecutor.Instance, observer);

        // Every step: the former 200 ms combat loop, in its original order.
        ticks.AddWorldStage("movement.player", tick => TickPlayerMovement(tick.NowUnixMs));
        ticks.AddWorldStage("arena", _ => TickArenaZones());
        ticks.AddWorldStage("movement.npc", tick => TickNpcMovement(tick.NowUnixMs));
        ticks.AddWorldStage("random-walk", tick => TickRandomWalk(tick.NowUnixMs));
        ticks.AddWorldStage("pending-hits", tick => TickPendingHits(tick.NowUnixMs));
        ticks.AddWorldStage("melee-hits", tick => TickMeleeHits(tick.NowUnixMs));
        ticks.AddWorldStage("pvp-flags", tick => TickPvpFlags(tick.NowUnixMs));
        ticks.AddWorldStage("auto-attacks", tick => TickAutoAttacks(tick.NowUnixMs));
        ticks.AddWorldStage("pending-casts", tick => TickPendingCasts(tick.NowUnixMs));
        ticks.AddWorldStage("queued-moves", tick => TickQueuedMoves(tick.NowUnixMs));
        ticks.AddWorldStage("trade-range", _ => TickTradeRange());
        ticks.AddWorldStage("party", tick => TickParty(tick.NowUnixMs));
        ticks.AddWorldStage("pending-pickups", tick => TickPendingPickups(tick.NowUnixMs));
        ticks.AddWorldStage("pending-talks", tick => TickPendingTalks(tick.NowUnixMs));
        ticks.AddWorldStage("fear", tick => TickFear(tick.NowUnixMs));
        ticks.AddWorldStage("aggression", tick => TickAggression(tick.NowUnixMs));
        ticks.AddWorldStage("leash", tick => TickLeash(tick.NowUnixMs));
        ticks.AddWorldStage("mob-attacks", tick => TickMobAttacks(tick.NowUnixMs));
        ticks.AddWorldStage("summons", tick => TickSummons(tick.NowUnixMs));
        ticks.AddWorldStage("cubics", tick => TickCubics(tick.NowUnixMs));
        ticks.AddWorldStage("duels", tick => TickDuels(tick.NowUnixMs));
        ticks.AddWorldStage("corpse-decay", tick => TickCorpseDecay(tick.NowUnixMs));
        ticks.AddWorldStage("attack-stance", tick => TickAttackStance(tick.NowUnixMs));
        ticks.AddWorldStage("ground-expiry", tick => TickGroundExpiry(tick.NowUnixMs));

        // Every slow period: the former 3 s loop, in its original order.
        ticks.AddRegionStage(_regenPlayersStage, slow);
        ticks.AddRegionStage(_regenNpcsStage, slow);
        ticks.AddWorldStage("spawns", _ => TickSpawns(), slow);
        ticks.AddRegionStage(_abnormalsStage, slow);
        ticks.AddWorldStage("bosses", _ => TickBosses(), slow);
        ticks.AddWorldStage("clan.dissolutions", tick => CompleteDueDissolutions(tick.NowUnixMs), slow);
        ticks.AddWorldStage("clan.hall-auctions", tick => TickHallAuctions(tick.NowUnixMs), slow);
        ticks.AddWorldStage("sieges", tick => TickSieges(tick.NowUnixMs), slow);
        ticks.AddWorldStage("manor", tick => TickManor(tick.NowUnixMs), slow);
        ticks.AddWorldStage("fishing", tick => TickFishing(tick.NowUnixMs), slow);
        ticks.AddWorldStage("lottery", tick => TickLottery(tick.NowUnixMs), slow);
        ticks.AddWorldStage("seven-signs", _ => TickSevenSigns(), slow);
        ticks.AddWorldStage("mammon", tick => TickMammon(tick.NowUnixMs), slow);
        ticks.AddWorldStage("olympiad", _ => TickOlympiad(), slow);
        _simulationTicks = ticks;
    }
}
