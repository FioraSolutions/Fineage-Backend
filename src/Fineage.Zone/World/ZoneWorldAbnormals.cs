using Fineage.Game.Skills;
using Fineage.Protocol;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    /// <summary>
    /// Apply a skill abnormal. Debuffs roll Mobius <c>calcEffectSuccess</c> (activateRate × trait resist)
    /// unless <paramref name="force"/> (admin packs). Returns false when resisted (no abnormal written).
    /// </summary>
    bool ApplyAbnormalFromSkill(
        SkillDefinition template,
        SkillLevelData levelData,
        ZoneEntity target,
        long nowUnixMs,
        ZoneEntity? attacker = null,
        bool force = false)
    {
        if (!force && template.IsDebuff &&
            !RollDebuffLand(attacker, target, template, levelData, out _))
            return false;

        ZoneAbnormals.Apply(
            target.Abnormals,
            template.SkillId,
            levelData.AbnormalLevel,
            template.AbnormalTimeSeconds,
            nowUnixMs,
            levelData.Level,
            template.AbnormalType);
        if (target.Abnormals.Count > 0)
            _withAbnormals.Add(target.ObjectId);
        FanoutAbnormal(target);
        return true;
    }

    /// <summary>Mobius calcEffectSuccess for debuffs; jewelry DefenceTrait lands in <paramref name="traitMod"/>.</summary>
    bool RollDebuffLand(
        ZoneEntity? attacker,
        ZoneEntity target,
        SkillDefinition template,
        SkillLevelData levelData,
        out double chance)
    {
        chance = 100d;
        if (!template.IsDebuff)
            return true;
        if (template.ActivateRate < 0 || string.IsNullOrWhiteSpace(template.BasicProperty))
            return true;

        double traitMod = ZoneCombat.GeneralResistanceBonus(
            attackTraits: null,
            defenceTraits: TargetDefenceTraits(target),
            trait: template.Trait);

        double mAtkMod = 1d;
        if (template.MagicType == 1 && attacker != null)
            mAtkMod = DebuffMAtkMod(attacker, target);

        if (levelData.MagicLevel <= 0)
            throw new InvalidOperationException(
                $"skill {template.SkillId} lv{levelData.Level} missing magicLevel for calcEffectSuccess");
        chance = ZoneCombat.DebuffLandChance(
            template.ActivateRate,
            template.BasicProperty,
            levelData.MagicLevel,
            template.LvlBonusRate,
            target.Level,
            TargetBaseStat(target, template.BasicProperty),
            traitMod,
            mAtkMod);

        bool landed = ZoneCombat.RollDebuffLand(_combatRandom, chance);
        ZoneLog.Info(
            $"[debuff-land] skill={template.SkillId} lv={levelData.Level} ml={levelData.MagicLevel} " +
            $"rate={template.ActivateRate} trait={template.Trait} traitMod={traitMod:F3} mAtkMod={mAtkMod:F3} " +
            $"chance={chance:F1} landed={landed} atk={attacker?.ObjectId ?? 0} tgt={target.ObjectId}");
        if (!landed)
        {
            if (attacker?.Session != null)
                ShotNotice(attacker.Session, SysMsgResisted,
                    $"{target.Name} has resisted your {template.Name}.");
            else if (target.Session != null)
                ShotNotice(target.Session, SysMsgResisted,
                    $"You have resisted {template.Name}.");
        }
        return landed;
    }

    /// <summary>Player jewelry/set DefenceTrait map; NPC race/weapon traits from the spawn template.</summary>
    IReadOnlyDictionary<string, double>? TargetDefenceTraits(ZoneEntity target) =>
        target.Kind == ZoneEntityKind.Player
            ? PlayerDefenceTraits(target)
            : target.DefenceTraits;

    int TargetBaseStat(ZoneEntity target, string basicProperty)
    {
        if (target.Kind != ZoneEntityKind.Player)
            return 0;
        return basicProperty.ToUpperInvariant() switch
        {
            "STR" => EffectiveStr(target),
            "DEX" => EffectiveDex(target),
            "CON" => EffectiveCon(target),
            "INT" => EffectiveAttributes(target).Int,
            "MEN" => EffectiveMen(target),
            "WIT" => EffectiveAttributes(target).Wit,
            _ => 0
        };
    }

    /// <summary>Mobius calcEffectSuccess mAtkMod for magic debuffs: sqrt(mAtk)/mDef·11 (no BSS yet).</summary>
    double DebuffMAtkMod(ZoneEntity attacker, ZoneEntity target)
    {
        double mAtk = attacker.Kind == ZoneEntityKind.Player
            ? PlayerStats(attacker).MAtk
            : Math.Max(1d, attacker.MAtk);
        double mDef = target.Kind == ZoneEntityKind.Player
            ? PlayerStats(target).MDef
            : Math.Max(1d, target.MDef > 0 ? target.MDef : target.Level * 30d);
        return Math.Sqrt(Math.Max(0, mAtk)) / Math.Max(1d, mDef) * 11d;
    }

    void ApplyAbnormalPlaytest(
        ZoneEntity target,
        int effectId,
        int value,
        int durationSeconds,
        long nowUnixMs)
    {
        ZoneAbnormals.Apply(target.Abnormals, effectId, value, durationSeconds, nowUnixMs);
        if (target.Abnormals.Count > 0)
            _withAbnormals.Add(target.ObjectId);
        FanoutAbnormal(target);
    }

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
        for (int i = 0; i < entity.Abnormals.Count; i++)
        {
            var ab = entity.Abnormals[i];
            if (!_skillDots.TryGet(ab.Id, out var dot))
                continue;
            if (ab.NextDotAtUnixMs > now)
                continue;
            int level = ab.SkillLevel > 0 ? ab.SkillLevel : Math.Max(1, ab.Value);
            double power = dot.PowerAt(level);
            int dmg = Math.Max(1, (int)Math.Round(dot.Percent ? entity.MaxHp * power : power));
            entity.CurHp = Math.Max(0, entity.CurHp - dmg);
            ab.NextDotAtUnixMs = now + dot.TickMs;
            hurt = true;
            if (entity.CurHp <= 0)
                break;
        }

        if (!hurt)
            return;
        FanoutVitals(entity, StatusFor(entity));
        if (entity.Kind == ZoneEntityKind.Npc && entity.CurHp <= 0)
        {
            // Killer unknown for periodic DoT — treat as environmental death (no PK/party share).
            RemoveDeadNpc(entity, killer: null);
        }
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

    int ResolveAbnormalDurationSeconds(int effectId, int fallbackSeconds)
    {
        if (effectId <= 0)
            throw new ArgumentOutOfRangeException(nameof(effectId), effectId, "Abnormal effect id required.");
        var skill = _game.Skills.Find(effectId)
            ?? throw new InvalidOperationException($"skills.json missing skillId={effectId} (duration).");
        if (skill.AbnormalTimeSeconds <= 0)
            throw new InvalidOperationException($"skill {effectId} has no abnormalTimeSeconds.");
        // fallbackSeconds kept only for call-site diagnostics in logs when callers still pass it.
        if (fallbackSeconds > 0 && fallbackSeconds != skill.AbnormalTimeSeconds)
            ZoneLog.Warn($"abnormal duration skill={effectId} catalog={skill.AbnormalTimeSeconds}s callerHint={fallbackSeconds}s — using catalog");
        return skill.AbnormalTimeSeconds;
    }

    int ResolveAbnormalValue(int effectId, int fallbackValue)
    {
        if (effectId <= 0)
            throw new ArgumentOutOfRangeException(nameof(effectId), effectId, "Abnormal effect id required.");
        var skill = _game.Skills.Find(effectId)
            ?? throw new InvalidOperationException($"skills.json missing skillId={effectId} (abnormalLevel).");
        if (!skill.TryLevelData(1, out var level) || level.AbnormalLevel <= 0)
            throw new InvalidOperationException($"skill {effectId} lv1 missing abnormalLevel.");
        if (fallbackValue > 0 && fallbackValue != level.AbnormalLevel)
            ZoneLog.Warn($"abnormal value skill={effectId} catalog={level.AbnormalLevel} callerHint={fallbackValue} — using catalog");
        return level.AbnormalLevel;
    }
}
