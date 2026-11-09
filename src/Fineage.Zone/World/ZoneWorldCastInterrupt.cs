using Fineage.Game;
using Fineage.Game.Combat;
using Fineage.Game.Skills;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius cast interruption: Formulas.calcAtkBreak on damage taken (magic/static skills only), abortCast at launch when the
/// target is gone / escaped / out of sight, and teleToLocation aborting the cast and queued actions.
/// No MagicSkillCanceled exists on the Net contract yet, so the client only gets SysMsg 27 on a hit break.
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>Drop the in-flight skill: no hit, casting window closed, queued cast cleared. Returns whether something was cast.</summary>
    bool AbortCast(ZoneEntity player, string reason, bool notify)
    {
        long now = _clock().ToUnixTimeMilliseconds();
        bool casting = player.PendingHit != null || player.CastingUntilMs > now;
        if (!casting)
            return false;
        int skill = player.PendingHit?.Combat?.SkillId ?? 0;
        SetPendingHit(player, null);
        player.CastingUntilMs = 0;
        if (player.Session is { } session)
        {
            session.PendingCastSkillId = 0;
            session.PendingCastTargetId = 0;
            session.PendingCastAtUnixMs = 0;
            if (notify)
                Notice(session, CastRules.CastingInterruptedMessage, "Your casting has been interrupted.");
        }

        ZoneLog.Info($"[cast] abort obj={player.ObjectId} skill={skill} reason={reason}");
        return true;
    }

    /// <summary>Mobius Creature.onHit → calcAtkBreak → breakCast for a player hit while casting a magic skill.</summary>
    void MaybeBreakCast(ZoneEntity player, int damage)
    {
        var hit = player.PendingHit;
        if (damage <= 0 || hit == null)
            return;
        bool interruptible = hit.Combat != null ? CastRules.Interruptible(hit.Combat.Magic) : CastRules.Interruptible(hit.MagicType);
        if (!interruptible)
            return;
        if (_clock().ToUnixTimeMilliseconds() >= hit.DueUnixMs)
            return;
        double men = 1;
        if (PlayerClasses.TryGet(player.ClassId, out var t) && t != null)
            men = StatBonus(PlayerClasses.StatBonuses, StatSlot.Men, t.Men + player.HennaMen);
        if (CastRules.RollAttackBreak(_combatRandom, damage, men))
            AbortCast(player, "hit", notify: true);
    }

    internal bool AbortCastForTest(ZoneEntity player) { lock (_gate) return AbortCast(player, "test", notify: false); }
    internal void ResolvePendingHitForTest(ZoneEntity player) { lock (_gate) ResolvePendingHit(player, _clock().ToUnixTimeMilliseconds()); }
    internal void HitPlayerForTest(ZoneEntity player, int damage) { lock (_gate) { player.CurHp = Math.Max(1, player.CurHp - damage); MaybeBreakCast(player, damage); } }

    /// <summary>Effect run at hitTime for a non-damage skill (Mobius onMagicHitTimer → callSkill).</summary>
    delegate void DeferredEffect(ZoneEntity target, long now);

    /// <summary>
    /// Schedule a non-damage skill: MP/reuse/MagicSkillUse already went out at cast start; the effect lands at hitTime after
    /// the Mobius launch checks (target alive, within escape range, in sight). hitTime 0 resolves now.
    /// </summary>
    void ScheduleEffect(ZoneEntity player, ZoneEntity target, SkillDefinition template, int level, long now, int hitTimeMs,
        DeferredEffect effect)
    {
        bool self = ReferenceEquals(player, target);
        SetPendingHit(player, new PendingSkillHit(target.ObjectId, 0, now + Math.Max(0, hitTimeMs), null, level)
        {
            SkillId = template.SkillId,
            MagicType = template.MagicType,
            EscapeRange = self ? 0 : CastRules.EscapeRange(template.CastRange, template.EffectRange),
            TargetIsNpc = target.Kind == ZoneEntityKind.Npc,
            Apply = effect
        });
        if (hitTimeMs <= 0)
            ResolvePendingHit(player, now);
    }

    void ResolveDeferredEffect(ZoneEntity player, PendingSkillHit hit, DeferredEffect effect, long now)
    {
        bool alive = _byId.TryGetValue(hit.TargetId, out var target) && (hit.TargetIsNpc
            ? target.Kind == ZoneEntityKind.Npc && target.CurHp > 0 && !target.Dead
            : target.Kind == ZoneEntityKind.Player && !target.Dead);
        if (!alive)
        {
            AbortCast(player, "target-gone", notify: false);
            return;
        }

        if (!ReferenceEquals(player, target!))
        {
            if (hit.EscapeRange > 0 && !ZoneCombat.InCastRange(player.X, player.Y, target!.X, target.Y, hit.EscapeRange + ZoneCombat.HitRangeTolerance))
            {
                AbortCast(player, "escaped", notify: false);
                return;
            }

            if (!CanSeeTarget(player, target!))
            {
                AbortCast(player, "no-los", notify: false);
                return;
            }
        }

        ZoneLog.Info($"[skill] effect {hit.SkillId} lv{hit.Level} target={target!.ObjectId}");
        effect(target, now);
    }

    /// <summary>Test: land the caster's in-flight skill now (as if hitTime passed) and flush the caster's frames.</summary>
    internal Task CompleteCastForTestAsync(ZoneSession session)
    {
        lock (_gate)
        {
            if (_byId.TryGetValue(session.ObjectId, out var player))
                ResolvePendingHit(player, _clock().ToUnixTimeMilliseconds());
        }

        return Task.CompletedTask;
    }
}
