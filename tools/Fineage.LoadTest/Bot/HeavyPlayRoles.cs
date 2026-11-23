using Fineage.LoadTest.Models;

namespace Fineage.LoadTest.Bot;

public enum HeavyPlayRole
{
    MixedRandom,
    TrainWarlord,
    TrainObserver,
    PartyWarlord,
    PartyObserver,
    EconomyBackground
}

public static class HeavyPlayRoles
{
    public static HeavyPlayRole Resolve(LoadScenario scenario, int botId)
    {
        var hp = scenario.HeavyPlay;
        if (hp == null || !hp.IsHeavyPlay)
            return HeavyPlayRole.MixedRandom;

        return hp.Mode.ToLowerInvariant() switch
        {
            "aoe-train" => botId == 0 ? HeavyPlayRole.TrainWarlord : HeavyPlayRole.TrainObserver,
            "aoe-multiparty" => ResolveParty(botId, hp),
            "aoe-combined" => ResolveCombined(botId, scenario),
            _ => HeavyPlayRole.MixedRandom
        };
    }

    static HeavyPlayRole ResolveParty(int botId, HeavyPlayConfig hp)
    {
        int partySize = Math.Max(2, hp.PartySize);
        int partyCount = Math.Max(1, hp.PartyCount);
        int combatBots = partySize * partyCount;
        if (botId >= combatBots)
            return HeavyPlayRole.MixedRandom;
        int slot = botId % partySize;
        return slot == 0 ? HeavyPlayRole.PartyWarlord : HeavyPlayRole.PartyObserver;
    }

    static HeavyPlayRole ResolveCombined(int botId, LoadScenario scenario)
    {
        var hp = scenario.HeavyPlay!;
        int combat = hp.PartySize * hp.PartyCount;
        if (botId < combat)
            return ResolveParty(botId, hp);
        return HeavyPlayRole.EconomyBackground;
    }

    public static bool IsCombatKiller(HeavyPlayRole role) =>
        role is HeavyPlayRole.TrainWarlord or HeavyPlayRole.PartyWarlord;
}