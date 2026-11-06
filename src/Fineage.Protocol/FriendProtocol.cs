namespace Fineage.Protocol;

/// <summary>Friends + block list (Mobius FriendList / BlockList). #285</summary>
public static class FriendProtocol
{
    public const int MaxFriends = 128;
    public const int MaxBlocked = 128;

    public const int BlockAdd = 0;
    public const int BlockRemove = 1;
    public const int BlockList = 2;
    public const int BlockAllOn = 3;
    public const int BlockAllOff = 4;

    public const string Missing = "friend_missing";
    public const string Self = "friend_self";
    public const string Busy = "friend_busy";
    public const string Already = "friend_already";
    public const string NotFriend = "friend_not";
    public const string Blocked = "friend_blocked";
    public const string Limit = "friend_limit";
    public const string Olympiad = "friend_olympiad";
}

/// <summary>Invite a player by name (Mobius RequestFriendInvite).</summary>
public sealed class FriendInviteMsg
{
    public string Name { get; set; } = "";
}

/// <summary>Answer a friend invite (Mobius RequestAnswerFriendInvite). Response 1 = accept.</summary>
public sealed class FriendAnswerMsg
{
    public int Response { get; set; }
}

/// <summary>Remove a friend by name (Mobius RequestFriendDel).</summary>
public sealed class FriendDeleteMsg
{
    public string Name { get; set; } = "";
}

/// <summary>Open friend list (Mobius RequestFriendList). Empty body.</summary>
public sealed class FriendListRequestMsg
{
}

/// <summary>Block / unblock / list / block-all (Mobius RequestBlock).</summary>
public sealed class BlockRequestMsg
{
    public int Type { get; set; }
    public string Name { get; set; } = "";
}
