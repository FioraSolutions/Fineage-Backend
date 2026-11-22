namespace Fineage.Protocol;

/// <summary>One party row in <see cref="CommandChannelInfoMsg"/>.</summary>
public sealed class CommandChannelPartyRowMsg
{
    public string LeaderName { get; set; } = "";
    public int LeaderObjectId { get; set; }
    public int MemberCount { get; set; }
}

/// <summary>Mobius ExMultiPartyCommandChannelInfo.</summary>
public sealed class CommandChannelInfoMsg
{
    public string LeaderName { get; set; } = "";
    public int LootType { get; set; }
    public int MemberCount { get; set; }
    public CommandChannelPartyRowMsg[] Parties { get; set; } = [];
}

/// <summary>Mobius ExAskJoinMPCC.</summary>
public sealed class CommandChannelAskJoinMsg
{
    public string RequestorName { get; set; } = "";
    public int RequestorObjectId { get; set; }
}