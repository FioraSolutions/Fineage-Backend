using Fineage.Game.Skills;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius <c>operateType T</c> toggles (Fake Death 60, Silent Move 221, Relax 226, Accuracy 256, Vicious Stance 312,
/// Focus Skill Mastery 334, …).
/// <para>
/// Mobius handles these through <c>Player.checkUseMagicConditions</c>: an active toggle re-use is refused with
/// <c>ActionFailed</c> after <c>stopSkillEffects(skillId)</c> — i.e. the second press switches it off. Activation
/// applies one abnormal with no duration (a toggle lives until it is switched off, its MP runs out, or its own
/// condition breaks), then the effect's <c>onActionTime</c> drains MP every <c>ticks</c> seconds.
/// </para>
/// <para>
/// The per-tick MP cost is not <c>mpConsume</c>: Mobius reads the cost from the effect's
/// <c>&lt;param power&gt;</c> (<c>MpConsumePerLevel</c> / <c>FakeDeath</c> / <c>SilentMove</c> / <c>Relax</c>), scaled by
/// <c>getTicksMultiplier() = ticks · EffectTickRatio / 1000</c> (666 by default in <c>Character.ini</c>), and
/// <c>MpConsumePerLevel</c> additionally ramps with the owner's level:
/// <c>power · ticksMultiplier · ((level − 1) / 7.5)</c>.
/// </para>
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>Mobius <c>Config.EFFECT_TICK_RATIO</c> default (dist/game/config/Character.ini EffectTickRatio=666).</summary>
    internal const double EffectTickRatio = 666d;

    /// <summary>Mobius <c>Player._isFakeDeath</c> refuse: a fake-dead pawn starts no skill (ActionFailed).</summary>
    internal const int SysMsgSkillAborted = 335;
    /// <summary>Mobius <c>SystemMessageId.YOUR_SKILL_WAS_DEACTIVATED_DUE_TO_LACK_OF_MP</c>.</summary>
    internal const int SysMsgSkillDeactivatedNoMp = 140;
    /// <summary>Mobius <c>SystemMessageId.S1_HAS_BEEN_ABORTED</c> — every toggle that ends on its own.</summary>
    internal const int SysMsgToggleDeactivated = 335;
    /// <summary>Mobius <c>SystemMessageId.THAT_SKILL_HAS_BEEN_DE_ACTIVATED_AS_HP_WAS_FULLY_RECOVERED</c>.</summary>
    internal const int SysMsgRelaxHpFull = 175;

    /// <summary>Toggles whose active abnormal must be removed when the skill is used again (Mobius onMagicHitTimer).</summary>
    internal static bool IsToggleSkill(SkillDefinition template) =>
        string.Equals(template.OperateType, "T", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Mobius <c>checkUseMagicConditions</c> handles the "already active" toggle before the reuse/MP gates:
    /// <c>if (skill.isToggle() &amp;&amp; isAffectedBySkill(id)) { stopSkillEffects(...); return false; }</c>. So a press
    /// that switches a toggle OFF never hits <c>isSkillDisabled</c> / <c>checkDoCastConditions</c>.
    /// </summary>
    internal static bool IsActiveToggle(ZoneEntity player, SkillDefinition? template) =>
        template != null && IsToggleSkill(template) && ActiveToggleIndex(player, template.SkillId) >= 0;

    /// <summary>
    /// Mobius <c>SkillUseIntent</c> entry for <c>operateType T</c>: switch off when already active, otherwise run the
    /// skill's own start gates (Relax needs a seated caster, CHAMELEON_REST too) and apply the toggle abnormal.
    /// </summary>
    string? TrySkillUseToggle(
        ZoneSession session,
        ZoneEntity player,
        SkillUseIntent request,
        SkillDefinition template,
        SkillLevelData levelData,
        int level,
        long now)
    {
        int active = ActiveToggleIndex(player, request.SkillId);
        if (active >= 0)
            return DeactivateToggle(session, player, request.SkillId, template, "toggle", sendAbort: true);

        // Mobius MpConsumePerLevel toggles with only mpInitialConsume start on the initial cost
        // (Vicious Stance, Shield Fortress, Soul Cry …); everything else on mpConsume.
        int initial = ToggleInitialMpCost(player, template, levelData);
        if (player.CurMp < initial)
            return SkillProtocol.Mp;

        // Mobius Player._isFakeDeath check comes after the toggle reset: a fake-dead pawn may only switch toggles off.
        if (player.FakeDeath)
            return ActionProtocol.Blocked;

        var ps = PlayerStats(player);
        int hitMs = ZoneCombat.SkillTimeMs(template.HitTimeMs, template.MagicType == 1, ps.MAtkSpd, ps.PAtkSpd);
        int reuseMs = SkillReuseMsFor(player, template, template.ReuseDelayMsAt(level));
        if (reuseMs > 0)
            player.SkillReuseUntilMs[request.SkillId] = now + reuseMs;

        if (initial > 0)
            player.CurMp = Math.Max(0, player.CurMp - initial);

        var cast = BuildCast(player, player, request.SkillId, level, hitMs, reuseMs);
        StopForCast(player, player, now);
        FanoutSkill(player, player, cast, targetStatus: null, CasterMpStatus(player));
        session.SelectedTargetId = player.ObjectId;

        // Mobius: a toggle is applied straight away (no distance/land roll) and lasts until it is switched off.
        ApplyToggleAbnormal(player, template, levelData, level, now, request.SkillId);
        StartToggleSideEffects(player, request.SkillId, now);
        ZoneLog.Info($"[toggle] on skill={request.SkillId} lv{level} mp={player.CurMp}/{player.MaxMp} initial={initial}");
        return null;
    }

    /// <summary>Mobius <c>Player.checkUseMagicConditions</c>: a seated caster cannot start a skill.</summary>
    public const int SysMsgCannotUseWhileStanding = 31;

    internal const int ToggleFakeDeath = 60;
    internal const int ToggleSilentMove = 221;
    internal const int ToggleRelax = 226;
    internal const int ToggleChameleonRest = 296;

    internal static int ActiveToggleIndex(ZoneEntity player, int skillId)
    {
        for (int i = 0; i < player.Abnormals.Count; i++)
        {
            if (player.Abnormals[i].Id == skillId)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Mobius start cost: <c>mpInitialConsume</c> is taken at cast start and <c>mpConsume</c> at launch, so a toggle
    /// pays the sum (Vicious Stance carries only the former, Fake Death only the latter). Mobius
    /// <c>Creature.checkDoCastConditions</c> gates on exactly this sum.
    /// </summary>
    int ToggleInitialMpCost(ZoneEntity player, SkillDefinition template, SkillLevelData levelData) =>
        SkillMpCost(player, template, Math.Max(0, levelData.MpConsume) + Math.Max(0, levelData.MpInitialConsume));

    void ApplyToggleAbnormal(
        ZoneEntity player, SkillDefinition template, SkillLevelData levelData, int level, long now, int skillId)
    {
        // A toggle never expires on its own: Mobius keeps abnormalTime = -1 and relies on the owner switching it off.
        ZoneAbnormals.Apply(
            player.Abnormals,
            skillId,
            levelData.AbnormalLevel,
            ToggleForeverSeconds,
            now,
            level,
            template.AbnormalType);
        if (player.Abnormals.Count > 0)
            _byId.Join(player, ActiveSets.WithAbnormals);
        // Mobius schedules the first effect tick one period after the toggle starts, not immediately.
        int periodMs = ToggleTickPeriodMs(skillId);
        if (periodMs > 0 && ActiveToggleIndex(player, skillId) is var index && index >= 0)
            player.Abnormals[index].NextDotAtUnixMs = now + periodMs;
        FanoutAbnormal(player);
    }

    /// <summary>Sentinel duration for a toggle (<c>ZoneAbnormals</c> would overflow on <see cref="int.MaxValue"/> s).</summary>
    internal const int ToggleForeverSeconds = 60 * 60 * 24 * 30;

    /// <summary>
    /// Start hooks driven by effect type: Fake Death plays dead; Relax / ChameleonRest sit the
    /// owner (Mobius <c>sitDown(false)</c> — do not refuse for the toggle's own cast window).
    /// </summary>
    void StartToggleSideEffects(ZoneEntity player, int skillId, long now)
    {
        if (skillId == ToggleFakeDeath)
            StartFakeDeath(player, now);
        else if (IsRelaxingEffect(skillId))
            SitDown(player, checkCast: false);
    }

    /// <summary>Effect payload is <c>Relax</c> or <c>ChameleonRest</c> (skills 226 / 296 and any peer).</summary>
    bool IsRelaxingEffect(int skillId)
    {
        foreach (var effect in EffectsOf(skillId))
        {
            if (effect.Name.Equals("Relax", StringComparison.OrdinalIgnoreCase) ||
                effect.Name.Equals("ChameleonRest", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Mobius <c>stopSkillEffects(REMOVED, skillId)</c> for a toggle: drop the abnormal, fan the bar out, notice the
    /// owner (<c>SystemMessageId.S1_HAS_BEEN_ABORTED</c>, unless <paramref name="sendAbort"/> is false for a silent
    /// expiry such as the fake-death wake-up) and run the skill's exit hook.
    /// </summary>
    string? DeactivateToggle(
        ZoneSession? session,
        ZoneEntity player,
        int skillId,
        SkillDefinition? template,
        string reason,
        bool sendAbort)
    {
        int index = ActiveToggleIndex(player, skillId);
        if (index >= 0)
        {
            player.Abnormals.RemoveAt(index);
            if (player.Abnormals.Count == 0)
                _byId.Leave(player, ActiveSets.WithAbnormals);
            FanoutAbnormal(player);
        }

        if (sendAbort && player.Session != null)
        {
            string name = template?.Name ?? _game.Skills.Find(skillId)?.Name ?? $"Skill {skillId}";
            ShotNotice(player.Session, SysMsgToggleDeactivated, $"{name} has been aborted.");
        }

        if (skillId == ToggleFakeDeath && player.FakeDeath)
            StopFakeDeath(player, wakeNotice: sendAbort);
        ZoneLog.Info($"[toggle] off skill={skillId} reason={reason} obj={player.ObjectId}");
        return null;
    }

    /// <summary>
    /// Mobius <c>Creature.startFakeDeath</c>: abort the swing/cast, stop moving, tell everyone the pawn is playing dead
    /// (<c>WT_START_FAKEDEATH</c>) and clear the hate of every mob that had it targeted.
    /// </summary>
    void StartFakeDeath(ZoneEntity player, long now)
    {
        player.FakeDeath = true;
        AbortCast(player, "fakedeath", notify: false);
        player.CastingUntilMs = 0;
        player.PendingHit = null;
        if (player.Session != null)
        {
            player.Intent.AutoAttackTargetId = 0;
            player.Intent.ClearApproach();
        }
        // Mobius Creature.startFakeDeath: abortAttack() / abortCast() / stopMove(null) before the wait type goes out.
        AdvanceMove(player, now);
        bool wasMoving = player.MoveActive;
        StopTimedMove(player);
        if (wasMoving)
            FanoutStopInPlace(player);
        FanoutWaitType(player, ActionProtocol.WaitStartFakeDeath);
        ClearAggroOf(player);
    }

    /// <summary>Mobius <c>broadcastPacket(new ChangeWaitType(this, WT_STOP_FAKEDEATH))</c> then <c>Revive</c>.</summary>
    void StopFakeDeath(ZoneEntity player, bool wakeNotice)
    {
        player.FakeDeath = false;
        FanoutWaitType(player, ActionProtocol.WaitStopFakeDeath);
        FanoutRevive(player);
        if (wakeNotice && player.Session != null)
            ZoneLog.Info($"[fakedeath] wake obj={player.ObjectId}");
    }

    /// <summary>
    /// Mobius drops the hate of a fake-dead pawn: <c>AggroInfo.checkHate</c> zeroes <c>_hate</c> as soon as the attacker
    /// <c>isAlikeDead()</c>, and <c>Player.isAlikeDead()</c> includes <c>_isFakeDeath</c>. So the pawn is not a target
    /// any more, while <c>FakeDeathUntarget=False</c> only means the mob keeps its <c>_target</c> pointer.
    /// </summary>
    void ClearAggroOf(ZoneEntity player)
    {
        foreach (int id in Active(ActiveSets.Thinking))
        {
            if (!_byId.TryGetValue(id, out var npc) || npc.Kind != ZoneEntityKind.Npc)
                continue;
            if (npc.AggroTargetId != player.ObjectId && !npc.HateList.ContainsKey(player.ObjectId))
                continue;
            npc.HateList.Remove(player.ObjectId);
            if (npc.AggroTargetId == player.ObjectId)
                SetAggroTarget(npc, MostHated(npc));
        }
    }

    /// <summary>
    /// Mobius <c>Player.stopEffects(EffectType.FAKE_DEATH)</c> style wake-up: any action the playable takes ends the pose.
    /// Called from move / attack / skill use / damage before the action itself is validated.
    /// </summary>
    internal bool WakeUpFromFakeDeath(ZoneEntity player, string reason)
    {
        if (!player.FakeDeath)
            return false;
        var template = _game.Skills.Find(ToggleFakeDeath);
        DeactivateToggle(player.Session, player, ToggleFakeDeath, template, reason, sendAbort: false);
        return true;
    }

    /// <summary>Test hook: run the fake-death wake-up the way a hit / sit action would.</summary>
    internal bool WakeUpFromFakeDeathForTest(ZoneEntity player)
    {
        using (_affinity.Step())
            return WakeUpFromFakeDeath(player, "test");
    }

    /// <summary>
    /// One Mobius toggle <c>onActionTime</c> pass for <paramref name="entry"/>: drains the effect's own MP cost.
    /// Returns <see langword="true"/> when the toggle must end (owner dead / MP ran out / Relax HP is full).
    /// </summary>
    /// <param name="notified">
    /// Set when this method already sent the abort notice (MP starvation / Relax HP full), so the caller does not send
    /// <c>S1_HAS_BEEN_ABORTED</c> on top of it — Mobius prints only the specific message.
    /// </param>
    bool DrainToggleTick(ZoneEntity player, ZoneAbnormalEntry entry, long now, out bool notified)
    {
        notified = false;
        if (player.Dead || player.CurHp <= 0)
            return true;
        if (!_skillEffects.TryGet(entry.Id, out var effects))
            return false;

        foreach (var effect in effects)
        {
            double perTick;
            switch (effect.Name)
            {
                case "MpConsumePerLevel":
                    // Mobius MpConsumePerLevel.onActionTime:
                    // power · ticksMultiplier · ((level − 1) / 7.5)  — a level-1 owner pays nothing.
                    perTick = effect.Power * TicksMultiplier(effect.Ticks) * ((Math.Max(1, player.Level) - 1) / 7.5);
                    break;
                case "FakeDeath":
                case "SilentMove":
                case "Relax":
                case "ChameleonRest":
                    // Mobius FakeDeath/SilentMove/Relax/ChameleonRest.onActionTime: power · ticksMultiplier.
                    perTick = effect.Power * TicksMultiplier(effect.Ticks);
                    break;
                default:
                    continue;
            }

            // Relax / ChameleonRest end as soon as HP is fully recovered (Mobius checks before the MP drain).
            if (effect.Name is "Relax" or "ChameleonRest" && player.CurHp + 1 > player.MaxHp)
            {
                if (player.Session != null)
                    ShotNotice(player.Session, SysMsgRelaxHpFull, "That skill has been de-activated as HP was fully recovered.");
                notified = true;
                return true;
            }

            // Mobius: a seated-only toggle ends when the owner stands up (Relax, Chameleon Rest).
            if (effect.Name is "Relax" or "ChameleonRest" && !player.Sitting)
                return true;

            if (perTick <= 0)
                continue;
            if (perTick > player.CurMp)
            {
                if (player.Session != null)
                    ShotNotice(player.Session, SysMsgSkillDeactivatedNoMp,
                        "Your skill was deactivated due to lack of MP.");
                notified = true;
                return true;
            }

            player.CurMp = Math.Max(0, player.CurMp - (int)perTick);
            FanoutVitals(player, CasterMpStatus(player));
            ZoneLog.Info($"[toggle] tick skill={entry.Id} mp={player.CurMp}/{player.MaxMp} cost={perTick:F2}");
        }

        return false;
    }

    /// <summary>Mobius <c>AbstractEffect.getTicksMultiplier</c>: <c>ticks · EffectTickRatio / 1000</c>.</summary>
    internal static double TicksMultiplier(int ticks) => ticks * EffectTickRatio / 1000d;
}
