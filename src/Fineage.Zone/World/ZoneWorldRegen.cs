using System.Diagnostics;
using Fineage.Game;
using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Protocol.Telemetry;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    Task? _simulation;

    public void StartSimulation(CancellationToken shutdown)
    {
        if (_simulation != null)
            return;
        _simulation = Task.Run(() => SimulationLoopAsync(shutdown));
        _combatLoop = Task.Run(() => CombatLoopAsync(shutdown));
    }

    async Task SimulationLoopAsync(CancellationToken ct)
    {
        int delayMs = ZoneSimulationSettings.TickMilliseconds;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(delayMs));
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                long started = Stopwatch.GetTimestamp();
                lock (_gate)
                {
                    TickRegen();
                    TickSpawns();
                    TickAbnormals();
                    TickBosses();
                    CompleteDueDissolutions(_clock().ToUnixTimeMilliseconds());
                    TickSevenSigns();
                    TickOlympiad();
                }
                await Task.CompletedTask.ConfigureAwait(false);
                FineageTelemetry.TickMilliseconds.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>One simulation regen tick on demand (tests and admin diagnostics), same body the loop runs.</summary>
    internal async Task RunRegenTickAsync(CancellationToken ct)
    {
        lock (_gate)
            TickRegen();
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

    void TickRegen()
    {
        foreach (var entity in Players())
        {
            if (entity.Dead)
                continue;
            if (entity.CurHp >= entity.MaxHp && entity.CurMp >= entity.MaxMp)
                continue;
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

            // Mobius keeps HP/MP as doubles, so a halved rate still accumulates. Vitals here are ints, so the
            // fractional remainder is carried per entity instead of being truncated away every tick.
            // A negative folded rate (a debuff subtracting more than the base) is floored at 0 rather than letting the
            // carry drift negative and flip into healing on a later tick.
            entity.RegenCarryHp += Math.Max(0d, hpRate);
            entity.RegenCarryMp += Math.Max(0d, mpRate);
            int hpGain = (int)Math.Floor(entity.RegenCarryHp);
            int mpGain = (int)Math.Floor(entity.RegenCarryMp);
            entity.RegenCarryHp -= hpGain;
            entity.RegenCarryMp -= mpGain;
            if (hpGain == 0 && mpGain == 0)
                continue;

            if (hpGain > 0)
                entity.CurHp = Math.Min(entity.MaxHp, entity.CurHp + hpGain);
            if (mpGain > 0)
                entity.CurMp = Math.Min(entity.MaxMp, entity.CurMp + mpGain);
            if (entity.CurHp >= entity.MaxHp)
                entity.RegenCarryHp = 0;
            if (entity.CurMp >= entity.MaxMp)
                entity.RegenCarryMp = 0;

            FanoutVitals(entity, PlayerVitalsStatus(entity));
        }

        TickNpcRegen();
    }

}
