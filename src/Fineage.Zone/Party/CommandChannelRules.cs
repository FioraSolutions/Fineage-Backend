using Fineage.Protocol;

namespace Fineage.Zone.Party;

/// <summary>Mobius RequestExAskJoinMPCC formation gates.</summary>
internal static class CommandChannelRules
{
    public static bool CanForm(
        bool clanLeader,
        int clanLevel,
        int pledgeClass,
        long strategyGuideCount,
        int clanImperiumLevel)
    {
        if (clanLeader && clanLevel >= CommandChannelProtocol.MinClanLevel)
            return true;
        if (strategyGuideCount > 0)
            return true;
        if (pledgeClass >= CommandChannelProtocol.MinPledgeClass && clanImperiumLevel > 0)
            return true;
        return false;
    }
}