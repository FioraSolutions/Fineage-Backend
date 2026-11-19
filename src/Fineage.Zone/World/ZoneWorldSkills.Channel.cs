using Fineage.Game;
using Fineage.Game.Skills;
using Fineage.Protocol;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius CA1 ground channels (Volcano / Gehenna): start-effects (SummonNpc), then MagicalDamage ticks
/// in <c>affectRange</c> every <c>channelingTickInterval</c> seconds, spending <c>mpPerChanneling</c>.
/// Classification is by operateType / channeling fields, never by skill id.
/// </summary>
public sealed partial class ZoneWorld
{
    string? TrySkillUseChannel(
        ZoneSession session,
        ZoneEntity player,
        SkillUseIntent request,
        SkillDefinition template,
        SkillLevelData levelData,
        int level,
        long now)
    {
        if (player.Dead)
            return SkillProtocol.Dead;
        if (!PlayerConditionsMet(session, player, template))
            return null;
        if (ConsumeItemBlocked(session, player, levelData))
            return null;

        int x = player.X, y = player.Y, z = player.Z;
        int targetId = request.TargetObjectId > 0 ? request.TargetObjectId : session.SelectedTargetId;
        if (string.Equals(template.TargetType, "GROUND", StringComparison.OrdinalIgnoreCase) &&
            targetId > 0 && _byId.TryGetValue(targetId, out var ground) && !ground.Dead)
        {
            x = ground.X;
            y = ground.Y;
            z = ground.Z;
        }

        int tickMs = template.ChannelingTickMs > 0 ? template.ChannelingTickMs : 2000;
        int duration = Math.Max(tickMs, template.HitTimeMs > 0 ? template.HitTimeMs : 15_000);
        int range = ResolveAffectRange(template);
        if (range <= 0)
            range = 200;

        ConsumeSkillMp(player, template, levelData.MpConsume);
        if (!ConsumeSkillItems(session, player, levelData))
            return null;
        var (_, reuseMs) = ScaledSkillTimes(player, template, level);
        if (reuseMs > 0)
            player.SkillReuseUntilMs[request.SkillId] = now + reuseMs;

        var anim = player;
        var cast = BuildCast(player, anim, request.SkillId, level, duration, reuseMs);
        StopForCast(player, anim, now);
        FanoutSkill(player, anim, cast, targetStatus: null, CasterMpStatus(player));
        EnterCombatStanceOnCast(player, template, level, now);
        player.CastingUntilMs = now + duration;
        player.ChannelUntilMs = now + duration;
        player.ChannelSkillId = request.SkillId;
        player.ChannelSkillLevel = level;
        player.ChannelX = x;
        player.ChannelY = y;
        player.ChannelZ = z;
        player.ChannelTickMs = tickMs;
        player.ChannelNextTickMs = now + tickMs;
        player.ChannelMpPerTick = Math.Max(0, template.MpPerChanneling);
        player.ChannelAffectRange = range;
        session.SelectedTargetId = targetId > 0 ? targetId : player.ObjectId;

        if (_skillEffects.TryGet(request.SkillId, out var rows))
        {
            foreach (var effect in rows)
            {
                if (!EffectNameIs(effect, "SummonNpc"))
                    continue;
                if (!effect.ReadExtra("phase").Equals("start", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(effect.ReadExtra("phase")))
                    continue;
                int npcId = effect.ReadExtraInt("npcId");
                int count = Math.Max(1, effect.ReadExtraInt("npcCount", 1));
                if (npcId <= 0)
                    continue;
                for (int i = 0; i < count; i++)
                    SpawnSignet(player, npcId, x, y, z, now);
            }
        }
        return null;
    }

    void TickSkillChannels(long now)
    {
        foreach (var player in Players().ToList())
        {
            if (player.ChannelUntilMs <= 0)
                continue;
            if (player.Dead || now >= player.ChannelUntilMs)
            {
                ClearChannel(player);
                continue;
            }
            if (now < player.ChannelNextTickMs)
                continue;
            player.ChannelNextTickMs = now + Math.Max(200, player.ChannelTickMs);
            if (player.ChannelMpPerTick > 0)
            {
                if (player.CurMp < player.ChannelMpPerTick)
                {
                    ClearChannel(player);
                    continue;
                }
                player.CurMp -= player.ChannelMpPerTick;
                FanoutVitals(player, CasterMpStatus(player));
            }
            ApplyChannelDamageTick(player, now);
        }
    }

    void ApplyChannelDamageTick(ZoneEntity caster, long now)
    {
        _skillCombat.TryGet(caster.ChannelSkillId, out var combat);
        var template = _game.Skills.Find(caster.ChannelSkillId);
        int power = 0;
        if (combat != null)
            power = (int)combat.PowerAt(caster.ChannelSkillLevel);
        else if (template != null && template.TryLevelData(caster.ChannelSkillLevel, out var ld))
            power = ld.Power;
        if (power <= 0)
            return;
        // Zero MP so the tick does not re-charge the launch cost; CA1 spends mpPerChanneling instead.
        var data = combat != null
            ? combat with { MpConsume = [0], MpInitialConsume = combat.MpInitialConsume.Length == 0 ? [0] : [0] }
            : new SkillCombatData(
                caster.ChannelSkillId, template?.Name ?? "", 1, SkillDamageKind.Magical, true,
                [power], [0], [0], 0, 0, 0, 0, 0);
        foreach (var entity in _byId.Values)
        {
            if (entity.Dead || entity.CurHp <= 0 || ReferenceEquals(entity, caster))
                continue;
            if (!IsAttackable(caster, entity))
                continue;
            if (entity.Kind == ZoneEntityKind.Player &&
                (!PlayerAutoAttackable(caster, entity) || (caster.Session != null &&
                    PeacePlayableRefused(caster.Session, caster, entity, notify: false))))
                continue;
            if (!ZoneCombat.InCastRange(caster.ChannelX, caster.ChannelY, entity.X, entity.Y, caster.ChannelAffectRange))
                continue;
            if (!VisibleTo(caster.ObjectId, entity.ObjectId) && entity.Kind == ZoneEntityKind.Player)
                continue;
            int damage = RollSkillDamage(caster, entity, data, caster.ChannelSkillLevel, consumeResources: false);
            if (damage <= 0)
                continue;
            if (entity.Kind == ZoneEntityKind.Player)
                ApplySkillDamageToPlayer(caster, entity, damage, now);
            else
            {
                entity.CurHp = Math.Max(0, entity.CurHp - damage);
                FanoutVitals(entity, StatusFor(entity));
                AddHate(entity, caster, DamageHate(damage, entity.Level));
                if (entity.CurHp <= 0)
                {
                    RemoveDeadNpc(entity, killer: caster);
                    GrantKillReward(caster, entity);
                }
            }
            QueueAttackerDamageLine(caster, entity, damage);
        }
    }

    void ClearChannel(ZoneEntity player)
    {
        player.ChannelUntilMs = 0;
        player.ChannelSkillId = 0;
        player.ChannelNextTickMs = 0;
        if (player.CastingUntilMs > 0)
            player.CastingUntilMs = 0;
    }
}
