using Fineage.Game;
using Fineage.Game.Combat;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius cast interruption: Formulas.calcAtkBreak on damage taken (magic/static skills only), abortCast at launch when the
/// target is gone / escaped / out of sight, and teleToLocation aborting the cast and queued actions.
/// No MagicSkillCanceled exists on the Net contract yet, so the client only gets SysMsg 27 on a hit break.
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>Drop the in-flight skill: no hit, casting window closed, queued cast cleared. Returns whether something was cast.</summary>
    bool AbortCast(ZoneEntity player, string reason, List<Pending> outbound, bool notify)
    {
        long now = _clock().ToUnixTimeMilliseconds();
        bool casting = player.PendingHit != null || player.CastingUntilMs > now;
        if (!casting)
            return false;
        int skill = player.PendingHit?.Combat?.SkillId ?? 0;
        player.PendingHit = null;
        player.CastingUntilMs = 0;
        if (player.Session is { } session)
        {
            session.PendingCastSkillId = 0;
            session.PendingCastTargetId = 0;
            session.PendingCastAtUnixMs = 0;
            if (notify)
                Notice(session, CastRules.CastingInterruptedMessage, "Your casting has been interrupted.", outbound);
        }

        ZoneLog.Info($"[cast] abort obj={player.ObjectId} skill={skill} reason={reason}");
        return true;
    }

    /// <summary>Mobius Creature.onHit → calcAtkBreak → breakCast for a player hit while casting a magic skill.</summary>
    void MaybeBreakCast(ZoneEntity player, int damage, List<Pending> outbound)
    {
        var hit = player.PendingHit;
        if (damage <= 0 || hit?.Combat == null || !CastRules.Interruptible(hit.Combat.Magic))
            return;
        if (_clock().ToUnixTimeMilliseconds() >= hit.DueUnixMs)
            return;
        double men = 1;
        if (PlayerClasses.TryGet(player.ClassId, out var t) && t != null)
            men = SafeBonus(PlayerClasses.StatBonuses, StatSlot.Men, t.Men);
        if (CastRules.RollAttackBreak(_combatRandom, damage, men))
            AbortCast(player, "hit", outbound, notify: true);
    }

    internal bool AbortCastForTest(ZoneEntity player) { lock (_gate) return AbortCast(player, "test", [], notify: false); }
    internal void ResolvePendingHitForTest(ZoneEntity player) { lock (_gate) ResolvePendingHit(player, _clock().ToUnixTimeMilliseconds(), []); }
    internal void HitPlayerForTest(ZoneEntity player, int damage) { lock (_gate) { player.CurHp = Math.Max(1, player.CurHp - damage); MaybeBreakCast(player, damage, []); } }
}
