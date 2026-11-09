using System.Diagnostics;
using Fineage.Game;
using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Protocol.Telemetry;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    /// <summary>
    /// Mobius <c>NPCStatus.doRegeneration</c>: a wounded NPC heals
    /// <c>max(1, hpRegen)</c> HP / <c>max(1, mpRegen)</c> MP per 3 s tick, capped at the maximums. Without it a
    /// mob that survived a fight stayed wounded forever until it died (audit <c>D30</c>).
    /// </summary>
    /// <summary>
    /// Mobius <c>BaseStat.CON.calcBonus</c> / <c>MEN.calcBonus</c> for this player's class, read from
    /// <c>data/stat-bonuses.json</c>, including henna. Out-of-range values clamp+warn via <c>StatBonus</c>.
    /// </summary>
    internal (double Con, double Men) RegenStatBonuses(ZoneEntity entity)
    {
        if (PlayerClasses == null || !PlayerClasses.TryGet(entity.ClassId, out var template) || template == null)
            return (1d, 1d);
        var table = PlayerClasses.StatBonuses;
        return (StatBonus(table, StatSlot.Con, template.Con + entity.HennaCon),
            StatBonus(table, StatSlot.Men, template.Men + entity.HennaMen));
    }

    void TickNpcRegen()
    {
        if (!_npcRegen || _needRegen.Count == 0)
            return;
        foreach (int id in _needRegen.ToArray())
        {
            if (!_byId.TryGetValue(id, out var npc) || npc.Kind != ZoneEntityKind.Npc || npc.Dead || npc.CurHp <= 0)
            {
                _needRegen.Remove(id);
                continue;
            }
            if (npc.CurHp >= npc.MaxHp && npc.CurMp >= npc.MaxMp)
            {
                SyncNeedRegen(npc);
                continue;
            }

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

            SyncNeedRegen(npc);
            FanoutVitals(npc, StatusFor(npc));
        }
    }

    void FanoutVitals(ZoneEntity subject, StatusUpdateMsg status)
    {
        SyncNeedRegen(subject);
        List<int>? netWatchers = null;
        foreach (var watcher in Players())
        {
            if (watcher.Session == null)
                continue;
            if (!ReferenceEquals(watcher, subject) && !watcher.Knownlist.Contains(subject.ObjectId))
                continue;
            netWatchers ??= [];            netWatchers.Add(watcher.ObjectId);
        }

        if (netWatchers is { Count: > 0 } && NetBridge != null)
        {
            NetBridge.FanoutStatusUpdate(netWatchers, status);
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

    (int hp, int maxHp, int mp, int maxMp, int level, int race, int sex) LoadVitals(
        ZoneSession session,
        CharacterRecord? stored = null)
    {
        if (stored != null)
        {
            var (maxHp, maxMp) = ResolveMaxima(stored);
            int hp = stored.Hp > 0 ? Math.Min(stored.Hp, maxHp) : maxHp;
            int mp = stored.Mp > 0 ? Math.Min(stored.Mp, maxMp) : maxMp;
            int level = stored.Level > 0 ? stored.Level : 1;
            return (hp, maxHp, mp, maxMp, level, stored.Race, stored.Sex);
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
