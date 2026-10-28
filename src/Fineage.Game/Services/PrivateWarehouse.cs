namespace Fineage.Game.Services;

/// <summary>One private-warehouse stack. Object ids are session-local and start at <see cref="PrivateWarehouse.FirstObjectId"/>.</summary>
public sealed class WarehouseStack
{
    public WarehouseStack(int objectId, int itemId, int count, int enchantLevel)
    {
        ObjectId = objectId;
        ItemId = itemId;
        Count = count;
        EnchantLevel = enchantLevel;
    }

    public int ObjectId { get; }

    public int ItemId { get; }

    public int Count { get; set; }

    public int EnchantLevel { get; }
}

/// <summary>
/// A character's private warehouse. Stacks persist as item id, count, and enchant.
/// Object ids are assigned again on load.
/// </summary>
public sealed class PrivateWarehouse
{
    public const int FirstObjectId = 2_000_000;

    readonly ItemCatalog _catalog;
    readonly List<WarehouseStack> _items = [];
    int _nextId = FirstObjectId;

    public PrivateWarehouse(ItemCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    public IReadOnlyList<WarehouseStack> Items => _items;

    public int SlotCount => _items.Count;

    public WarehouseStack? Find(int objectId)
    {
        foreach (var stack in _items)
        {
            if (stack.ObjectId == objectId)
                return stack;
        }

        return null;
    }

    public WarehouseStack? FindByItemId(int itemId)
    {
        foreach (var stack in _items)
        {
            if (stack.ItemId == itemId)
                return stack;
        }

        return null;
    }

    public bool ContainsItem(int itemId) => FindByItemId(itemId) != null;

    /// <summary>Adds or merges. Returns the stack. <paramref name="created"/> is true when a new slot was used.</summary>
    public WarehouseStack Add(int itemId, int count, int enchantLevel, out bool created)
    {
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count));
        var template = _catalog.Get(itemId);
        if (template.Stackable)
        {
            if (enchantLevel != 0)
                throw new InvalidOperationException($"Stackable item {itemId} cannot carry an enchant level.");
            var existing = FindByItemId(itemId);
            if (existing != null)
            {
                existing.Count = checked(existing.Count + count);
                created = false;
                return existing;
            }
        }

        var stack = new WarehouseStack(_nextId++, itemId, count, enchantLevel);
        _items.Add(stack);
        created = true;
        return stack;
    }

    public bool TryTake(int objectId, int count, out WarehouseStack? stack, out bool removed)
    {
        stack = null;
        removed = false;
        if (count < 1)
            return false;
        var found = Find(objectId);
        if (found == null || found.Count < count)
            return false;
        if (found.Count == count)
        {
            found.Count = 0;
            _items.Remove(found);
            stack = found;
            removed = true;
            return true;
        }

        found.Count -= count;
        stack = found;
        return true;
    }

    public void Restore(IEnumerable<WarehouseRow>? rows)
    {
        _items.Clear();
        _nextId = FirstObjectId;
        if (rows == null)
            return;
        foreach (var row in rows)
        {
            if (row.Count < 1 || row.ItemId <= 0)
                throw new InvalidDataException($"Warehouse item {row.ItemId} count {row.Count} is invalid.");
            Add(row.ItemId, row.Count, row.EnchantLevel, out _);
        }
    }

    public WarehouseRow[] Rows()
    {
        var rows = new WarehouseRow[_items.Count];
        for (int i = 0; i < rows.Length; i++)
        {
            var stack = _items[i];
            rows[i] = new WarehouseRow(stack.ItemId, stack.Count, stack.EnchantLevel);
        }

        return rows;
    }
}

/// <summary>Persisted private-warehouse stack. Equipped slot is not stored; the bag slot is always the warehouse.</summary>
public readonly record struct WarehouseRow(int ItemId, int Count, int EnchantLevel);
