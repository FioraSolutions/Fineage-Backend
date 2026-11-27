namespace Fineage.Protocol;

/// <summary>Opcode <see cref="MsgType.RequestShowBoard"/> — Mobius <c>RequestShowBoard</c> (unused int).</summary>
public sealed class ShowBoardRequestMsg
{
    public int Unused { get; set; }
}

/// <summary>Opcode <see cref="MsgType.RequestBBSwrite"/> — Mobius <c>RequestBBSwrite</c>.</summary>
public sealed class BBSwriteMsg
{
    public string Url { get; set; } = "";
    public string Arg1 { get; set; } = "";
    public string Arg2 { get; set; } = "";
    public string Arg3 { get; set; } = "";
    public string Arg4 { get; set; } = "";
    public string Arg5 { get; set; } = "";
}

/// <summary>Opcode <see cref="MsgType.ShowBoard"/> — Mobius <c>ShowBoard</c>.</summary>
public sealed class ShowBoardMsg
{
    /// <summary>1 = show, 0 = hide.</summary>
    public byte Show { get; set; } = 1;
    public string Content { get; set; } = "";
}
