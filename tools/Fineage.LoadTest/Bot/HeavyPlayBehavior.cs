using Fineage.Net.Contract;
using Fineage.LoadTest.Models;

namespace Fineage.LoadTest.Bot;

public static class HeavyPlayBehavior
{
    /// <summary>Resend AttackRequest this long after the last one when no swing or cast is in flight.</summary>
    public static readonly TimeSpan AttackResendAfter = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Runs one heavy-play tick. Returns false for roles that use the scenario's weighted mix
    /// (<see cref="HeavyPlayRole.MixedRandom"/>, <see cref="HeavyPlayRole.EconomyBackground"/>), so economy flows,
    /// gatekeeper travel and legacy actions all go through <c>RunMixedBehaviorTickAsync</c>.
    /// </summary>
    public static async Task<bool> TryRunTickAsync(
        FineageBotClient bot,
        IReadOnlyList<FineageBotClient> fleet,
        HeavyPlayRole role,
        CancellationToken ct)
    {
        if (role is HeavyPlayRole.MixedRandom or HeavyPlayRole.EconomyBackground)
            return false;

        if (HeavyPlayRoles.IsCombatKiller(role))
        {
            await RunFighterTickAsync(bot, ct);
            return true;
        }

        // Train observers: stay in AOI, minimal commands — maximize inbound combat fan-out.
        if (bot.Rng.NextDouble() < 0.08)
            await bot.ZoneWritePublicAsync(NetOpcode.RequestActionUse,
                NetCodec.EncodeActionUse(new ActionUseNetMsg { ActionId = 12 }), ct);
        return true;
    }

    /// <summary>
    /// A fighter plays like a player farming a pack, one command in flight at a time (behaviour slots are 250 ms,
    /// far faster than a cast): pick the nearest live NPC and AttackRequest it; while a cast or the first swing is
    /// pending, do nothing; otherwise cast the next rotation skill whose reuse (from the server's MagicSkillUse)
    /// has passed, or re-send AttackRequest (a cast drops auto-attack) every <see cref="AttackResendAfter"/>.
    /// A new target only after EntityDie or despawn. With nothing in reach, walk to the spawn-layout point.
    /// </summary>
    internal static async Task RunFighterTickAsync(FineageBotClient bot, CancellationToken ct)
    {
        var hp = bot.Scenario.HeavyPlay!;
        var now = DateTimeOffset.UtcNow;
        int tick = bot.FightTicks++;
        int target = bot.FightTargetObjectId;
        var log = bot.ObservationLog;
        if (!log.IsLiveAttackTarget(target))
        {
            if (!log.TryPickAttackTarget(out target))
            {
                bot.FightTargetObjectId = 0;
                if (tick % 8 == 0)
                {
                    await bot.SendTrackedAsync(NetOpcode.MoveInput,
                        NetCodec.EncodeMoveInput(new MoveInputMsg
                        {
                            DestX = bot.SpawnX + bot.Rng.Next(-120, 120),
                            DestY = bot.SpawnY + bot.Rng.Next(-120, 120),
                            DestZ = bot.SpawnZ
                        }), ct);
                }

                return;
            }

            bot.FightTargetObjectId = target;
            await bot.ZoneWritePublicAsync(NetOpcode.SelectTarget,
                NetCodec.EncodeSelectTarget(new SelectTargetNetMsg { TargetObjectId = target }), ct);
            await SendAttackAsync(bot, target, now, ct);
            return;
        }

        if (log.HasPendingCommand(NetOpcode.UseSkill, now) || log.HasPendingCommand(NetOpcode.AttackRequest, now))
            return;

        int[] skills = hp.SkillRotation.Length > 0 ? hp.SkillRotation : [36];
        int start = tick % skills.Length;
        for (int i = 0; i < skills.Length; i++)
        {
            int skillId = skills[(start + i) % skills.Length];
            if (!log.IsSkillReady(skillId, now))
                continue;
            await bot.SendTrackedAsync(
                NetOpcode.UseSkill,
                NetCodec.EncodeUseSkill(new UseSkillNetMsg { SkillId = skillId, TargetObjectId = target, CtrlPressed = false }),
                ct,
                targetObjectId: target,
                skillId: skillId);
            return;
        }

        if (now - bot.LastAttackSentAt >= AttackResendAfter)
            await SendAttackAsync(bot, target, now, ct);
    }

    static async Task SendAttackAsync(FineageBotClient bot, int target, DateTimeOffset now, CancellationToken ct)
    {
        bot.LastAttackSentAt = now;
        await bot.SendTrackedAsync(
            NetOpcode.AttackRequest,
            NetCodec.EncodeAttackRequest(new AttackRequestNetMsg { TargetObjectId = target }),
            ct,
            targetObjectId: target);
    }

    public static async Task FormPartiesAsync(IReadOnlyList<FineageBotClient> fleet, LoadScenario scenario, CancellationToken ct)
    {
        var hp = scenario.HeavyPlay;
        if (hp == null || !hp.IsHeavyPlay)
            return;

        int partySize = Math.Max(2, hp.PartySize);
        int partyCount = hp.Mode.ToLowerInvariant() switch
        {
            "aoe-train" => 1,
            "aoe-multiparty" or "aoe-combined" => hp.PartyCount,
            _ => 0
        };
        if (partyCount == 0)
            return;

        for (int p = 0; p < partyCount; p++)
        {
            int leaderIdx = p * partySize;
            if (leaderIdx >= fleet.Count)
                break;
            var leader = fleet[leaderIdx];
            if (!leader.Stats.InWorld || leader.Stats.Disconnected)
                continue;
            for (int m = 1; m < partySize; m++)
            {
                int idx = leaderIdx + m;
                if (idx >= fleet.Count)
                    break;
                var mate = fleet[idx];
                if (!mate.Stats.InWorld || mate.Stats.Disconnected)
                    continue;
                if (mate.SelfObjectId <= 0)
                    continue;
                await leader.ZoneWritePublicAsync(NetOpcode.PartyInviteRequest,
                    NetCodec.EncodePartyInviteRequest(new PartyInviteRequestNetMsg
                    {
                        TargetObjectId = mate.SelfObjectId,
                        ItemDistribution = 0
                    }), ct);
                await Task.Delay(80, ct);
            }
        }
    }
}