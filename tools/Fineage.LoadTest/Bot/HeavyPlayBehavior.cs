using Fineage.Net.Contract;
using Fineage.LoadTest.Models;

namespace Fineage.LoadTest.Bot;

public static class HeavyPlayBehavior
{
    /// <summary>One skill from the rotation every this many fighter ticks; the ticks between let auto-attack swing.</summary>
    public const int SkillEveryTicks = 3;

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
    /// A fighter plays like a player farming a pack: one AttackRequest per target (the server keeps
    /// auto-attacking, approaching and swinging, until the target dies), an AoE skill from the rotation every
    /// <see cref="SkillEveryTicks"/> ticks once its reuse (from the server's MagicSkillUse) has passed, and a new
    /// target only when the old one died or left view.
    /// With nothing in reach it walks to its spawn-layout point (party corner or train center).
    /// </summary>
    internal static async Task RunFighterTickAsync(FineageBotClient bot, CancellationToken ct)
    {
        var hp = bot.Scenario.HeavyPlay!;
        int tick = bot.FightTicks++;
        int target = bot.FightTargetObjectId;
        if (!bot.ObservationLog.IsLiveAttackTarget(target))
        {
            if (!bot.ObservationLog.TryPickAttackTarget(out target))
            {
                bot.FightTargetObjectId = 0;
                await bot.SendTrackedAsync(NetOpcode.MoveInput,
                    NetCodec.EncodeMoveInput(new MoveInputMsg
                    {
                        DestX = bot.SpawnX + bot.Rng.Next(-120, 120),
                        DestY = bot.SpawnY + bot.Rng.Next(-120, 120),
                        DestZ = bot.SpawnZ
                    }), ct);
                return;
            }

            bot.FightTargetObjectId = target;
            await bot.ZoneWritePublicAsync(NetOpcode.SelectTarget,
                NetCodec.EncodeSelectTarget(new SelectTargetNetMsg { TargetObjectId = target }), ct);
            await bot.SendTrackedAsync(
                NetOpcode.AttackRequest,
                NetCodec.EncodeAttackRequest(new AttackRequestNetMsg { TargetObjectId = target }),
                ct,
                targetObjectId: target);
            return;
        }

        if (tick % SkillEveryTicks != 0)
            return;

        int[] skills = hp.SkillRotation.Length > 0 ? hp.SkillRotation : [36];
        int start = (tick / SkillEveryTicks) % skills.Length;
        var now = DateTimeOffset.UtcNow;
        int skillId = 0;
        for (int i = 0; i < skills.Length && skillId == 0; i++)
        {
            int candidate = skills[(start + i) % skills.Length];
            if (bot.ObservationLog.IsSkillReady(candidate, now))
                skillId = candidate;
        }

        if (skillId == 0)
            return; // every rotation skill is on reuse; auto-attack keeps swinging

        await bot.SendTrackedAsync(
            NetOpcode.UseSkill,
            NetCodec.EncodeUseSkill(new UseSkillNetMsg { SkillId = skillId, TargetObjectId = target, CtrlPressed = false }),
            ct,
            targetObjectId: target,
            skillId: skillId);
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