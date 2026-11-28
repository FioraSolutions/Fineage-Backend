namespace Fineage.Protocol;

/// <summary>Opcode <see cref="MsgType.RequestPetition"/> — Mobius <c>RequestPetition</c>.</summary>
public sealed class PetitionRequestMsg
{
    public string Content { get; set; } = "";
    public int Type { get; set; }
}

/// <summary>Opcode <see cref="MsgType.RequestPetitionCancel"/> — empty / unused body.</summary>
public sealed class PetitionCancelMsg
{
}
