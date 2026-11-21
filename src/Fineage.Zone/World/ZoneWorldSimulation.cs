using System.Diagnostics.CodeAnalysis;
using Fineage.Zone.Bosses;
using Fineage.Zone.DimensionalRift;
using Fineage.Zone.Clan;
using Fineage.Zone.Lottery;
using Fineage.Zone.MonsterRace;
using Fineage.Zone.Manor;
using Fineage.Zone.Olympiad;
using Fineage.Zone.Party;
using Fineage.Zone.Punishment;
using Fineage.Zone.SevenSigns;
using Fineage.Zone.Siege;
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
    ISimulationObserver _simulationObserver;
    RegionTickScheduler _simulationTicks;
    IRegionStage _regenPlayersStage;
    IRegionStage _regenNpcsStage;
    IRegionStage _abnormalsStage;
    IRegionStage _aggressionStage;
    IRegionStage _randomWalkStage;

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
    internal void StepSimulation() => RunStep(wall =>
    {
        _simulationTicks.Step(wall);
        _commandLog?.AppendStep(_simulationTicks.Tick, wall);
    });

    /// <summary>Test hook: one full scheduler step with fan-out and persistence, same as <see cref="StepSimulation"/>.</summary>
    internal void RunStepForTest() => StepSimulation();

    /// <summary>Test hook: runs <paramref name="body"/> with world ownership, without advancing the scheduler.</summary>
    internal void RunStepForTest(Action body)
    {
        using (_affinity.Step())
            body();
    }

    /// <summary>Test hook: runs <paramref name="body"/> with world ownership, without advancing the scheduler.</summary>
    internal T RunStepForTest<T>(Func<T> body)
    {
        using (_affinity.Step())
            return body();
    }

    /// <summary>
    /// Runs <paramref name="body"/> as a step with one wall sample, on the calling thread, which owns the world until
    /// the step is over (<see cref="WorldAffinity"/>). Its fan-out is recorded into one batch, sealed when the body
    /// returns and then sent; inventory/warehouse Postgres writes the stages dirtied are flushed before the send.
    /// Commands it applied are answered last.
    /// </summary>
    void RunStep(Action<long> body)
    {
        using var turn = _affinity.Step();
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

    /// <summary>
    /// Runs <paramref name="body"/> at the tick the wall clock is in, pinned like a step: test and admin hooks that run
    /// one actor outside the loop.
    /// </summary>
    T AtHookTick<T>(Func<TickContext, T> body)
    {
        long wall = _simulationClock.WallUnixMs();
        _simulationTicks.Resume(wall);
        var tick = new TickContext(_simulationTicks.Tick, _simulationClock.TimeOf(_simulationTicks.Tick), wall);
        using (_simulationClock.Pin(tick))
            return body(tick);
    }

    /// <summary>Per-region streams for <paramref name="purpose"/>, or one injected generator for every region.</summary>
    IRegionRandom Streams(Random? injected, RandomPurpose purpose)
    {
        if (injected is null)
            return new SeededRegionRandom(_simulationSeed, purpose);
        _sharedStreams.Add(purpose);
        return new SharedRegionRandom(injected);
    }

    /// <summary>The stream of one global system: the actor's own (bosses, lottery, mammon, olympiad, manor) or fishing's.</summary>
    Random GlobalStream(RandomPurpose purpose) => new SimulationRandom(SimulationRandom.StreamSeed(_simulationSeed, purpose));

    /// <summary>The stream a roll made by <paramref name="actor"/> draws from: the one of the region that owns it.</summary>
    Random Rng(IRegionRandom streams, ZoneEntity actor) => streams.For(_byId.OwnerOf(actor).Region);

    [MemberNotNull(nameof(_simulationClock), nameof(_regions), nameof(_byId), nameof(_simulationObserver),
        nameof(_simulationTicks), nameof(_regenPlayersStage), nameof(_regenNpcsStage), nameof(_abnormalsStage),
        nameof(_aggressionStage), nameof(_randomWalkStage), nameof(_borders))]
    void InitSimulation(IRegionPartition partition, ISimulationObserver observer, IRegionExecutor executor)
    {
        _simulationClock = new SimulationClock(ZoneSimulationSettings.StepMilliseconds, () => _clock().ToUnixTimeMilliseconds());
        _regions = partition;
        _byId = new RegionDirectory<ZoneEntity>(partition, requireOwner: _affinity.Require);
        _simulationObserver = observer;
        if (observer is ZoneSimulationObserver production)
            production.BindNpcDiagnostics(TickNpcDiagnostics);
        _borders = new BorderSnapshots<ZonePresence>(partition, BorderReach);
        _regenPlayersStage = new RegionStage("regen.players", CollectRegenPlayers, (_, id) => RegenPlayer(id), parallel: true);
        _regenNpcsStage = new RegionStage("regen.npcs", CollectRegenNpcs, (_, id) => RegenNpc(id), parallel: true);
        _abnormalsStage = new RegionStage("abnormals", CollectAbnormals, (tick, id) => TickEntityAbnormals(id, tick.NowUnixMs),
            parallel: true);
        _aggressionStage = new AggressionStage(this);
        _randomWalkStage = new RandomWalkStage(this);

        int slow = ZoneSimulationSettings.SlowPeriodTicks;
        // Every region body runs in a RegionBodyScope, so the executor cannot change the outcome; it only decides
        // whether the stages that allow it spread their regions over worker threads.
        var ticks = new RegionTickScheduler(_simulationClock, executor, observer, new RegionScopes(this));

        // Phase 0: player commands submitted since the last step.
        ticks.AddWorldStage(CommandsStage, ApplyCommands);

        // Every step: the former 200 ms combat loop, in its original order.
        ticks.AddWorldStage("movement.player", tick => TickPlayerMovement(tick.NowUnixMs));
        ticks.AddWorldStage("arena", _ => TickArenaZones());
        ticks.AddWorldStage("movement.npc", tick => TickNpcMovement(tick.NowUnixMs));
        ticks.AddRegionStage(_randomWalkStage);
        ticks.AddWorldStage("pending-hits", tick => TickPendingHits(tick.NowUnixMs));
        ticks.AddWorldStage("melee-hits", tick => TickMeleeHits(tick.NowUnixMs));
        ticks.AddWorldStage("pvp-flags", tick => TickPvpFlags(tick.NowUnixMs));
        ticks.AddWorldStage("auto-attacks", tick => TickAutoAttacks(tick.NowUnixMs));
        ticks.AddWorldStage("pending-casts", tick => TickPendingCasts(tick.NowUnixMs));
        ticks.AddWorldStage("queued-moves", tick => TickQueuedMoves(tick.NowUnixMs));
        ticks.AddWorldStage("trade-range", _ => TickTradeRange());
        ticks.AddWorldStage(PartyActor.StageName, TickParty);
        ticks.AddWorldStage("pending-pickups", tick => TickPendingPickups(tick.NowUnixMs));
        ticks.AddWorldStage("pending-talks", tick => TickPendingTalks(tick.NowUnixMs));
        ticks.AddWorldStage("wedding-teleport", tick => TickPendingWeddingTeleports(tick.NowUnixMs));
        ticks.AddWorldStage("fear", tick => TickFear(tick.NowUnixMs));
        ticks.AddRegionStage(_aggressionStage);
        ticks.AddWorldStage("leash", tick => TickLeash(tick.NowUnixMs));
        ticks.AddWorldStage("mob-attacks", tick => TickMobAttacks(tick.NowUnixMs));
        ticks.AddWorldStage("summons", tick => TickSummons(tick.NowUnixMs));
        ticks.AddWorldStage("cubics", tick => TickCubics(tick.NowUnixMs));
        ticks.AddWorldStage("duels", tick => TickDuels(tick.NowUnixMs));
        ticks.AddWorldStage("corpse-decay", tick => TickCorpseDecay(tick.NowUnixMs));
        ticks.AddWorldStage("attack-stance", tick => TickAttackStance(tick.NowUnixMs));
        ticks.AddWorldStage("ground-expiry", tick => TickGroundExpiry(tick.NowUnixMs));
        ticks.AddWorldStage("environment.zones", tick => TickEnvironmentZones(tick.NowUnixMs));
        ticks.AddWorldStage(PunishmentActor.StageName, tick => TickPunishments(tick.NowUnixMs));
        ticks.AddWorldStage("door-auto-close", tick => TickDoorAutoClose(tick.WallUnixMs));
        ticks.AddWorldStage("cursed-weapons", tick => TickCursedWeapons(tick.NowUnixMs));

        // Every slow period: the former 3 s loop, in its original order. Calendar schedules (clan deadlines, auctions,
        // sieges, manor, lottery, mammon) take the step's wall sample; the rest follows simulation time.
        ticks.AddRegionStage(_regenPlayersStage, slow);
        ticks.AddRegionStage(_regenNpcsStage, slow);
        ticks.AddWorldStage("spawns", _ =>
        {
            TickDayNightSpawns();
            TickSpawns();
        }, slow);
        ticks.AddRegionStage(_abnormalsStage, slow);
        ticks.AddWorldStage(BossActor.StageName, tick => SpawnBosses(_bosses.Step(tick)), slow);
        ticks.AddWorldStage(DimensionalRiftActor.StageName, TickDimensionalRift, slow);
        ticks.AddWorldStage(ClanActor.StageName, tick => DissolveClans(tick, _clan.Step(tick)), slow);
        ticks.AddWorldStage(HallAuctionActor.StageName, TickHallAuctions, slow);
        ticks.AddWorldStage(HallFunctionActor.StageName, tick =>
        {
            TickHallDecoEnter();
            TickHallFunctions(tick);
        }, slow);
        ticks.AddWorldStage(SiegeActor.StageName, TickSieges, slow);
        ticks.AddWorldStage(ChSiegeActor.StageName, TickChSieges, slow);
        ticks.AddWorldStage(ManorActor.StageName, tick => ApplyManorModes(_manor.Step(tick)), slow);
        ticks.AddWorldStage("fishing", tick => TickFishing(tick.NowUnixMs), slow);
        ticks.AddWorldStage(LotteryActor.StageName, tick => AnnounceLottery(_lottery.Step(tick)), slow);
        ticks.AddWorldStage(MonsterRaceActor.StageName, tick => AnnounceMonsterRace(_monsterRace.Step(tick)), slow);
        ticks.AddWorldStage(SevenSignsActor.StageName, tick => ApplySevenSignsPeriods(_sevenSigns.Step(tick)), slow);
        ticks.AddWorldStage(SevenSignsFestivalActor.StageName, TickSevenSignsFestival, slow);
        ticks.AddWorldStage(MammonActor.StageName, tick => PlaceMammons(_mammon.Step(tick)), slow);
        ticks.AddWorldStage(OlympiadActor.StageName, tick => ApplyOlympiad(_olympiad.Step(tick)), slow);

        // End-of-tick barrier: owners change only here, after every stage of the step has run.
        ticks.AddWorldStage(HandoffStage, HandOff);
        _simulationTicks = ticks;
    }
}
