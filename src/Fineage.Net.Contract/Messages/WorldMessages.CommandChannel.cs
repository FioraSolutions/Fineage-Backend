namespace Fineage.Net.Contract;

public sealed class CommandChannelAskRequestNetMsg
{
    public string TargetName { get; set; } = "";
}

public sealed class CommandChannelAcceptRequestNetMsg
{
    public int Response { get; set; }
}

public sealed class CommandChannelOustRequestNetMsg
{
    public string TargetName { get; set; } = "";
}

public sealed class CommandChannelPartyRowNet
{
    public string LeaderName { get; set; } = "";
    public int LeaderObjectId { get; set; }
    public int MemberCount { get; set; }
}

public sealed class CommandChannelInfoNetMsg
{
    public string LeaderName { get; set; } = "";
    public int LootType { get; set; }
    public int MemberCount { get; set; }
    public CommandChannelPartyRowNet[] Parties { get; set; } = [];
}

public sealed class CommandChannelAskJoinNetMsg
{
    public string RequestorName { get; set; } = "";
    public int RequestorObjectId { get; set; }
}