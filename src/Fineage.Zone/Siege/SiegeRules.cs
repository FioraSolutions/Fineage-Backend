using Fineage.Protocol;

namespace Fineage.Zone.Siege;

/// <summary>
/// Registration checks in the Mobius <c>RequestJoinSiege</c> / <c>Siege.checkIfCanRegister</c> order.
/// </summary>
public static class SiegeRules
{
    public static bool WindowClosed(long siegeUnixMs, long nowUnixMs, long closeMillis) =>
        siegeUnixMs - nowUnixMs <= closeMillis;

    public static string? JoinBlock(SiegeJoinCheck check)
    {
        if (!check.HasClan)
            return SiegeProtocol.Clan;
        if (!check.Leader)
            return SiegeProtocol.Leader;
        if (check.Dissolving)
            return SiegeProtocol.Dissolving;
        if (!check.AsAttacker && check.OwnerClanId <= 0)
            return SiegeProtocol.Npc;
        if (check.RegistrationClosed)
            return SiegeProtocol.Closed;
        if (check.InProgress)
            return SiegeProtocol.Progress;
        if (check.ClanLevel < check.MinClanLevel)
            return SiegeProtocol.Level;
        if (check.ClanId == check.OwnerClanId)
            return SiegeProtocol.AutoDefender;
        if (check.OwnedCastleId > 0)
            return SiegeProtocol.Owns;
        if (check.AlreadyHere)
            return SiegeProtocol.Duplicate;
        if (check.SameDayElsewhere)
            return SiegeProtocol.SameDay;
        if (check.AsAttacker && check.AttackerCount >= check.MaxAttackers)
            return SiegeProtocol.Attackers;
        if (!check.AsAttacker && check.DefenderCount >= check.MaxDefenders)
            return SiegeProtocol.Defenders;
        return null;
    }

    public static string? LeaveBlock(bool hasClan, bool leader, bool isOwner, bool registered, bool registrationClosed, bool inProgress)
    {
        if (!hasClan)
            return SiegeProtocol.Clan;
        if (!leader)
            return SiegeProtocol.Leader;
        if (isOwner)
            return SiegeProtocol.AutoDefender;
        if (!registered)
            return SiegeProtocol.None;
        if (registrationClosed)
            return SiegeProtocol.Closed;
        if (inProgress)
            return SiegeProtocol.Progress;
        return null;
    }
}

public readonly record struct SiegeJoinCheck(
    bool HasClan,
    bool Leader,
    bool Dissolving,
    bool AsAttacker,
    int ClanId,
    int ClanLevel,
    int OwnerClanId,
    int OwnedCastleId,
    bool RegistrationClosed,
    bool InProgress,
    bool AlreadyHere,
    bool SameDayElsewhere,
    int AttackerCount,
    int DefenderCount,
    int MinClanLevel,
    int MaxAttackers,
    int MaxDefenders);
