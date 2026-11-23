using System.Diagnostics;
using Fineage.Game;
using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Protocol.Telemetry;
using Fineage.Zone.Combat;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    Task? _simulation;
    long _stepStartedTimestamp;
    string _stepPhase = "idle";

    internal string StepPhase => Volatile.Read(ref _stepPhase);

    internal string EconomyLockDescription => _economyLock.Describe();

    internal string FanoutQueueDescription => _fanoutDispatcher.DescribeQueue();

    void NotePhase(string phase) => Volatile.Write(ref _stepPhase, phase);

    public void StartSimulation(CancellationToken shutdown)
    {
        if (_simulation != null)
            return;
        _affinity.Drive();
        _snapshots.Start();
        _economyPersist?.Start();
        _simulation = Task.Run(() => SimulationLoopAsync(shutdown));
        _ = Task.Run(() => StepWatchdogAsync(shutdown));
    }

    /// <summary>Test hook: production affinity (handlers must go through the ingress) without starting the loop.</summary>
    internal void DriveForTest() => _affinity.Drive();

    /// <summary>
    /// What the watchdog logs when the current step outlasts <see cref="ZoneSimulationSettings.StepWatchdogSeconds"/>:
    /// the tick, how long it has run, and the region bodies still in flight (an empty list names a world stage, a
    /// collect step or the barrier). Null while no step is overdue or none runs.
    /// </summary>
    internal string? StepWatchdogReport(TimeSpan? threshold = null)
    {
        long started = Volatile.Read(ref _stepStartedTimestamp);
        if (started == 0)
            return null;
        var elapsed = Stopwatch.GetElapsedTime(started);
        if (elapsed < (threshold ?? TimeSpan.FromSeconds(ZoneSimulationSettings.StepWatchdogSeconds)))
            return null;
        var bodies = _simulationTicks.InFlightRegionBodies;
        string detail = bodies.Count == 0
            ? "no region bodies in flight: a world stage, a collect step or the stage barrier is stuck"
            : "region bodies still running: " + string.Join("; ", bodies);
        string work = _simulationTicks.CurrentWork ?? "none";
        return $"simulation step tick={_simulationTicks.Tick} has run {elapsed.TotalSeconds:F1}s without finishing: {detail}; phase={StepPhase} work={work}";
    }

    /// <summary>
    /// The step that never returns used to be indistinguishable from an idle world: the loop sat in
    /// <see cref="StepSimulation"/> and nothing was logged. The watchdog turns it into a concrete error naming the
    /// stuck stage and region bodies, and repeats while the step lasts.
    /// </summary>
    async Task StepWatchdogAsync(CancellationToken ct)
    {
        string? last = null;
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
                long startedNow = Volatile.Read(ref _stepStartedTimestamp);
                if (startedNow != 0)
                    FineageTelemetry.NoteTickRunning(Stopwatch.GetElapsedTime(startedNow).TotalMilliseconds);
                string? report = StepWatchdogReport();
                if (report == null)
                {
                    last = null;
                    continue;
                }
                if (report != last)
                {
                    bool newStall = last == null;
                    last = report;
                    if (newStall)
                    {
                        FineageTelemetry.NoteWatchdogEpisode();
                        FineageTelemetry.StageFailures.Add(1, new KeyValuePair<string, object?>("stage", "step-watchdog"));
                    }

                    long started = Volatile.Read(ref _stepStartedTimestamp);
                    if (started != 0)
                        FineageTelemetry.NoteWatchdogStall(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                    ZoneHotPathLogs.SimulationWatchdog(report);
                    if (newStall)
                        ZoneHotPathLogs.SimulationWatchdog(StepHangDiagnostics.Capture(this));
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Below this gap the next tick is close enough that phase 0 of the step itself admits the queue. A slice
    /// any shorter trips the timebox's "deadline already passed" path.
    /// </summary>
    const int AdmissionGapMs = 5;

    /// <summary>
    /// The live clock: starts at the current tick (the world may have been built a while ago), then wakes on each tick
    /// boundary of the wall clock so tick time and the wall time handlers read stay a few milliseconds apart. A queued
    /// command wakes the same loop early and is admitted through <see cref="AdmitQueuedCommands"/> before that boundary.
    /// </summary>
    async Task SimulationLoopAsync(CancellationToken ct)
    {
        using (_affinity.Step())
            _simulationTicks.Resume(_simulationClock.WallUnixMs());
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                long wait = _simulationTicks.MillisecondsUntilNextTick(_simulationClock.WallUnixMs());
                NotePhase("waiting-for-tick");
                if (wait >= AdmissionGapMs && _ingress.QueuedDepth > 0)
                {
                    AdmitQueuedCommands(wait);
                    continue;
                }

                if (wait > 0 && !await WaitForTickOrCommandAsync(wait, ct).ConfigureAwait(false))
                    continue;

                long started = Stopwatch.GetTimestamp();
                NotePhase("step");
                Volatile.Write(ref _stepStartedTimestamp, started);
                try
                {
                    StepSimulation();
                }
                catch (Exception ex)
                {
                    // Stage bodies are isolated by the scheduler; what reaches here is the deferred Postgres flush or an observer fault.
                    // Movement, combat and regen all live on this loop, so one bad save must not end it.
                    FineageTelemetry.StageFailures.Add(1, new KeyValuePair<string, object?>("stage", "step"));
                    ZoneHotPathLogs.SimulationStepFailedOutsideStages(_simulationTicks.Tick, ex);
                }
                finally
                {
                    Volatile.Write(ref _stepStartedTimestamp, 0L);
                    FineageTelemetry.ClearTickRunning();
                }
                FineageTelemetry.NoteTickDuration(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            ShutdownSaveOnlinePlayers();
            _snapshots.Stop();
            _economyPersist?.Stop();
            FlushLedgerOnShutdown();
            CloseCommandIngress($"the zone simulation stopped at tick {_simulationTicks.Tick}");
        }
    }

    /// <summary>One simulation regen tick on demand (tests and admin diagnostics), same stages the loop runs.</summary>
    internal async Task RunRegenTickAsync(CancellationToken ct)
    {
        RunStep(now =>
        {
            _simulationTicks.RunNow(_regenPlayersStage, now);
            _simulationTicks.RunNow(_regenNpcsStage, now);
        });
        _fanoutDispatcher.WaitUntilIdle();
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// Mobius <c>CreatureStatus.doRegeneration</c> posture for a player: <c>isSitting</c> first, then <c>!isMoving</c>,
    /// then <c>isRunning</c>. A moving walker matches no branch, so Mobius gives it no multiplier.
    /// </summary>
    static ZoneRegen.Posture RegenPosture(ZoneEntity entity)
    {
        if (entity.Sitting)
            return ZoneRegen.Posture.Sitting;
        if (!entity.MoveActive)
            return ZoneRegen.Posture.Standing;
        return entity.Running ? ZoneRegen.Posture.Running : ZoneRegen.Posture.Walking;
    }

    /// <summary>
    /// Per-tick CP this player regenerates right now (Mobius <c>calcCpRegen</c>), fed through the same folds
    /// <see cref="TickRegen"/> uses. Tests read the real rate instead of hard-coding it.
    /// </summary>
    internal double CpRegenRate(ZoneEntity player)
    {
        var (conBonus, _) = RegenStatBonuses(player);
        return CpRegenRate(player, PlayerStatMods(player), RegenPosture(player), conBonus);
    }

    static double CpRegenRate(ZoneEntity player, CombatStatMods mods, ZoneRegen.Posture posture, double conBonus) =>
        mods.Apply(CombatStat.RegCp, ZoneRegen.CpInit(player.Level, conBonus)) * ZoneRegen.PostureMultiplier(posture);

    /// <summary>
    /// Per-tick HP/MP this player regenerates right now, fed through the same folds <see cref="TickRegen"/> uses.
    /// Tests read the real rate instead of hard-coding it, so a formula change cannot leave them stale.
    /// </summary>
    internal (double Hp, double Mp) RegenRate(ZoneEntity player)
    {
        var mods = PlayerStatMods(player);
        var posture = RegenPosture(player);
        var (conBonus, menBonus) = RegenStatBonuses(player);
        var (hallHp, hallMp) = HallRegenBonus(player);
        var (mtHp, mtMp) = MotherTreeRegenBonus(player);
        double hp = mods.Apply(CombatStat.RegHp, ZoneRegen.HpPerTick(player.Level, conBonus, posture) * hallHp);
        double mp = mods.Apply(CombatStat.RegMp, ZoneRegen.MpPerTick(player.Level, menBonus, posture) * hallMp);
        if (mtHp > 0)
            hp += mtHp;
        if (mtMp > 0)
            mp += mtMp;
        return (hp, mp);
    }

    void CollectRegenPlayers(TickContext tick, RegionWorkCollector work)
    {
        foreach (var entity in Players())
            work.Add(_byId.OwnerOf(entity).Region, entity.ObjectId);
    }

    /// <summary>Mobius <c>Formulas.calcHpRegen</c> / <c>calcMpRegen</c> flat mother-tree add-ons after rate × multiplier.</summary>
    internal (double Hp, double Mp) MotherTreeRegenBonusForTest(ZoneEntity player) =>
        MotherTreeRegenBonus(player);

    (double Hp, double Mp) MotherTreeRegenBonus(ZoneEntity player)
    {
        if (MotherTree.Count == 0)
            return (0, 0);
        var zone = MotherTree.At(player.X, player.Y, player.Z);
        if (zone == null)
            return (0, 0);
        if (zone.AffectedRace >= 0 && player.Race != zone.AffectedRace)
            return (0, 0);
        return (zone.HpRegenBonus, zone.MpRegenBonus);
    }

    void RegenPlayer(int objectId)
    {
        if (!_byId.TryGetValue(objectId, out var entity) || entity.Dead)
            return;
        // Mobius PlayerStatus.doRegeneration regenerates CP in the same tick as HP/MP.
        if (entity.CurHp >= entity.MaxHp && entity.CurMp >= entity.MaxMp && entity.CurCp >= entity.MaxCp)
            return;
        // Mobius Formulas.calcHpRegen/calcMpRegen: (calcStat(REGENERATE_HP_RATE, max(1, init), …) * multiplier).
        // Folding the stat means Weight Penalty 4270's regHp/regMp ×0.5 halves the rate, and the regen masteries
        // (Fast HP Recovery, Mana Recovery, Vital Force, …) raise it, through the same pipeline as everything else.
        var mods = PlayerStatMods(entity);
        var posture = RegenPosture(entity);
        var (conBonus, menBonus) = RegenStatBonuses(entity);
        var (hallHp, hallMp) = HallRegenBonus(entity);
        var (mtHp, mtMp) = MotherTreeRegenBonus(entity);
        double hpRate = mods.Apply(CombatStat.RegHp, entity.CurHp < entity.MaxHp
            ? ZoneRegen.HpPerTick(entity.Level, conBonus, posture) * hallHp
            : 0d);
        double mpRate = mods.Apply(CombatStat.RegMp, entity.CurMp < entity.MaxMp
            ? ZoneRegen.MpPerTick(entity.Level, menBonus, posture) * hallMp
            : 0d);
        if (mtHp > 0)
            hpRate += mtHp;
        if (mtMp > 0)
            mpRate += mtMp;
        double cpRate = entity.CurCp < entity.MaxCp ? CpRegenRate(entity, mods, posture, conBonus) : 0d;

        // Mobius keeps HP/MP as doubles, so a halved rate still accumulates. Vitals here are ints, so the
        // fractional remainder is carried per entity instead of being truncated away every tick.
        // A negative folded rate (a debuff subtracting more than the base) is floored at 0 rather than letting the
        // carry drift negative and flip into healing on a later tick.
        entity.RegenCarryHp += Math.Max(0d, hpRate);
        entity.RegenCarryMp += Math.Max(0d, mpRate);
        entity.RegenCarryCp += Math.Max(0d, cpRate);
        int hpGain = (int)Math.Floor(entity.RegenCarryHp);
        int mpGain = (int)Math.Floor(entity.RegenCarryMp);
        int cpGain = (int)Math.Floor(entity.RegenCarryCp);
        entity.RegenCarryHp -= hpGain;
        entity.RegenCarryMp -= mpGain;
        entity.RegenCarryCp -= cpGain;
        if (hpGain == 0 && mpGain == 0 && cpGain == 0)
            return;

        if (hpGain > 0)
            entity.CurHp = Math.Min(entity.MaxHp, entity.CurHp + hpGain);
        if (mpGain > 0)
            entity.CurMp = Math.Min(entity.MaxMp, entity.CurMp + mpGain);
        if (cpGain > 0)
            entity.CurCp = Math.Min(entity.MaxCp, entity.CurCp + cpGain);
        if (entity.CurHp >= entity.MaxHp)
            entity.RegenCarryHp = 0;
        if (entity.CurCp >= entity.MaxCp)
            entity.RegenCarryCp = 0;
        if (entity.CurMp >= entity.MaxMp)
            entity.RegenCarryMp = 0;

        FanoutVitals(entity, PlayerVitalsStatus(entity));
    }

}
