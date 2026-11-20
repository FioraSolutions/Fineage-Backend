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
    readonly ulong _simulationSeed;
    SimulationClock _simulationClock;
    IRegionPartition _regions;
    RegionTickScheduler _simulationTicks;
    IRegionStage _regenPlayersStage;
    IRegionStage _regenNpcsStage;
    IRegionStage _abnormalsStage;

    internal RegionTickScheduler SimulationTicks => _simulationTicks;

    /// <summary>Seed of every random stream in this world; log it to replay a run (<c>FINEAGE_ZONE_SEED</c>).</summary>
    public ulong SimulationSeed => _simulationSeed;

    /// <summary>
    /// Simulation time for rules: the tick time inside a step (commands included); outside one (session enter and
    /// leave, read-only snapshots) the wall clock.
    /// </summary>
    long NowUnixMs() => _simulationClock.NowUnixMs();

    /// <summary>The wall sample of the current step for calendar rules; outside a step the wall clock.</summary>
    DateTimeOffset WallNow() => DateTimeOffset.FromUnixTimeMilliseconds(_simulationClock.WallUnixMs());

    /// <summary>One full fixed step on demand (tests and admin diagnostics), same body the loop runs.</summary>
    internal void StepSimulation() => RunUnderGate(wall =>
    {
        _simulationTicks.Step(wall);
        _commandLog?.AppendStep(_simulationTicks.Tick, wall);
    });

    /// <summary>
    /// Runs <paramref name="body"/> under <c>_gate</c> with one wall sample. Its fan-out is recorded into one batch that
    /// is sealed before the lock is released and sent after it; inventory/warehouse Postgres writes the stages dirtied
    /// are flushed after the lock too. Commands it applied are answered last.
    /// </summary>
    void RunUnderGate(Action<long> body)
    {
        bool outermost = _fanout.Recording == null;
        IReadOnlyList<ZoneCommandIngress.Applied> answered = [];
        try
        {
            WithDeferredInventoryPersist(() =>
            {
                _fanout.Begin();
                try
                {
                    body(_simulationClock.WallUnixMs());
                }
                finally
                {
                    _fanout.Seal();
                    if (outermost)
                        answered = _ingress.TakeApplied();
                }

                return 0;
            });
        }
        finally
        {
            _fanout.Drain();
            ZoneCommandIngress.Complete(answered);
        }
    }

    /// <summary>Per-region streams for <paramref name="purpose"/>, or one injected generator for every region.</summary>
    IRegionRandom Streams(Random? injected, RandomPurpose purpose) =>
        injected is null ? new SeededRegionRandom(_simulationSeed, purpose) : new SharedRegionRandom(injected);

    /// <summary>The stream of a global system (fishing, manor, lottery, mammon) until it becomes its own actor.</summary>
    Random GlobalStream(RandomPurpose purpose) => new SimulationRandom(SimulationRandom.StreamSeed(_simulationSeed, purpose));

    /// <summary>The stream a roll made by <paramref name="actor"/> draws from: the one of the region it stands in.</summary>
    Random Rng(IRegionRandom streams, ZoneEntity actor) =>
        streams.For(_regions.RegionOf(actor.InstanceId, actor.X, actor.Y));

    [MemberNotNull(nameof(_simulationClock), nameof(_regions), nameof(_simulationTicks), nameof(_regenPlayersStage),
        nameof(_regenNpcsStage), nameof(_abnormalsStage))]
    void InitSimulation(IRegionPartition partition, ISimulationObserver observer)
    {
        _simulationClock = new SimulationClock(ZoneSimulationSettings.StepMilliseconds, () => _clock().ToUnixTimeMilliseconds());
        _regions = partition;
        _regenPlayersStage = new RegionStage("regen.players", CollectRegenPlayers, (_, id) => RegenPlayer(id));
        _regenNpcsStage = new RegionStage("regen.npcs", CollectRegenNpcs, (_, id) => RegenNpc(id));
        _abnormalsStage = new RegionStage("abnormals", CollectAbnormals, (tick, id) => TickEntityAbnormals(id, tick.NowUnixMs));

        int slow = ZoneSimulationSettings.SlowPeriodTicks;
        // Region bodies still fan out through world-wide Players() scans and party state, so regions run one after
        // another under _gate until that state is region-owned.
        var ticks = new RegionTickScheduler(_simulationClock, partition, SequentialRegionExecutor.Instance, observer);

        // Phase 0: player commands submitted since the last step.
        ticks.AddWorldStage(CommandsStage, ApplyCommands);

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

        // Every slow period: the former 3 s loop, in its original order. Calendar schedules (clan deadlines, auctions,
        // sieges, manor, lottery, mammon) take the step's wall sample; the rest follows simulation time.
        ticks.AddRegionStage(_regenPlayersStage, slow);
        ticks.AddRegionStage(_regenNpcsStage, slow);
        ticks.AddWorldStage("spawns", _ => TickSpawns(), slow);
        ticks.AddRegionStage(_abnormalsStage, slow);
        ticks.AddWorldStage("bosses", _ => TickBosses(), slow);
        ticks.AddWorldStage("clan.dissolutions", tick => CompleteDueDissolutions(tick.WallUnixMs), slow);
        ticks.AddWorldStage("clan.hall-auctions", tick => TickHallAuctions(tick.WallUnixMs), slow);
        ticks.AddWorldStage("sieges", tick => TickSieges(tick.WallUnixMs), slow);
        ticks.AddWorldStage("manor", tick => TickManor(tick.WallUnixMs), slow);
        ticks.AddWorldStage("fishing", tick => TickFishing(tick.NowUnixMs), slow);
        ticks.AddWorldStage("lottery", tick => TickLottery(tick.WallUnixMs), slow);
        ticks.AddWorldStage("seven-signs", _ => TickSevenSigns(), slow);
        ticks.AddWorldStage("mammon", tick => TickMammon(tick.WallUnixMs), slow);
        ticks.AddWorldStage("olympiad", _ => TickOlympiad(), slow);
        _simulationTicks = ticks;
    }
}
