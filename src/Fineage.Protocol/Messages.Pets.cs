namespace Fineage.Protocol;

/// <summary>Rename the owner's pet (Mobius <c>RequestChangePetName</c>). <see cref="Name"/> is the new name.</summary>
public sealed class ChangePetNameMsg
{
    public string Name { get; set; } = "";
}
