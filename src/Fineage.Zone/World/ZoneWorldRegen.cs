using System.Diagnostics;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Protocol.Telemetry;
using Fineage.Zone.Combat;
using MessagePack;

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
                var outbound = new List<Pending>();
                lock (_gate)
                {
                    TickRegen(outbound);
                    TickSpawns(outbound);
                    TickAbnormals(outbound);
                    TickBosses(outbound);
                    CompleteDueDissolutions(_clock().ToUnixTimeMilliseconds(), outbound);
                    TickSevenSigns(outbound);
                    TickOlympiad(outbound);
                }
                await FlushOthersAsync(outbound, ct).ConfigureAwait(false);
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
        var outbound = new List<Pending>();
        lock (_gate)
            TickRegen(outbound);
        await FlushOthersAsync(outbound, ct).ConfigureAwait(false);
    }

    void TickRegen(List<Pending> outbound)
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
            double hpRate = mods.Apply(CombatStat.RegHp, entity.CurHp < entity.MaxHp
                ? (double)ZoneRegen.HpPerTick(entity.MaxHp, entity.Level, entity.Sitting)
                : 0d);
            double mpRate = mods.Apply(CombatStat.RegMp, entity.CurMp < entity.MaxMp
                ? (double)ZoneRegen.MpPerTick(entity.MaxMp, entity.Level, entity.Sitting)
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

            FanoutVitals(entity, PlayerVitalsStatus(entity), outbound);
        }
    }

    void FanoutVitals(ZoneEntity subject, StatusUpdateMsg status, List<Pending> outbound)
    {
        byte[] body = MessagePackSerializer.Serialize(status);
        List<int>? netWatchers = null;
        foreach (var watcher in Players())
        {
            if (watcher.Session == null)
                continue;
            if (!ReferenceEquals(watcher, subject) && !watcher.Knownlist.Contains(subject.ObjectId))
                continue;
            outbound.Add(new Pending(watcher.Session, MsgType.StatusUpdate, body));
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
        if (_accounts == null || entity.Kind != ZoneEntityKind.Player)
            return;
        _accounts.TrySaveVitals(session.Account, session.ObjectId, entity.CurHp, entity.CurMp);
    }
}
