using Fineage.Protocol;

namespace Fineage.Zone.Friends;

/// <summary>
/// Pure Mobius friends / block-list checks. World keeps invite state and list mutation under the gate.
/// </summary>
public static class FriendRules
{
    /// <summary>
    /// Refusal for <c>RequestFriendInvite</c> after name trim. Caller resolves online target / block flags.
    /// </summary>
    public static string? InviteError(
        string trimmedName,
        string selfName,
        bool targetOnlineInWorld,
        bool blockedEitherWay,
        bool alreadyFriends,
        bool eitherBusy,
        int selfFriendCount,
        int targetFriendCount)
    {
        if (trimmedName.Length == 0)
            return "bad_request";
        if (string.Equals(selfName, trimmedName, StringComparison.OrdinalIgnoreCase))
            return FriendProtocol.Self;
        if (!targetOnlineInWorld)
            return FriendProtocol.Missing;
        if (blockedEitherWay)
            return FriendProtocol.Blocked;
        if (alreadyFriends)
            return FriendProtocol.Already;
        if (eitherBusy)
            return FriendProtocol.Busy;
        if (selfFriendCount >= FriendProtocol.MaxFriends || targetFriendCount >= FriendProtocol.MaxFriends)
            return FriendProtocol.Limit;
        return null;
    }

    /// <summary>
    /// Refusal after a pending invite was claimed for an accept (response == 1). Decline never hits this.
    /// </summary>
    public static string? AcceptError(
        bool inviterOnlineInWorld,
        bool alreadyFriendsEitherWay,
        int selfFriendCount,
        int inviterFriendCount)
    {
        if (!inviterOnlineInWorld)
            return FriendProtocol.Missing;
        if (alreadyFriendsEitherWay)
            return FriendProtocol.Already;
        if (selfFriendCount >= FriendProtocol.MaxFriends || inviterFriendCount >= FriendProtocol.MaxFriends)
            return FriendProtocol.Limit;
        return null;
    }

    /// <summary>Refusal for block-add after name trim and target id resolution.</summary>
    public static string? BlockAddError(
        string trimmedName,
        string selfName,
        int targetId,
        bool alreadyBlocked,
        int blockedCount)
    {
        if (trimmedName.Length == 0)
            return "bad_request";
        if (string.Equals(selfName, trimmedName, StringComparison.OrdinalIgnoreCase))
            return FriendProtocol.Self;
        if (targetId < 1)
            return FriendProtocol.Missing;
        if (alreadyBlocked)
            return FriendProtocol.Already;
        if (blockedCount >= FriendProtocol.MaxBlocked)
            return FriendProtocol.Limit;
        return null;
    }

    public static bool IsKnownBlockType(int type) =>
        type is FriendProtocol.BlockAdd
            or FriendProtocol.BlockRemove
            or FriendProtocol.BlockList
            or FriendProtocol.BlockAllOn
            or FriendProtocol.BlockAllOff;
}
