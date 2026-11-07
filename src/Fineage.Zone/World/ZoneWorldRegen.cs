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

    /// <summary>
    /// Mobius <c>NPCStatus.doRegeneration</c>: a wounded NPC heals
    /// <c>max(1, hpRegen)</c> HP / <c>max(1, mpRegen)</c> MP per 3 s tick, capped at the maximums. Without it a
    /// mob that survived a fight stayed wounded forever until it died (audit <c>D30</c>).
    /// </summary>
    /// <summary>
    /// Mobius <c>BaseStat.CON.calcBonus</c> / <c>MEN.calcBonus</c> for this player's class, read from
    /// <c>data/stat-bonuses.json</c>. 1 when the class or the stat value is outside the table.
    /// </summary>
    internal (double Con, double Men) RegenStatBonuses(ZoneEntity entity)
    {
        if (PlayerClasses == null || !PlayerClasses.TryGet(entity.ClassId, out var template) || template == null)
            return (1d, 1d);
        var table = PlayerClasses.StatBonuses;
        return (BonusOrOne(table, StatSlot.Con, template.Con), BonusOrOne(table, StatSlot.Men, template.Men));
    }

    static double BonusOrOne(StatBonusTable table, StatSlot slot, int value)
    {
        try { return table.Bonus(slot, value); }
        catch (ArgumentOutOfRangeException) { return 1d; }
    }

    void TickNpcRegen()
    {
        if (!_npcRegen)
            return;
        foreach (var npc in _byId.Values)
        {
            if (npc.Kind != ZoneEntityKind.Npc || npc.Dead || npc.CurHp <= 0)
                continue;
            if (npc.CurHp >= npc.MaxHp && npc.CurMp >= npc.MaxMp)
                continue;

            npc.RegenCarryHp += npc.CurHp < npc.MaxHp ? Math.Max(1d, npc.HpRegen) : 0d;
            npc.RegenCarryMp += npc.CurMp < npc.MaxMp ? Math.Max(1d, npc.MpRegen) : 0d;
            int hpGain = (int)Math.Floor(npc.RegenCarryHp);
            int mpGain = (int)Math.Floor(npc.RegenCarryMp);
            npc.RegenCarryHp -= hpGain;
            npc.RegenCarryMp -= mpGain;
            if (hpGain == 0 && mpGain == 0)
                continue;

            if (hpGain > 0)
                npc.CurHp = Math.Min(npc.MaxHp, npc.CurHp + hpGain);
            if (mpGain > 0)
                npc.CurMp = Math.Min(npc.MaxMp, npc.CurMp + mpGain);
            if (npc.CurHp >= npc.MaxHp)
                npc.RegenCarryHp = 0;
            if (npc.CurMp >= npc.MaxMp)
                npc.RegenCarryMp = 0;

            FanoutVitals(npc, StatusFor(npc));
        }
    }

    void FanoutVitals(ZoneEntity subject, StatusUpdateMsg status)
    {
        List<int>? netWatchers = null;
        foreach (var watcher in Players())
        {
            if (watcher.Session == null)
                continue;
            if (!ReferenceEquals(watcher, subject) && !watcher.Knownlist.Contains(subject.ObjectId))
                continue;
            netWatchers ??= [];
            netWatchers.Add(watcher.ObjectId);
        }

        if (netWatchers is { Count: > 0 } && NetBridge != null)
        {
            NetBridge.FanoutStatusUpdate(netWatchers, new Fineage.Net.Contract.StatusUpdateNetMsg
            {
                EntityValue = (ulong)(uint)status.ObjectId,
                EntityGeneration = 1,
                AttributeIds = status.AttributeIds ?? [],
                Values = status.Values ?? []
            });
        }
    }

    static StatusUpdateMsg PlayerVitalsStatus(ZoneEntity entity) => new()
    {
        ObjectId = entity.ObjectId,
        AttributeIds =
        [
            InterludeStatus.CurHp,
            InterludeStatus.MaxHp,
            InterludeStatus.CurMp,
            InterludeStatus.MaxMp
        ],
        Values = [entity.CurHp, entity.MaxHp, entity.CurMp, entity.MaxMp]
    };

    (int hp, int maxHp, int mp, int maxMp, int level, int race, int sex) LoadVitals(ZoneSession session)
    {
        if (_accounts != null)
        {
            var record = _accounts.FindById(session.Account, session.ObjectId);
            if (record != null)
            {
                var (maxHp, maxMp) = ResolveMaxima(record);
                int hp = record.Hp > 0 ? Math.Min(record.Hp, maxHp) : maxHp;
                int mp = record.Mp > 0 ? Math.Min(record.Mp, maxMp) : maxMp;
                int level = record.Level > 0 ? record.Level : 1;
                return (hp, maxHp, mp, maxMp, level, record.Race, record.Sex);
            }
        }

        if (PlayerClasses.TryGet(session.ClassId, out var template) && template != null)
            return (template.MaxHp, template.MaxHp, template.MaxMp, template.MaxMp, 1, template.Race, 0);

        return (ClassTemplates.HumanFighterHp, ClassTemplates.HumanFighterHp, ClassTemplates.HumanFighterMp,
            ClassTemplates.HumanFighterMp, 1, 0, 0);
    }

    (int maxHp, int maxMp) ResolveMaxima(CharacterRecord record)
    {
        if (record.MaxHp > 0 && record.MaxMp > 0)
            return (record.MaxHp, record.MaxMp);
        if (PlayerClasses.TryGet(record.ClassId, out var template) && template != null)
        {
            int level = record.Level > 0 ? record.Level : 1;
            return (template.ModifiedHp(level), template.ModifiedMp(level));
        }

        return (ClassTemplates.HumanFighterHp, ClassTemplates.HumanFighterMp);
    }

    void SaveVitals(ZoneSession session, ZoneEntity entity)
    {
        if (entity.Kind != ZoneEntityKind.Player)
            return;
        _persist.SaveVitals(session.Account, session.ObjectId, entity.CurHp, entity.CurMp);
    }
}
