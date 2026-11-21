using Fineage.Game.Skills;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    void CollectAbnormals(TickContext tick, RegionWorkCollector work)
    {
        _byId.Collect(ActiveSets.WithAbnormals, work);
    }

    void TickEntityAbnormals(int id, long now)
    {
        if (!_byId.TryGetValue(id, out var entity))
            return;
        if (entity.Abnormals.Count == 0)
        {
            _byId.Leave(entity, ActiveSets.WithAbnormals);
            return;
        }

        TickDotDamage(entity, now);
        TickToggleDrain(entity, now);

        if (!ZoneAbnormals.TickExpired(entity.Abnormals, now))
            return;
        if (entity.Abnormals.Count == 0)
            _byId.Leave(entity, ActiveSets.WithAbnormals);
        FanoutAbnormal(entity);
    }

    /// <summary>
    /// Mobius toggle <c>onActionTime</c> for player abnormals: drain the toggle's MP every <c>ticks</c> seconds and end
    /// it when the owner can no longer pay, its HP/seat condition broke, or it was already removed.
    /// </summary>
    void TickToggleDrain(ZoneEntity entity, long now)
    {
        if (entity.Kind != ZoneEntityKind.Player || entity.Dead)
            return;
        for (int i = entity.Abnormals.Count - 1; i >= 0; i--)
        {
            var entry = entity.Abnormals[i];
            if (!IsToggleSkillId(entry.Id))
                continue;
            // Each effect ticks on its own period; the abnormal entry carries the next due time for the skill.
            int periodMs = ToggleTickPeriodMs(entry.Id);
            if (periodMs <= 0 || entry.NextDotAtUnixMs > now)
                continue;
            entry.NextDotAtUnixMs = now + periodMs;
            if (!DrainToggleTick(entity, entry, now, out bool notified))
                continue;
            // The drain already printed the specific message (140 / 175) when notified; otherwise the toggle ends
            // silently and DeactivateToggle sends S1_HAS_BEEN_ABORTED. Either way the skill's exit hook must run —
            // for Fake Death that is what stands the pawn back up (WT_STOP_FAKEDEATH + Revive). Always go through
            // DeactivateToggle so FanoutAbnormal reaches the client (removing the row first skipped the glow clear).
            DeactivateToggle(entity.Session, entity, entry.Id, _game.Skills.Find(entry.Id), "tick", sendAbort: !notified);
        }
        if (entity.Abnormals.Count == 0)
            _byId.Leave(entity, ActiveSets.WithAbnormals);
    }

    /// <summary>Mobius effect <c>ticks</c> in ms for a toggle's MP drain (0 when the skill has no ticking drain).</summary>
    internal int ToggleTickPeriodMs(int skillId)
    {
        if (!_skillEffects.TryGet(skillId, out var effects))
            return 0;
        foreach (var effect in effects)
        {
            bool drains = effect.Name is "MpConsumePerLevel" or "FakeDeath" or "SilentMove" or "Relax" or "ChameleonRest";
            if (drains && effect.Ticks > 0)
                // Mobius schedules the effect task at ticks · EFFECT_TICK_RATIO ms (666 ≈ two thirds of a second per tick).
                return (int)Math.Round(effect.Ticks * EffectTickRatio);
        }
        return 0;
    }

    internal bool IsToggleSkillId(int skillId) =>
        _game.Skills.Find(skillId) is { } template && IsToggleSkill(template);

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
        RunStep(now => _simulationTicks.RunNow(_abnormalsStage, now));
        return Task.CompletedTask;
    }

    /// <summary>Test hook: force-apply a skill abnormal (skips land roll).</summary>
    internal bool ApplyAbnormalFromSkillForTest(
        Fineage.Game.Skills.SkillDefinition template,
        Fineage.Game.Skills.SkillLevelData levelData,
        ZoneEntity target,
        bool force = true,
        ZoneEntity? attacker = null)
    {
        long now = NowUnixMs();
        using (_affinity.Step())
            return ApplyAbnormalFromSkill(template, levelData, target, now, attacker, force: force);
    }

    /// <summary>Test hook: PartySpelled / TargetStatus wire snapshot.</summary>
    internal Fineage.Protocol.AbnormalStatusUpdateMsg AbnormalSnapshotForTest(ZoneEntity entity)
    {
        using (_affinity.Step())
            return AbnormalFor(entity);
    }

    void FanoutAbnormal(ZoneEntity target)
    {
        var update = AbnormalFor(target);
        // Mobius: a buff/debuff that changes combat stats triggers UserInfo (status window P.Atk/P.Def/Spd)
        // and a fresh run speed (Wind Walk/Slow change how fast the pawn actually travels).
        bool statful = target.Abnormals.Any(a => _statEffects.TryGet(a.Id, out _));
        if (target.Kind == ZoneEntityKind.Player)
            RefreshPlayerMaxima(target, fanout: false);
        bool speedChanged = RefreshRunSpeed(target);
        if (speedChanged)
            FanoutNetMoveType(target);
        if ((statful || target.HasStatAbnormal) && target.Kind == ZoneEntityKind.Player && target.Session != null)
            SelfUserInfo(target);
        target.HasStatAbnormal = statful;
        var netWatchers = NetWatchersOf(target);

        if (netWatchers.Count > 0 && NetBridge != null)
            NetBridge.FanoutAbnormalStatusUpdate(netWatchers, update);
        FanPartySpelled(target);
    }

    /// <summary>
    /// Mobius PartySpelled / updateEffectIcons snapshot. Spoil is not a real abnormal — Interlude still
    /// shows skill 254 on the target window while <see cref="ZoneEntity.SpoilerObjectId"/> is set.
    /// </summary>
    AbnormalStatusUpdateMsg AbnormalFor(ZoneEntity entity)
    {
        long now = NowUnixMs();
        int spoilExtra = entity.SpoilerObjectId != 0 ? 1 : 0;
        var effects = new AbnormalEffectMsg[entity.Abnormals.Count + spoilExtra];
        for (int i = 0; i < entity.Abnormals.Count; i++)
            effects[i] = ZoneAbnormals.ToWire(now, entity.Abnormals[i], VisualFor(entity.Abnormals[i]));
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

    /// <summary>Look up Mobius <c>abnormalVisualEffect</c> from the skill template (Silent Move has no slot).</summary>
    string VisualFor(ZoneAbnormalEntry entry)
    {
        if (entry == null)
            return "";
        var skill = _game.Skills.Find(entry.Id);
        return skill?.AbnormalVisualEffect ?? "";
    }

    /// <summary>
    /// Abnormal bar of a pawn entering someone's AOI (players and NPCs with live effects or a spoil mark);
    /// false when there is nothing to show.
    /// </summary>
    internal bool TryBuildPresenceAbnormal(ZoneEntity entity, out AbnormalStatusUpdateMsg update)
    {
        if (entity.Dead || entity.Kind is not (ZoneEntityKind.Player or ZoneEntityKind.Npc) ||
            (entity.Abnormals.Count == 0 && entity.SpoilerObjectId == 0))
        {
            update = null!;
            return false;
        }

        update = AbnormalFor(entity);
        return true;
    }

    /// <summary>Mobius updateEffectIcons snapshot for EnterWorld (null when the bar is empty).</summary>
    internal bool TryBuildNetAbnormalStatus(ZoneSession session, out AbnormalStatusUpdateMsg update)
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
