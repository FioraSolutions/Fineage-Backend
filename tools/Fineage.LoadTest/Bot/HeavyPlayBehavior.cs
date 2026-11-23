using Fineage.Net.Contract;
using Fineage.LoadTest.Models;

namespace Fineage.LoadTest.Bot;

public static class HeavyPlayBehavior
{
    public static async Task<bool> TryRunTickAsync(
        FineageBotClient bot,
        IReadOnlyList<FineageBotClient> fleet,
        HeavyPlayRole role,
        CancellationToken ct)
    {
        if (role == HeavyPlayRole.MixedRandom)
            return false;

        if (role == HeavyPlayRole.EconomyBackground)
        {
            string action = bot.PickEconomyOrTeleportAction();
            if (action.StartsWith("economy_", StringComparison.Ordinal))
            {
                await BotEconomyFlows.RunActionAsync(bot, action, fleet, ct);
                return true;
            }

            await bot.RunWeightedActionAsync(action, fleet, ct);
            return true;
        }

        if (HeavyPlayRoles.IsCombatKiller(role))
        {
            await RunWarlordTickAsync(bot, ct);
            return true;
        }

        // Observers: stay in AOI, minimal commands — maximize inbound combat fan-out.
        if (bot.Rng.NextDouble() < 0.08)
            await bot.ZoneWritePublicAsync(NetOpcode.RequestActionUse,
                NetCodec.EncodeActionUse(new ActionUseNetMsg { ActionId = 12 }), ct);
        return true;
    }

    static async Task RunWarlordTickAsync(FineageBotClient bot, CancellationToken ct)
    {
        var hp = bot.Scenario.HeavyPlay!;
        int tick = (int)bot.Stats.ActionsAttempted;
        int phase = tick % 6;
        if (phase is 0 or 1)
        {
            int dx = bot.Rng.Next(-120, 120);
            int dy = bot.Rng.Next(-120, 120);
            await bot.SendTrackedAsync(NetOpcode.MoveInput,
                NetCodec.EncodeMoveInput(new MoveInputMsg
                {
                    DestX = bot.SpawnX + dx,
                    DestY = bot.SpawnY + dy,
                    DestZ = bot.SpawnZ
                }), ct);
            return;
        }

        if (!bot.ObservationLog.TryPickAttackTarget(out int target))
            return;

        await bot.ZoneWritePublicAsync(NetOpcode.SelectTarget,
            NetCodec.EncodeSelectTarget(new SelectTargetNetMsg { TargetObjectId = target }), ct);

        if (phase is 2 or 3)
        {
            await bot.SendTrackedAsync(
                NetOpcode.AttackRequest,
                NetCodec.EncodeAttackRequest(new AttackRequestNetMsg { TargetObjectId = target }),
                ct,
                targetObjectId: target);
            return;
        }

        int[] skills = hp.SkillRotation.Length > 0 ? hp.SkillRotation : [36];
        int skillId = skills[tick % skills.Length];
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