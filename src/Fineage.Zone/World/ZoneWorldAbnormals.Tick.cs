using Fineage.Game.Skills;
using Fineage.Protocol;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    void TickAbnormals()
    {
        if (_withAbnormals.Count == 0)
            return;
        long now = _clock().ToUnixTimeMilliseconds();
        foreach (int id in _withAbnormals.ToArray())
        {
            if (!_byId.TryGetValue(id, out var entity))
            {
                _withAbnormals.Remove(id);
                continue;
            }
            if (entity.Abnormals.Count == 0)
            {
                _withAbnormals.Remove(id);
                continue;
            }

            TickDotDamage(entity, now);

            if (!ZoneAbnormals.TickExpired(entity.Abnormals, now))
                continue;
            if (entity.Abnormals.Count == 0)
                _withAbnormals.Remove(id);
            FanoutAbnormal(entity);
        }
    }

    /// <summary>Mobius DamOverTime / DamOverTimePercent while the abnormal is live.</summary>
    void TickDotDamage(ZoneEntity entity, long now)
    {
        if (entity.CurHp <= 0 || entity.Dead || entity.MaxHp <= 0)
            return;
        bool hurt = false;
        int totalDmg = 0;
        for (int i = 0; i < entity.Abnormals.Count; i++)
        {
            var ab = entity.Abnormals[i];
            if (!_skillDots.TryGet(ab.Id, out var dot))
                continue;
            if (ab.NextDotAtUnixMs > now)
                continue;
            int level = ab.SkillLevel > 0 ? ab.SkillLevel : Math.Max(1, ab.Value);
            double power = dot.PowerAt(level);
            // Mobius DamOverTimePercent: currentHp * power (ticksMultiplier folded into skill-dots tick).
            double raw = dot.Percent ? entity.CurHp * power : power;
            // Keep fractional DoT from vanishing on full-HP low-level mobs: floor at 1 once power > 0.
            int dmg = power <= 0 ? 0 : Math.Max(1, (int)Math.Round(raw));
            if (dmg <= 0)
            {
                ab.NextDotAtUnixMs = now + dot.TickMs;
                continue;
            }
            // Mobius canKill=false DoTs leave the target at 1 HP.
            int nextHp = entity.CurHp - dmg;
            if (nextHp < 1)
                nextHp = 1;
            totalDmg += entity.CurHp - nextHp;
            entity.CurHp = nextHp;
            ab.NextDotAtUnixMs = now + dot.TickMs;
            hurt = true;
        }

        if (!hurt || totalDmg <= 0)
            return;
        SyncNeedRegen(entity);
        FanoutVitals(entity, StatusFor(entity));
    }

    /// <summary>Test hook: run one abnormal expiry pass and fan-out clears.</summary>
    internal Task RunAbnormalTickAsync(CancellationToken ct)
    {
        lock (_gate)
            TickAbnormals();
        return Task.CompletedTask;
    }

    /// <summary>Test hook: force-apply a skill abnormal (skips land roll).</summary>
    internal bool ApplyAbnormalFromSkillForTest(
        Fineage.Game.Skills.SkillDefinition template,
        Fineage.Game.Skills.SkillLevelData levelData,
        ZoneEntity target,
        bool force = true)
    {
        long now = _clock().ToUnixTimeMilliseconds();
        lock (_gate)
            return ApplyAbnormalFromSkill(template, levelData, target, now, force: force);
    }

    /// <summary>Test hook: PartySpelled / TargetStatus wire snapshot.</summary>
    internal Fineage.Protocol.AbnormalStatusUpdateMsg AbnormalSnapshotForTest(ZoneEntity entity)
    {
        lock (_gate)
            return AbnormalFor(entity);
    }

    void FanoutAbnormal(ZoneEntity target)
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
            netWatchers ??= [];
            netWatchers.Add(watcher.ObjectId);
        }

        if (netWatchers is { Count: > 0 } && NetBridge != null)
            NetBridge.FanoutAbnormalStatusUpdate(netWatchers, update);
    }

    /// <summary>
    /// Mobius PartySpelled / updateEffectIcons snapshot. Spoil is not a real abnormal — Interlude still
    /// shows skill 254 on the target window while <see cref="ZoneEntity.SpoilerObjectId"/> is set.
    /// </summary>
    AbnormalStatusUpdateMsg AbnormalFor(ZoneEntity entity)
    {
        long now = _clock().ToUnixTimeMilliseconds();
        int spoilExtra = entity.SpoilerObjectId != 0 ? 1 : 0;
        var effects = new AbnormalEffectMsg[entity.Abnormals.Count + spoilExtra];
        for (int i = 0; i < entity.Abnormals.Count; i++)
            effects[i] = ZoneAbnormals.ToWire(now, entity.Abnormals[i]);
        if (spoilExtra > 0)
        {
            effects[^1] = new AbnormalEffectMsg
            {
                Id = 254,
                Value = 1,
                DurationSeconds = 3600
            };
        }
        return new AbnormalStatusUpdateMsg
        {
            ObjectId = entity.ObjectId,
            Effects = effects
        };
    }

    /// <summary>Mobius updateEffectIcons snapshot for EnterWorld (null when the bar is empty).</summary>
    internal bool TryBuildNetAbnormalStatus(ZoneSession session, out AbnormalStatusUpdateMsg update)
    {
        lock (_gate)
        {
            if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var entity) ||
                entity.Kind != ZoneEntityKind.Player || entity.Abnormals.Count == 0)
            {
                update = null!;
                return false;
            }

            update = AbnormalFor(entity);
            return true;
        }
    }

    int ResolveAbnormalDurationSeconds(int effectId)
    {
        if (effectId <= 0)
            throw new ArgumentOutOfRangeException(nameof(effectId), effectId, "Abnormal effect id required.");
        var skill = _game.Skills.Find(effectId)
            ?? throw new InvalidOperationException($"skills.json missing skillId={effectId} (duration).");
        if (skill.AbnormalTimeSeconds <= 0)
            throw new InvalidOperationException($"skill {effectId} has no abnormalTimeSeconds.");
        return skill.AbnormalTimeSeconds;
    }

    int ResolveAbnormalValue(int effectId)
    {
        if (effectId <= 0)
            throw new ArgumentOutOfRangeException(nameof(effectId), effectId, "Abnormal effect id required.");
        var skill = _game.Skills.Find(effectId)
            ?? throw new InvalidOperationException($"skills.json missing skillId={effectId} (abnormalLevel).");
        if (!skill.TryLevelData(1, out var level) || level.AbnormalLevel <= 0)
            throw new InvalidOperationException($"skill {effectId} lv1 missing abnormalLevel.");
        return level.AbnormalLevel;
    }
}
