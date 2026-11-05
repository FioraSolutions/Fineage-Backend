using Fineage.Game.Skills;
using Fineage.Protocol;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    void ApplyAbnormalFromSkill(
        SkillDefinition template,
        SkillLevelData levelData,
        ZoneEntity target,
        long nowUnixMs,
        List<Pending> outbound)
    {
        ZoneAbnormals.Apply(
            target.Abnormals,
            template.SkillId,
            levelData.AbnormalLevel,
            template.AbnormalTimeSeconds,
            nowUnixMs,
            levelData.Level);
        FanoutAbnormal(target, outbound);
    }

    void ApplyAbnormalPlaytest(
        ZoneEntity target,
        int effectId,
        int value,
        int durationSeconds,
        long nowUnixMs,
        List<Pending> outbound)
    {
        ZoneAbnormals.Apply(target.Abnormals, effectId, value, durationSeconds, nowUnixMs);
        FanoutAbnormal(target, outbound);
    }

    void TickAbnormals(List<Pending> outbound)
    {
        long now = _clock().ToUnixTimeMilliseconds();
        foreach (var entity in _byId.Values)
        {
            if (entity.Abnormals.Count == 0)
                continue;
            if (!ZoneAbnormals.TickExpired(entity.Abnormals, now))
                continue;
            FanoutAbnormal(entity, outbound);
        }
    }

    /// <summary>Test hook: run one abnormal expiry pass and fan-out clears.</summary>
    internal async Task RunAbnormalTickAsync(CancellationToken ct)
    {
        var outbound = new List<Pending>();
        lock (_gate)
            TickAbnormals(outbound);
        await FlushOthersAsync(outbound, ct).ConfigureAwait(false);
    }

    void FanoutAbnormal(ZoneEntity target, List<Pending> outbound)
    {
        var update = AbnormalFor(target);
        // Mobius: a buff/debuff that changes combat stats triggers UserInfo (status window P.Atk/P.Def/Spd)
        // and a fresh run speed (Wind Walk/Slow change how fast the pawn actually travels).
        bool statful = target.Abnormals.Any(a => _statEffects.TryGet(a.Id, out _));
        bool speedChanged = RefreshRunSpeed(target);
        if (speedChanged)
            FanoutNetMoveType(target);
        if ((statful || target.HasStatAbnormal) && target.Kind == ZoneEntityKind.Player && target.Session != null)
            SelfUserInfo(target);
        target.HasStatAbnormal = statful;
        List<int>? netWatchers = null;
        foreach (var watcher in Players())
        {
            if (watcher.Session == null)
                continue;
            bool cares = ReferenceEquals(watcher, target) || watcher.Knownlist.Contains(target.ObjectId);
            if (!cares)
                continue;
            outbound.Add(Pack(watcher.Session, MsgType.AbnormalStatusUpdate, update));
            netWatchers ??= [];
            netWatchers.Add(watcher.ObjectId);
        }

        if (netWatchers is { Count: > 0 } && NetBridge != null)
        {
            var effects = update.Effects ?? [];
            var netEffects = new Fineage.Net.Contract.AbnormalEffectNet[effects.Length];
            for (int i = 0; i < effects.Length; i++)
            {
                var src = effects[i];
                netEffects[i] = new Fineage.Net.Contract.AbnormalEffectNet
                {
                    Id = src.Id,
                    Value = src.Value,
                    DurationSeconds = src.DurationSeconds
                };
            }

            NetBridge.FanoutAbnormalStatusUpdate(netWatchers, new Fineage.Net.Contract.AbnormalStatusUpdateNetMsg
            {
                ObjectId = update.ObjectId,
                Effects = netEffects
            });
        }
    }

    AbnormalStatusUpdateMsg AbnormalFor(ZoneEntity entity)
    {
        long now = _clock().ToUnixTimeMilliseconds();
        var effects = new AbnormalEffectMsg[entity.Abnormals.Count];
        for (int i = 0; i < entity.Abnormals.Count; i++)
            effects[i] = ZoneAbnormals.ToWire(now, entity.Abnormals[i]);
        return new AbnormalStatusUpdateMsg
        {
            ObjectId = entity.ObjectId,
            Effects = effects
        };
    }

    int ResolveAbnormalDurationSeconds(int effectId, int fallbackSeconds)
    {
        if (effectId > 0)
        {
            var skill = _game.Skills.Find(effectId);
            if (skill != null && skill.AbnormalTimeSeconds > 0)
                return skill.AbnormalTimeSeconds;
        }

        return fallbackSeconds;
    }

    int ResolveAbnormalValue(int effectId, int fallbackValue)
    {
        if (effectId > 0)
        {
            var skill = _game.Skills.Find(effectId);
            if (skill != null && skill.TryLevelData(1, out var level))
                return level.AbnormalLevel;
        }

        return fallbackValue;
    }
}
