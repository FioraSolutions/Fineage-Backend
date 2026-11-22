namespace Fineage.Game.Services;

/// <summary>One private-warehouse stack. Object ids come from the zone <see cref="ItemObjectIdAllocator"/>.</summary>
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
/// A character's private warehouse, freight hold, or clan warehouse.
/// Plain stackables share one stack per item id. Non-stackables are one piece per object id.
/// Object ids are owned by <see cref="ItemObjectIdAllocator"/> so a row cannot collide with a bag slot.
/// </summary>
public sealed class PrivateWarehouse
{
    public const int FirstObjectId = 2_000_000;

    readonly ItemCatalog _catalog;
    readonly ItemObjectIdAllocator _ids;
    readonly List<WarehouseStack> _items = [];
    readonly List<string> _loadWarnings = [];

    public PrivateWarehouse(ItemCatalog catalog, ItemObjectIdAllocator ids)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(ids);
        _catalog = catalog;
        _ids = ids;
    }

    public IReadOnlyList<string> LoadWarnings => _loadWarnings;

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
            var existing = FindPlain(itemId);
            if (existing != null)
            {
                existing.Count = checked(existing.Count + count);
                created = false;
                return existing;
            }

            var stack = new WarehouseStack(_ids.Next(), itemId, count, 0);
            _items.Add(stack);
            created = true;
            return stack;
        }

        WarehouseStack? first = null;
        for (int i = 0; i < count; i++)
        {
            var piece = new WarehouseStack(_ids.Next(), itemId, 1, enchantLevel);
            _items.Add(piece);
            first ??= piece;
        }

        created = true;
        return first!;
    }

    WarehouseStack? FindPlain(int itemId)
    {
        foreach (var stack in _items)
        {
            if (stack.ItemId == itemId && stack.EnchantLevel == 0)
                return stack;
        }

        return null;
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
        _loadWarnings.Clear();
        if (rows == null)
            return;
        foreach (var row in rows)
        {
            if (row.Count < 1 || row.ItemId <= 0)
                throw new InvalidDataException($"Warehouse item {row.ItemId} count {row.Count} is invalid.");
            if (!_catalog.TryGet(row.ItemId, out var template))
                throw new InvalidDataException($"Warehouse item {row.ItemId} object {row.ObjectId} has no template.");

            if (row.ObjectId > 0)
                _ids.Observe(row.ObjectId);

            if (template.Stackable)
            {
                if (row.EnchantLevel != 0)
                    throw new InvalidDataException($"Warehouse stackable item {row.ItemId} object {row.ObjectId} cannot carry an enchant level.");
                var existing = FindPlain(row.ItemId);
                if (existing != null)
                {
                    existing.Count = checked(existing.Count + row.Count);
                    _loadWarnings.Add(
                        $"warehouse merge duplicate stack item={row.ItemId} droppedObjectId={row.ObjectId} keptObjectId={existing.ObjectId} count={row.Count}");
                    continue;
                }

                int objectId = row.ObjectId > 0 ? row.ObjectId : _ids.Next();
                if (row.ObjectId > 0 && Find(row.ObjectId) != null)
                    throw new InvalidDataException($"Warehouse object id {row.ObjectId} is duplicated.");
                _items.Add(new WarehouseStack(objectId, row.ItemId, row.Count, 0));
                continue;
            }

            if (row.ObjectId > 0 && Find(row.ObjectId) != null)
                throw new InvalidDataException($"Warehouse object id {row.ObjectId} is duplicated.");
            if (row.Count != 1)
            {
                _loadWarnings.Add(
                    $"warehouse split non-stackable item={row.ItemId} objectId={row.ObjectId} count={row.Count}");
            }

            int firstId = row.ObjectId > 0 ? row.ObjectId : _ids.Next();
            _items.Add(new WarehouseStack(firstId, row.ItemId, 1, row.EnchantLevel));
            for (int i = 1; i < row.Count; i++)
                _items.Add(new WarehouseStack(_ids.Next(), row.ItemId, 1, row.EnchantLevel));
        }
    }

    public WarehouseRow[] Rows()
    {
        var rows = new WarehouseRow[_items.Count];
        for (int i = 0; i < rows.Length; i++)
        {
            var stack = _items[i];
            rows[i] = new WarehouseRow(stack.ItemId, stack.Count, stack.EnchantLevel, stack.ObjectId);
        }

        return rows;
    }
}

/// <summary>Persisted private-warehouse stack. Equipped slot is not stored; the bag slot is always the warehouse.</summary>
public readonly record struct WarehouseRow(int ItemId, int Count, int EnchantLevel, int ObjectId = 0);
