using Fineage.Game.Skills;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Skills;
using MessagePack;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public async Task<string?> TrySkillUseAsync(ZoneSession session, SkillUseRequestMsg request, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";

        var outbound = new List<Pending>();
        string? error;
        lock (_gate)
            error = TrySkillUse(session, request, outbound);
        if (error != null)
            return error;

        await FlushSelfAsync(session, outbound, ct).ConfigureAwait(false);
        return null;
    }

    void RefreshKnownSkills(ZoneEntity entity)
    {
        entity.KnownSkills.Clear();
        foreach (var row in PlayerSkillBook.AutoSkills(_game.SkillTrees, _game.Skills, entity.ClassId, entity.Level))
            entity.KnownSkills[row.SkillId] = row.Level;
    }

    SkillListMsg SkillListFor(ZoneEntity entity)
    {
        var rows = entity.KnownSkills
            .OrderBy(pair => pair.Key)
            .Select(pair => new KnownSkill(pair.Key, pair.Value))
            .ToList();
        long now = _clock().ToUnixTimeMilliseconds();
        return SkillMessages.List(rows, entity.SkillReuseUntilMs, now);
    }

    string? TrySkillUse(ZoneSession session, SkillUseRequestMsg request, List<Pending> outbound)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var player) || player.Session == null)
            return "world";
        if (player.Dead)
            return SkillProtocol.Dead;
        if (request.SkillId <= 0)
            return "bad_request";

        if (!player.KnownSkills.TryGetValue(request.SkillId, out int level))
            return SkillProtocol.Unknown;

        var template = _game.Skills.Find(request.SkillId);
        if (template == null || template.Passive)
            return template == null ? SkillProtocol.Unknown : SkillProtocol.Passive;
        if (!template.TryLevelData(level, out var levelData))
            return SkillProtocol.Unknown;

        long now = _clock().ToUnixTimeMilliseconds();
        if (player.SkillReuseUntilMs.TryGetValue(request.SkillId, out long reuseUntil) && reuseUntil > now)
            return SkillProtocol.Reuse;

        if (player.CurMp < levelData.MpConsume)
            return SkillProtocol.Mp;

        if (template.IsBuffSkill)
            return TrySkillUseBuff(session, player, request, template, levelData, level, now, outbound);
        if (template.IsDebuffSkill)
            return TrySkillUseDebuff(session, player, request, template, levelData, level, now, outbound);

        return TrySkillUseAttack(session, player, request, template, levelData, level, now, outbound);
    }

    string? TrySkillUseAttack(
        ZoneSession session,
        ZoneEntity player,
        SkillUseRequestMsg request,
        SkillDefinition template,
        SkillLevelData levelData,
        int level,
        long now,
        List<Pending> outbound)
    {
        int targetId = request.TargetObjectId > 0 ? request.TargetObjectId : session.SelectedTargetId;
        if (targetId <= 0)
            return SkillProtocol.Target;
        if (!_byId.TryGetValue(targetId, out var target))
            return SkillProtocol.Target;
        if (target.Kind != ZoneEntityKind.Npc || !target.Attackable || target.CurHp <= 0)
            return SkillProtocol.Target;
        if (!VisibleTo(player.ObjectId, targetId))
            return SkillProtocol.Target;

        bool inRange = template.CastRange > 0
            ? ZoneCombat.InCastRange(player.X, player.Y, target.X, target.Y, template.CastRange + 100)
            : ZoneCombat.InMeleeRange(player.X, player.Y, target.X, target.Y);
        if (!inRange)
            return SkillProtocol.Range;

        session.AutoAttackTargetId = 0;
        player.CurMp = Math.Max(0, player.CurMp - levelData.MpConsume);
        if (template.ReuseDelayMs > 0)
            player.SkillReuseUntilMs[request.SkillId] = now + template.ReuseDelayMs;

        int damage = Math.Max(1, levelData.Power + ZoneCombat.SampleMeleeDamage / 3);
        target.CurHp = Math.Max(0, target.CurHp - damage);

        var cast = BuildCast(player, target, request.SkillId, level, template);
        var status = StatusFor(target);
        var selfMp = CasterMpStatus(player);
        FanoutSkill(player, target, cast, status, selfMp, outbound);

        player.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        session.SelectedTargetId = targetId;

        if (target.CurHp <= 0)
        {
            RemoveDeadNpc(target, outbound);
            GrantKillReward(player, target, outbound);
        }

        return null;
    }

    string? TrySkillUseBuff(
        ZoneSession session,
        ZoneEntity player,
        SkillUseRequestMsg request,
        SkillDefinition template,
        SkillLevelData levelData,
        int level,
        long now,
        List<Pending> outbound)
    {
        int targetId = ResolveBuffTargetId(session, player, request, template);
        if (targetId <= 0)
            return SkillProtocol.Target;
        if (!_byId.TryGetValue(targetId, out var target))
            return SkillProtocol.Target;
        if (target.Kind != ZoneEntityKind.Player || target.Dead || target.CurHp <= 0)
            return SkillProtocol.Target;
        if (targetId != player.ObjectId && !VisibleTo(player.ObjectId, targetId))
            return SkillProtocol.Target;
        if (!ZoneCombat.InCastRange(player.X, player.Y, target.X, target.Y, template.CastRange))
            return SkillProtocol.Range;

        player.CurMp = Math.Max(0, player.CurMp - levelData.MpConsume);
        if (template.ReuseDelayMs > 0)
            player.SkillReuseUntilMs[request.SkillId] = now + template.ReuseDelayMs;

        ApplyAbnormalFromSkill(template, levelData, target, now, outbound);

        var cast = BuildCast(player, target, request.SkillId, level, template);
        FanoutSkill(player, target, cast, targetStatus: null, CasterMpStatus(player), outbound);
        player.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        return null;
    }

    string? TrySkillUseDebuff(
        ZoneSession session,
        ZoneEntity player,
        SkillUseRequestMsg request,
        SkillDefinition template,
        SkillLevelData levelData,
        int level,
        long now,
        List<Pending> outbound)
    {
        int targetId = request.TargetObjectId > 0 ? request.TargetObjectId : session.SelectedTargetId;
        if (targetId <= 0)
            return SkillProtocol.Target;
        if (!_byId.TryGetValue(targetId, out var target))
            return SkillProtocol.Target;
        if (target.Kind != ZoneEntityKind.Npc || !target.Attackable || target.CurHp <= 0)
            return SkillProtocol.Target;
        if (!VisibleTo(player.ObjectId, targetId))
            return SkillProtocol.Target;
        if (!ZoneCombat.InCastRange(player.X, player.Y, target.X, target.Y, template.CastRange))
            return SkillProtocol.Range;

        player.CurMp = Math.Max(0, player.CurMp - levelData.MpConsume);
        if (template.ReuseDelayMs > 0)
            player.SkillReuseUntilMs[request.SkillId] = now + template.ReuseDelayMs;

        ApplyAbnormalFromSkill(template, levelData, target, now, outbound);

        var cast = BuildCast(player, target, request.SkillId, level, template);
        FanoutSkill(player, target, cast, targetStatus: null, CasterMpStatus(player), outbound);
        player.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        return null;
    }

    static int ResolveBuffTargetId(ZoneSession session, ZoneEntity player, SkillUseRequestMsg request, SkillDefinition template)
    {
        if (string.Equals(template.TargetType, "SELF", StringComparison.OrdinalIgnoreCase))
            return player.ObjectId;
        if (request.TargetObjectId > 0)
            return request.TargetObjectId;
        return player.ObjectId;
    }

    static MagicSkillUseMsg BuildCast(
        ZoneEntity caster,
        ZoneEntity target,
        int skillId,
        int level,
        SkillDefinition template) => new()
        {
            CasterObjectId = caster.ObjectId,
            TargetObjectId = target.ObjectId,
            SkillId = skillId,
            SkillLevel = level,
            HitTimeMs = template.HitTimeMs,
            ReuseDelayMs = template.ReuseDelayMs
        };

    static StatusUpdateMsg CasterMpStatus(ZoneEntity player) => new()
    {
        ObjectId = player.ObjectId,
        AttributeIds = [InterludeStatus.CurMp, InterludeStatus.MaxMp],
        Values = [player.CurMp, player.MaxMp]
    };

    void FanoutSkill(
        ZoneEntity caster,
        ZoneEntity target,
        MagicSkillUseMsg cast,
        StatusUpdateMsg? targetStatus,
        StatusUpdateMsg casterMp,
        List<Pending> outbound)
    {
        byte[] castBody = MessagePackSerializer.Serialize(cast);
        byte[]? targetBody = targetStatus == null ? null : MessagePackSerializer.Serialize(targetStatus);
        byte[] mpBody = MessagePackSerializer.Serialize(casterMp);

        foreach (var watcher in Players())
        {
            if (watcher.Session == null)
                continue;
            bool cares = ReferenceEquals(watcher, caster) ||
                         watcher.Knownlist.Contains(target.ObjectId) ||
                         watcher.Knownlist.Contains(caster.ObjectId);
            if (!cares)
                continue;

            outbound.Add(new Pending(watcher.Session, MsgType.MagicSkillUse, castBody));
            if (targetBody != null)
                outbound.Add(new Pending(watcher.Session, MsgType.StatusUpdate, targetBody));
            if (ReferenceEquals(watcher, caster))
                outbound.Add(new Pending(watcher.Session, MsgType.StatusUpdate, mpBody));
        }
    }
}
