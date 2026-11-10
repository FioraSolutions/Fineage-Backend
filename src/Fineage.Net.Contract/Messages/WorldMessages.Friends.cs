namespace Fineage.Net.Contract
{
    public sealed class FriendInviteNetMsg
    {
        public string Name { get; set; } = "";
    }

    public sealed class FriendAnswerNetMsg
    {
        public int Response { get; set; }
    }

    public sealed class FriendDeleteNetMsg
    {
        public string Name { get; set; } = "";
    }

    public sealed class BlockRequestNetMsg
    {
        public int Type { get; set; }
        public string Name { get; set; } = "";
    }

    public sealed class FriendInviteAskNetMsg
    {
        public string RequestorName { get; set; } = "";
    }

    public sealed class FriendRowNet
    {
        public int ObjectId { get; set; }
        public string Name { get; set; } = "";
        public int Online { get; set; }
    }

    public sealed class FriendListNetMsg
    {
        public FriendRowNet[] Friends { get; set; } = System.Array.Empty<FriendRowNet>();
    }

    /// <summary>Added=1 / removed=0; Online reflects presence when added or on login notify.</summary>

    public sealed class FriendUpdateNetMsg
    {
        public int Added { get; set; }
        public int ObjectId { get; set; }
        public string Name { get; set; } = "";
        public int Online { get; set; }
    }

    public sealed class BlockListNetMsg
    {
        public int BlockAll { get; set; }
        public FriendRowNet[] Blocked { get; set; } = System.Array.Empty<FriendRowNet>();
    }

    /// <summary>One paperdoll slot (Mobius UserInfo / CharInfo order, 17 slots, right hand = 7). Zeros when empty.</summary>
}
