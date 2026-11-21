namespace Fineage.Protocol;

public static class WeddingProtocol
{
    public const string Engage = "engage";
    public const string Divorce = "divorce";
    public const string GoToLove = "gotolove";
}

/// <summary>Zone → client: engagement confirm prompt (Mobius ConfirmDlg analogue).</summary>
public sealed class WeddingEngageAskMsg
{
    public string RequestorName { get; set; } = "";
    public int RequestorObjectId { get; set; }
}

/// <summary>Client → zone: answer engagement (1 accept, 0 decline).</summary>
public sealed class WeddingEngageAnswerMsg
{
    public int Response { get; set; }
}