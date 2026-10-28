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
                }
                await FlushOthersAsync(outbound, ct).ConfigureAwait(false);
                FineageTelemetry.TickMilliseconds.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    void TickRegen(List<Pending> outbound)
    {
        foreach (var entity in Players())
        {
            if (entity.CurHp >= entity.MaxHp && entity.CurMp >= entity.MaxMp)
                continue;

            int hpGain = entity.CurHp < entity.MaxHp
                ? ZoneRegen.HpPerTick(entity.MaxHp, entity.Level, entity.Sitting)
                : 0;
            int mpGain = entity.CurMp < entity.MaxMp
                ? ZoneRegen.MpPerTick(entity.MaxMp, entity.Level, entity.Sitting)
                : 0;
            if (hpGain == 0 && mpGain == 0)
                continue;

            if (hpGain > 0)
                entity.CurHp = Math.Min(entity.MaxHp, entity.CurHp + hpGain);
            if (mpGain > 0)
                entity.CurMp = Math.Min(entity.MaxMp, entity.CurMp + mpGain);

            FanoutVitals(entity, PlayerVitalsStatus(entity), outbound);
        }
    }

    void FanoutVitals(ZoneEntity subject, StatusUpdateMsg status, List<Pending> outbound)
    {
        byte[] body = MessagePackSerializer.Serialize(status);
        foreach (var watcher in Players())
        {
            if (watcher.Session == null)
                continue;
            if (ReferenceEquals(watcher, subject) || watcher.Knownlist.Contains(subject.ObjectId))
                outbound.Add(new Pending(watcher.Session, MsgType.StatusUpdate, body));
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

        if (ClassTemplates.TryGet(session.ClassId, out var template) && template != null)
            return (template.MaxHp, template.MaxHp, template.MaxMp, template.MaxMp, 1, template.Race, 0);

        return (ClassTemplates.HumanFighterHp, ClassTemplates.HumanFighterHp, ClassTemplates.HumanFighterMp,
            ClassTemplates.HumanFighterMp, 1, 0, 0);
    }

    static (int maxHp, int maxMp) ResolveMaxima(CharacterRecord record)
    {
        if (record.MaxHp > 0 && record.MaxMp > 0)
            return (record.MaxHp, record.MaxMp);
        if (ClassTemplates.TryGet(record.ClassId, out var template) && template != null)
            return (template.MaxHp, template.MaxMp);
        return (ClassTemplates.HumanFighterHp, ClassTemplates.HumanFighterMp);
    }

    void SaveVitals(ZoneSession session, ZoneEntity entity)
    {
        if (_accounts == null || entity.Kind != ZoneEntityKind.Player)
            return;
        _accounts.TrySaveVitals(session.Account, session.ObjectId, entity.CurHp, entity.CurMp);
    }
}
