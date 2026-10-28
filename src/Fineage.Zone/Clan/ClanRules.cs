using Fineage.Protocol;

namespace Fineage.Zone.Clan;

/// <summary>
/// Clan checks ported from L2J Mobius business rules. See <see cref="ClanProtocol"/>.
/// </summary>
public static class ClanRules
{
    public static int MemberLimit(int clanLevel, int pledgeType) => pledgeType switch
    {
        0 => clanLevel switch
        {
            0 => 10,
            1 => 15,
            2 => 20,
            3 => 30,
            _ => 40
        },
        -1 => 20,
        100 or 200 => clanLevel == 11 ? 30 : 20,
        1001 or 1002 or 2001 or 2002 => clanLevel is 9 or 10 or 11 ? 25 : 10,
        _ => 0
    };

    public static bool IsAlphaNumeric(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        foreach (char ch in text)
        {
            if (!char.IsLetterOrDigit(ch))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Mobius order: not alphanumeric or shorter than 2 is <see cref="ClanProtocol.Name"/>;
    /// longer than 16 is <see cref="ClanProtocol.NameLength"/>.
    /// </summary>
    public static string? NameError(string? name)
    {
        if (name == null || !IsAlphaNumeric(name) || name.Length < ClanProtocol.MinNameLength)
            return ClanProtocol.Name;
        if (name.Length > ClanProtocol.MaxNameLength)
            return ClanProtocol.NameLength;
        return null;
    }

    public static string? CreateBlock(
        int level,
        int clanId,
        long createExpiryUnixMs,
        long nowUnixMs,
        string? name,
        bool nameTaken)
    {
        if (level < ClanProtocol.MinCreateLevel)
            return ClanProtocol.Level;
        if (clanId != 0)
            return ClanProtocol.Member;
        if (PenaltyActive(createExpiryUnixMs, nowUnixMs))
            return ClanProtocol.CreatePenalty;
        if (NameError(name) is { } nameError)
            return nameError;
        if (nameTaken)
            return ClanProtocol.NameTaken;
        return null;
    }

    public static string? InviteBlock(
        bool inClan,
        bool leader,
        int pledgeType,
        bool self,
        bool targetVisible,
        bool targetBusy,
        bool selfBusy,
        bool clanRecruitPenalty,
        bool targetInClan,
        bool targetJoinPenalty,
        int memberCount,
        int clanLevel)
    {
        if (!inClan)
            return ClanProtocol.None;
        if (!leader)
            return ClanProtocol.Leader;
        if (pledgeType != ClanProtocol.MainPledge)
            return ClanProtocol.Pledge;
        if (self)
            return ClanProtocol.Self;
        if (!targetVisible)
            return ClanProtocol.Target;
        if (targetBusy)
            return ClanProtocol.Busy;
        if (selfBusy)
            return ClanProtocol.Waiting;
        if (clanRecruitPenalty)
            return ClanProtocol.RecruitPenalty;
        if (targetInClan)
            return ClanProtocol.TargetClan;
        if (targetJoinPenalty)
            return ClanProtocol.JoinPenalty;
        if (memberCount >= MemberLimit(clanLevel, pledgeType))
            return ClanProtocol.Full;
        return null;
    }

    public static string? LeaveBlock(bool inClan, bool leader, bool inCombat)
    {
        if (!inClan)
            return ClanProtocol.None;
        if (leader)
            return ClanProtocol.LeaderLeave;
        if (inCombat)
            return ClanProtocol.Combat;
        return null;
    }

    public static string? KickBlock(bool inClan, bool leader, bool self, bool targetInClan, bool targetInCombat)
    {
        if (!inClan)
            return ClanProtocol.None;
        if (!leader)
            return ClanProtocol.Leader;
        if (self)
            return ClanProtocol.DismissSelf;
        if (!targetInClan)
            return ClanProtocol.Target;
        if (targetInCombat)
            return ClanProtocol.Combat;
        return null;
    }

    public static string? DissolveBlock(bool inClan, bool leader, bool alreadyDissolving)
    {
        if (!inClan)
            return ClanProtocol.None;
        if (!leader)
            return ClanProtocol.Leader;
        if (alreadyDissolving)
            return ClanProtocol.Dissolving;
        return null;
    }

    public static string? CancelDissolveBlock(bool inClan, bool leader, bool dissolving)
    {
        if (!inClan)
            return ClanProtocol.None;
        if (!leader)
            return ClanProtocol.Leader;
        if (!dissolving)
            return ClanProtocol.NotDissolving;
        return null;
    }

    public static bool PenaltyActive(long expiryUnixMs, long nowUnixMs) => expiryUnixMs > nowUnixMs;

    public static bool InviteExpired(long expiresUnixMs, long nowUnixMs) => nowUnixMs >= expiresUnixMs;

    public static bool DissolveDue(long dissolvingExpiryUnixMs, long nowUnixMs) =>
        dissolvingExpiryUnixMs != 0 && dissolvingExpiryUnixMs <= nowUnixMs;

    public static bool IsInCombat(long combatUntilUnixMs, long nowUnixMs) => combatUntilUnixMs > nowUnixMs;

    public static long JoinPenaltyExpiry(long nowUnixMs) => nowUnixMs + ClanProtocol.JoinDays * ClanProtocol.DayMillis;

    public static long CreatePenaltyExpiry(long nowUnixMs) => nowUnixMs + ClanProtocol.CreateDays * ClanProtocol.DayMillis;

    public static long DissolveExpiry(long nowUnixMs) => nowUnixMs + ClanProtocol.DissolveDays * ClanProtocol.DayMillis;

    public static long InviteDeadline(long nowUnixMs) => nowUnixMs + ClanProtocol.InviteTimeoutSeconds * 1000L;
}
