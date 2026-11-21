namespace Fineage.Net.Contract;

public sealed class WeddingEngageAskNetMsg
{
    public string RequestorName { get; set; } = "";
    public int RequestorObjectId { get; set; }
}

public sealed class WeddingEngageAnswerNetMsg
{
    public int Response { get; set; }
}