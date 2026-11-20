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
    /// <summary>
    /// Mobius <c>BaseStat.CON.calcBonus</c> / <c>MEN.calcBonus</c> for this player's class, read from
    /// <c>data/stat-bonuses.json</c>, including henna.
    /// </summary>
    internal (double Con, double Men) RegenStatBonuses(ZoneEntity entity)
    {
        if (PlayerClasses == null || !PlayerClasses.TryGet(entity.ClassId, out var template) || template == null)
            return (1d, 1d);
        var attrs = EffectiveAttributes(entity, template);
        return (attrs.ConBonus, attrs.MenBonus);
    }

    /// <summary>
    /// Mobius <c>NPCStatus.doRegeneration</c>: a wounded NPC heals
    /// <c>max(1, hpRegen)</c> HP / <c>max(1, mpRegen)</c> MP per 3 s tick, capped at the maximums. Without it a
    /// mob that survived a fight stayed wounded forever until it died (audit <c>D30</c>).
    /// </summary>
    void CollectRegenNpcs(TickContext tick, RegionWorkCollector work)
    {
        if (!_npcRegen || _needRegen.Count == 0)
            return;
        foreach (int id in _needRegen.ToArray())
        {
            if (!_byId.TryGetValue(id, out var entity))
            {
                _needRegen.Remove(id);
                continue;
            }
            work.Add(id, entity.InstanceId, entity.X, entity.Y);
        }
    }

    void RegenNpc(int id)
    {
        if (!_byId.TryGetValue(id, out var npc) || npc.Kind != ZoneEntityKind.Npc || npc.Dead || npc.CurHp <= 0)
        {
            _needRegen.Remove(id);
            return;
        }
        if (npc.CurHp >= npc.MaxHp && npc.CurMp >= npc.MaxMp)
        {
            SyncNeedRegen(npc);
            return;
        }

        // DoT (poison/bleed) must outpace NPC regen or StatusUpdate HP looks stuck at full.
        bool hasDot = npc.Abnormals.Any(a => _skillDots.TryGet(a.Id, out _));
        npc.RegenCarryHp += !hasDot && npc.CurHp < npc.MaxHp ? Math.Max(1d, npc.HpRegen) : 0d;
        npc.RegenCarryMp += npc.CurMp < npc.MaxMp ? Math.Max(1d, npc.MpRegen) : 0d;
        int hpGain = (int)Math.Floor(npc.RegenCarryHp);
        int mpGain = (int)Math.Floor(npc.RegenCarryMp);
        npc.RegenCarryHp -= hpGain;
        npc.RegenCarryMp -= mpGain;
        if (hpGain == 0 && mpGain == 0)
            return;

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

    void FanoutVitals(ZoneEntity subject, StatusUpdateMsg status)
    {
        SyncNeedRegen(subject);
        if (subject.DuelId != 0)
            SendDuelUserInfo(subject);
        var netWatchers = NetWatchersOf(subject);

        if (netWatchers.Count > 0 && NetBridge != null)
        {
            NetBridge.FanoutStatusUpdate(netWatchers, status);
        }
        FanPartyVitals(subject);
    }

    static StatusUpdateMsg PlayerVitalsStatus(ZoneEntity entity) => new()
    {
        ObjectId = entity.ObjectId,
        AttributeIds =
        [
            InterludeStatus.CurHp,
            InterludeStatus.MaxHp,
            InterludeStatus.CurMp,
            InterludeStatus.MaxMp,
            InterludeStatus.CurCp,
            InterludeStatus.MaxCp
        ],
        Values = [entity.CurHp, entity.MaxHp, entity.CurMp, entity.MaxMp, entity.CurCp, entity.MaxCp]
    };

    (int hp, int maxHp, int mp, int maxMp, int cp, int maxCp, int level, int race, int sex) LoadVitals(
        ZoneSession session,
        CharacterRecord? stored = null)
    {
        if (stored != null)
        {
            var (maxHp, maxMp, maxCp) = ResolveMaxima(stored);
            // MaxHp/MaxMp/MaxCp > 0 means a previous logout wrote the bar, including 0 current (dead).
            int hp = RestoreBar(stored.Hp, stored.MaxHp, maxHp);
            int mp = RestoreBar(stored.Mp, stored.MaxMp, maxMp);
            int cp = RestoreBar(stored.Cp, stored.MaxCp, maxCp);
            int level = stored.Level > 0 ? stored.Level : 1;
            return (hp, maxHp, mp, maxMp, cp, maxCp, level, stored.Race, stored.Sex);
        }

        if (PlayerClasses.TryGet(session.ClassId, out var template) && template != null)
            return (template.MaxHp, template.MaxHp, template.MaxMp, template.MaxMp, template.MaxCp, template.MaxCp, 1, template.Race, 0);

        return (ClassTemplates.HumanFighterHp, ClassTemplates.HumanFighterHp, ClassTemplates.HumanFighterMp,
            ClassTemplates.HumanFighterMp, 0, 0, 1, 0, 0);
    }

    static int RestoreBar(int storedCurrent, int storedMax, int liveMax)
    {
        if (liveMax <= 0)
            return 0;
        if (storedMax > 0)
            return Math.Clamp(storedCurrent, 0, liveMax);
        if (storedCurrent > 0)
            return Math.Min(storedCurrent, liveMax);
        return liveMax;
    }

    (int maxHp, int maxMp, int maxCp) ResolveMaxima(CharacterRecord record)
    {
        if (PlayerClasses.TryGet(record.ClassId, out var template) && template != null)
        {
            int level = record.Level > 0 ? record.Level : 1;
            int maxHp = record.MaxHp > 0 ? record.MaxHp : template.ModifiedHp(level);
            int maxMp = record.MaxMp > 0 ? record.MaxMp : template.ModifiedMp(level);
            int maxCp;
            try { maxCp = record.MaxCp > 0 ? record.MaxCp : template.ModifiedCp(level); }
            catch (ArgumentOutOfRangeException) { maxCp = record.MaxCp > 0 ? record.MaxCp : template.MaxCp; }
            return (maxHp, maxMp, maxCp);
        }

        return (record.MaxHp > 0 ? record.MaxHp : ClassTemplates.HumanFighterHp,
            record.MaxMp > 0 ? record.MaxMp : ClassTemplates.HumanFighterMp,
            Math.Max(0, record.MaxCp));
    }

    void SaveVitals(ZoneSession session, ZoneEntity entity)
    {
        if (entity.Kind != ZoneEntityKind.Player)
            return;
        _persist.SaveVitals(session.Account, session.ObjectId, entity.CurHp, entity.CurMp, entity.CurCp);
    }
}
