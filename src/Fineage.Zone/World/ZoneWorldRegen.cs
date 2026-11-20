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

    public void StartSimulation(CancellationToken shutdown)
    {
        if (_simulation != null)
            return;
        _simulation = Task.Run(() => SimulationLoopAsync(shutdown));
    }

    /// <summary>
    /// The live clock: starts at the current tick (the world may have been built a while ago), then wakes on each tick
    /// boundary of the wall clock so tick time and the wall time handlers read stay a few milliseconds apart.
    /// </summary>
    async Task SimulationLoopAsync(CancellationToken ct)
    {
        lock (_gate)
            _simulationTicks.Resume(_simulationClock.WallUnixMs());
        try
        {
            while (true)
            {
                long wait = _simulationTicks.MillisecondsUntilNextTick(_simulationClock.WallUnixMs());
                await Task.Delay(TimeSpan.FromMilliseconds(wait), ct).ConfigureAwait(false);
                long started = Stopwatch.GetTimestamp();
                try
                {
                    StepSimulation();
                }
                catch (Exception ex)
                {
                    // Stage bodies are isolated by the scheduler; what reaches here is the deferred Postgres flush or an observer fault.
                    // Movement, combat and regen all live on this loop, so one bad save must not end it.
                    FineageTelemetry.StageFailures.Add(1, new KeyValuePair<string, object?>("stage", "step"));
                    ZoneLog.Warn($"simulation step tick={_simulationTicks.Tick} failed outside stage isolation: {ex}");
                }
                FineageTelemetry.TickMilliseconds.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _ingress.Close($"the zone simulation stopped at tick {_simulationTicks.Tick}");
        }
    }

    /// <summary>One simulation regen tick on demand (tests and admin diagnostics), same stages the loop runs.</summary>
    internal async Task RunRegenTickAsync(CancellationToken ct)
    {
        RunUnderGate(now =>
        {
            _simulationTicks.RunNow(_regenPlayersStage, now);
            _simulationTicks.RunNow(_regenNpcsStage, now);
        });
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
        return (
            mods.Apply(CombatStat.RegHp, ZoneRegen.HpPerTick(player.Level, conBonus, posture)),
            mods.Apply(CombatStat.RegMp, ZoneRegen.MpPerTick(player.Level, menBonus, posture)));
    }

    void CollectRegenPlayers(TickContext tick, RegionWorkCollector work)
    {
        foreach (var entity in Players())
            work.Add(entity.ObjectId, entity.InstanceId, entity.X, entity.Y);
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
        double hpRate = mods.Apply(CombatStat.RegHp, entity.CurHp < entity.MaxHp
            ? ZoneRegen.HpPerTick(entity.Level, conBonus, posture)
            : 0d);
        double mpRate = mods.Apply(CombatStat.RegMp, entity.CurMp < entity.MaxMp
            ? ZoneRegen.MpPerTick(entity.Level, menBonus, posture)
            : 0d);
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
