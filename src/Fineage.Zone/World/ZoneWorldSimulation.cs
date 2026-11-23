using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Fineage.Protocol.Telemetry;
using Fineage.Zone.Bosses;
using Fineage.Zone.DimensionalRift;
using Fineage.Zone.Clan;
using Fineage.Zone.Fishing;
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
    IRegionStage _playerMovementStage;
    IRegionStage _npcMovementStage;
    IRegionStage _pendingHitStage;
    IRegionStage _autoAttackStage;
    IRegionStage _mobAttackStage;

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

    /// <summary>Test/diagnostics: wait until every sealed fan-out batch has been delivered.</summary>
    internal void WaitForFanoutIdle() => _fanoutDispatcher.WaitUntilIdle();

    /// <summary>Test hook: one full scheduler step, then waits for async fan-out to finish.</summary>
    internal void RunStepForTest()
    {
        StepSimulation();
        _fanoutDispatcher.WaitUntilIdle();
    }

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
        try
        {
            WithDeferredInventoryPersist(() =>
            {
                _fanout.ConfigureSequence(_simulationTicks.Tick, 0);
                _fanout.Begin();
                try
                {
                    body(_simulationClock.WallUnixMs());
                }
                finally
                {
                    _fanout.Seal();
                }

                return 0;
            });

            AwaitTestFanoutDelivery();
        }
        finally
        {
            if (outermost)
            {
                AwaitTestFanoutDelivery();
                var stranded = _ingress.TakeApplied();
                if (stranded.Count > 0)
                    ZoneCommandIngress.Complete(stranded);
            }
        }
    }

    /// <summary>
    /// After the <c>commands</c> stage: commit the economy those commands dirtied, seal and send their fan-out, then
    /// complete replies. A failed ledger commit does not tell those characters success. Returns
    /// (flush, fanout seal+drain, reply completion) milliseconds. Must run only inside a <see cref="RunStep"/> body
    /// while a fan-out batch is recording.
    /// </summary>
    (double FlushMs, double SealMs, double ReplyMs) ReleaseAppliedCommandReplies()
    {
        long flushStart = Stopwatch.GetTimestamp();
        bool committed;
        HashSet<int>? uncommitted = null;
        lock (_economyLock)
        {
            committed = FlushDirtyInventories();
            if (!committed)
                uncommitted = UncommittedEconomicCharacters();
        }

        if (uncommitted != null)
            _ingress.FailUncommitted(uncommitted, EconomyCommitFailed);
        double flushMs = RecordCommandPhase(PhaseFlush, flushStart);

        long sealStart = Stopwatch.GetTimestamp();
        double enqueueMs = _fanout.Seal(sealStart);
        double sealMs = RecordCommandPhase(PhaseSeal, sealStart);
        double prepMs = Math.Max(0, sealMs - enqueueMs);
        FineageTelemetry.CommandTimelineMilliseconds.Record(flushMs + prepMs,
            new KeyValuePair<string, object?>("phase", "apply_to_seal"));
        FineageTelemetry.CommandTimelineMilliseconds.Record(enqueueMs,
            new KeyValuePair<string, object?>("phase", "seal_to_fanout_enqueue"));

        AwaitTestFanoutDelivery();

        long replyStart = Stopwatch.GetTimestamp();
        var applied = _ingress.TakeApplied();
        if (applied.Count > 0)
            ZoneCommandIngress.Complete(applied);
        _fanout.Begin();
        double replyMs = RecordCommandPhase(PhaseReply, replyStart);
        return (flushMs, sealMs, replyMs);
    }

    HashSet<int> UncommittedEconomicCharacters()
    {
        lock (_economyLock)
        {
            var ids = new HashSet<int>(
                _dirtyInventories.Count + _dirtyWarehouses.Count + _dirtyFreight.Count + _dirtyOfflineFreight.Count);
            foreach (int id in _dirtyInventories.Keys)
                ids.Add(id);
            foreach (int id in _dirtyWarehouses.Keys)
                ids.Add(id);
            foreach (int id in _dirtyFreight.Keys)
                ids.Add(id);
            foreach (var bag in _dirtyOfflineFreight)
                ids.Add(bag.CharacterId);
            return ids;
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
        nameof(_aggressionStage), nameof(_randomWalkStage), nameof(_playerMovementStage), nameof(_npcMovementStage),
        nameof(_borders), nameof(_pendingHitStage), nameof(_autoAttackStage), nameof(_mobAttackStage))]
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
        _playerMovementStage = new PlayerMovementStage(this);
        _npcMovementStage = new NpcMovementStage(this);
        _pendingHitStage = new RegionStage("pending-hits", CollectPendingHits, (tick, id) => PendingHitEntity(id, tick.NowUnixMs),
            parallel: true);
        _autoAttackStage = new RegionStage("auto-attacks", CollectAutoAttackers, (tick, id) => AutoAttackEntity(id, tick.NowUnixMs),
            parallel: true);
        _mobAttackStage = new RegionStage("mob-attacks", CollectAiBusy, (tick, id) => MobAttackEntity(id, tick.NowUnixMs),
            parallel: true);

        int slow = ZoneSimulationSettings.SlowPeriodTicks;
        // Every region body runs in a RegionBodyScope, so the executor cannot change the outcome; it only decides
        // whether the stages that allow it spread their regions over worker threads.
        var ticks = new RegionTickScheduler(_simulationClock, executor, observer, new RegionScopes(this));

        // Phase 0: player commands submitted since the last step.
        ticks.AddWorldStage(CommandsStage, ApplyCommands);
        ticks.AddWorldStage("game-time", tick => TickClientGameTime(tick.WallUnixMs));

        // Every step: the former 200 ms combat loop, in its original order.
        ticks.AddRegionStage(_playerMovementStage);
        ticks.AddWorldStage("arena", _ => TickArenaZones());
        ticks.AddWorldStage("npc.paths", TickNpcPathfindStage);
        ticks.AddRegionStage(_npcMovementStage);
        ticks.AddRegionStage(_randomWalkStage);
        ticks.AddRegionStage(_pendingHitStage);
        ticks.AddWorldStage("melee-hits", tick => TickMeleeHits(tick.NowUnixMs));
        ticks.AddWorldStage("pvp-flags", tick => TickPvpFlags(tick.NowUnixMs));
        ticks.AddRegionStage(_autoAttackStage);
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
        ticks.AddRegionStage(_mobAttackStage);
        ticks.AddWorldStage("summons", tick => TickSummons(tick.NowUnixMs));
        ticks.AddWorldStage("cubics", tick => TickCubics(tick.NowUnixMs));
        ticks.AddWorldStage("duels", tick => TickDuels(tick.NowUnixMs));
        ticks.AddWorldStage("corpse-decay", tick => TickCorpseDecay(tick.NowUnixMs));
        ticks.AddWorldStage("attack-stance", tick => TickAttackStance(tick.NowUnixMs));
        ticks.AddWorldStage("force-charge-decay", tick => TickForceChargeDecay(tick.NowUnixMs));
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
        ticks.AddWorldStage(FishingChampionshipActor.StageName, tick => _fishingChampionship.Step(tick), slow);
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
