namespace Fineage.Net.Contract;

public sealed class PackageToListNetMsg
{
    public PackageToListEntryNet[] Characters { get; set; } = System.Array.Empty<PackageToListEntryNet>();
}

public sealed class PackageToListEntryNet
{
    public int ObjectId { get; set; }
    public string Name { get; set; } = "";
}

public sealed class PackageSendableListNetMsg
{
    public int RecipientObjectId { get; set; }
    public int Adena { get; set; }
    public ItemListEntryNet[] Items { get; set; } = System.Array.Empty<ItemListEntryNet>();
}

public sealed class PackageSendRequestNetMsg
{
    public int RecipientObjectId { get; set; }
    public ShopObjectCountNet[] Lines { get; set; } = System.Array.Empty<ShopObjectCountNet>();
}

public sealed class PackageSendableListRequestNetMsg
{
    public int RecipientObjectId { get; set; }
}