namespace Fineage.Protocol;

/// <summary>Opcode <see cref="MsgType.RequestWriteHeroWords"/> — Mobius <c>RequestWriteHeroWords</c>.</summary>
public sealed class WriteHeroWordsMsg
{
    public string Text { get; set; } = "";
}
