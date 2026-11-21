namespace Fineage.Protocol;

public sealed class PackageToListMsg
{
    public PackageToListEntryMsg[] Characters { get; set; } = [];
}

public sealed class PackageToListEntryMsg
{
    public int ObjectId { get; set; }
    public string Name { get; set; } = "";
}

public sealed class PackageSendableListMsg
{
    public int RecipientObjectId { get; set; }
    public int Adena { get; set; }
    public ItemListEntryMsg[] Items { get; set; } = [];
}