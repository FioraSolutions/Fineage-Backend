namespace Fineage.Game.Shops;

public enum ShopDeny
{
    Quantity,
    NotTradable,
    Pocket,
    Stock,
    Weight,
    Slots,
    Adena,
    Ingredients
}

public enum ShopTouchKind
{
    Add,
    Modify,
    Remove
}

public readonly record struct ShopLine(int ItemId, int Count);

public readonly record struct ShopObjectLine(int ObjectId, int Count);

public readonly record struct ShopTouch(ShopTouchKind Kind, ItemInstance Item);

public sealed class ShopMutation
{
    public ShopDeny? Deny { get; private init; }
    public int MissingCount { get; private init; }
    public string MissingName { get; private init; } = "";
    public List<ShopTouch> Touches { get; } = [];
    public List<ItemInstance> Taken { get; } = [];

    public bool Ok => Deny == null;

    public static ShopMutation Fail(ShopDeny deny, int missingCount = 0, string? missingName = null) => new()
    {
        Deny = deny,
        MissingCount = missingCount,
        MissingName = missingName ?? ""
    };
}

public sealed class PreparedMultiSell
{
    readonly Dictionary<int, PreparedShopEntry> _byId;

    public PreparedMultiSell(int listId, bool maintainEnchantment, bool inventoryOnly, IReadOnlyList<PreparedShopEntry> entries)
    {
        ListId = listId;
        MaintainEnchantment = maintainEnchantment;
        InventoryOnly = inventoryOnly;
        Entries = entries;
        _byId = new Dictionary<int, PreparedShopEntry>();
        foreach (var entry in entries)
            _byId[entry.EntryId] = entry;
    }

    public int ListId { get; }
    public bool MaintainEnchantment { get; }
    public bool InventoryOnly { get; }
    public IReadOnlyList<PreparedShopEntry> Entries { get; }

    public PreparedShopEntry? Find(int entryId) => _byId.GetValueOrDefault(entryId);
}

public sealed record PreparedShopEntry(
    int EntryId,
    int TemplateEntryId,
    bool Stackable,
    int TaxAmount,
    IReadOnlyList<PreparedShopPart> Ingredients,
    IReadOnlyList<PreparedShopPart> Products);

public sealed record PreparedShopPart(int ItemId, int Count, int Enchant);
