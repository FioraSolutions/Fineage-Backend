using Fineage.Game.Quests;

namespace Fineage.Game;

public sealed partial class PlayerInventory
{
    public readonly record struct StoredRow(
        int ObjectId,
        int ItemId,
        int Count,
        int EnchantLevel,
        int EquippedSlot,
        int CustomType1 = 0,
        int CustomType2 = 0,
        int Option1 = 0,
        int Option2 = 0);

    /// <summary>Replaces every row from a stored snapshot (shop ledger refusal rollback).</summary>
    public void ReplaceFromStored(IReadOnlyList<StoredRow> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        ResetStorage();
        SuppressChanges();
        try
        {
            foreach (var row in items)
            {
                if (!_catalog.TryGet(row.ItemId, out _))
                    throw new InvalidOperationException(
                        $"Item {row.ItemId} object {row.ObjectId} has no template.");
                ItemInstance instance = row.ObjectId > 0
                    ? AddRestored(row.ObjectId, row.ItemId, row.Count, row.EnchantLevel, row.CustomType1, row.CustomType2, row.Option1, row.Option2)
                    : Add(row.ItemId, row.Count, row.EnchantLevel, row.CustomType1, row.CustomType2);
                if (row.EquippedSlot >= 0 && instance.Template.Equippable)
                    Equip(instance);
            }
        }
        finally
        {
            ResumeChanges();
            DiscardChanges();
        }
    }

    public IReadOnlyList<ItemInstance> AddPieces(int itemId, int count, int enchantLevel = 0)
    {
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count));
        var template = _catalog.Get(itemId);
        if (template.Stackable)
            return [Add(itemId, count, enchantLevel)];

        var created = new List<ItemInstance>(count);
        for (int i = 0; i < count; i++)
            created.Add(Add(itemId, 1, enchantLevel));
        return created;
    }

    /// <summary>Sum of template weight times count. Equipped pieces are included.</summary>
    public long TotalWeight
    {
        get
        {
            long total = 0;
            foreach (var item in _items)
                total += (long)item.Template.Weight * item.Count;
            return total;
        }
    }

    /// <summary>
    /// Removes unequipped pieces of <paramref name="itemId"/>.
    /// <paramref name="enchantLevel"/> below 0 matches every enchant and, for single pieces, takes the lowest enchant first.
    /// Returns false and changes nothing when the bag is short.
    /// </summary>
    public bool TryTakeUnequipped(int itemId, int count, int enchantLevel, ICollection<ItemInstance> touched)
    {
        ArgumentNullException.ThrowIfNull(touched);
        if (count < 1)
            return false;

        var pool = new List<ItemInstance>();
        foreach (var item in _items)
        {
            if (item.ItemId != itemId || item.IsEquipped)
                continue;
            if (enchantLevel >= 0 && item.EnchantLevel != enchantLevel)
                continue;
            pool.Add(item);
        }

        if (pool.Count == 0)
            return false;

        if (pool[0].Template.Stackable)
        {
            var stack = pool[0];
            if (!Reduce(stack, count, refuseIfEquipped: true, out _))
                return false;
            touched.Add(stack);
            return true;
        }

        if (enchantLevel < 0)
            pool.Sort(static (left, right) => left.EnchantLevel.CompareTo(right.EnchantLevel));
        if (pool.Count < count)
            return false;

        for (int i = 0; i < count; i++)
        {
            var piece = pool[i];
            if (!Reduce(piece, 1, refuseIfEquipped: true, out _))
                return false;
            touched.Add(piece);
        }

        return true;
    }

    /// <summary>
    /// Removes <paramref name="count"/> from one unequipped stack or piece.
    /// Returns false and changes nothing when the object is missing, worn, or short.
    /// </summary>
    public bool TryReduce(ItemInstance item, int count, out bool removed) =>
        Reduce(item, count, refuseIfEquipped: true, out removed);

    /// <summary>
    /// Removes <paramref name="count"/> of <paramref name="itemId"/>.
    /// Returns false and changes nothing when the bag is short.
    /// </summary>
    public bool TryTake(int itemId, int count, ICollection<BagTouch> touches)
    {
        ArgumentNullException.ThrowIfNull(touches);
        if (count < 1 || CountOf(itemId) < count)
            return false;

        var pool = new List<ItemInstance>();
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            if (_items[i].ItemId == itemId)
                pool.Add(_items[i]);
        }

        int left = count;
        foreach (var item in pool)
        {
            if (left == 0)
                break;
            int take = Math.Min(item.Count, left);
            if (!Reduce(item, take, refuseIfEquipped: false, out bool removed))
                return false;
            left -= take;
            touches.Add(new BagTouch(removed ? BagTouchKind.Remove : BagTouchKind.Modify, item));
        }

        return left == 0;
    }

    public ItemInstance? FindByItemId(int itemId)
    {
        foreach (var item in _items)
        {
            if (item.ItemId == itemId)
                return item;
        }

        return null;
    }

    public ItemInstance? GetEquipped(PaperdollSlot slot)
    {
        var index = (int)slot;
        if ((uint)index >= SlotCount)
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "Paperdoll slot is outside 0..16.");
        return _equipped[index];
    }

    /// <summary>
    /// Adds <paramref name="count"/> of <paramref name="itemId"/>.
    /// Stackable templates merge into the stack already in the bag.
    /// Other templates become <paramref name="count"/> separate pieces.
    /// Returns the merged stack, or the first piece when several were created.
    /// </summary>
    public ItemInstance Add(int itemId, int count = 1, int enchantLevel = 0, int customType1 = 0, int customType2 = 0)
    {
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Count must be at least 1.");
        CheckEnchant(enchantLevel);

        var template = _catalog.Get(itemId);
        if (template.Stackable)
        {
            if (enchantLevel != 0 || customType1 != 0 || customType2 != 0)
                throw new InvalidOperationException($"Stackable item {itemId} cannot carry enchant or custom types.");
            var existing = FindPlainStack(itemId);
            if (existing != null)
            {
                existing.Count = checked(existing.Count + count);
                Note(ChangeModify, existing, existing.Count, SlotOf(existing));
                return existing;
            }

            var stack = Create(template, count, 0);
            _items.Add(stack);
            IndexAdd(stack);
            Note(ChangeAdd, stack, stack.Count, -1);
            return stack;
        }

        var first = Create(template, 1, enchantLevel, customType1, customType2);
        _items.Add(first);
        IndexAdd(first);
        Note(ChangeAdd, first, 1, -1);
        for (var i = 1; i < count; i++)
        {
            var piece = Create(template, 1, enchantLevel);
            _items.Add(piece);
            IndexAdd(piece);
            Note(ChangeAdd, piece, 1, -1);
        }

        return first;
    }

    /// <summary>
    /// Restores a persisted stack/piece with a durable <paramref name="objectId"/>.
    /// Stackable rows with a known id do not merge into another stack.
    /// </summary>
    public ItemInstance AddRestored(int objectId, int itemId, int count, int enchantLevel = 0, int customType1 = 0, int customType2 = 0, int option1 = 0, int option2 = 0)
    {
        if (objectId <= 0)
            throw new ArgumentOutOfRangeException(nameof(objectId), objectId, "Object id must be positive.");
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Count must be at least 1.");
        CheckEnchant(enchantLevel);
        if (Find(objectId) != null)
            throw new InvalidOperationException($"Item object {objectId} is already in this inventory.");

        var template = _catalog.Get(itemId);
        if (template.Stackable && enchantLevel != 0)
            throw new InvalidOperationException($"Stackable item {itemId} cannot carry an enchant level.");

        _ids.Observe(objectId);
        if (template.Stackable && customType1 == 0 && customType2 == 0 && option1 == 0 && option2 == 0)
        {
            var existing = FindPlainStack(itemId);
            if (existing != null)
            {
                existing.Count = checked(existing.Count + count);
                RememberCollapsed(objectId, existing, count);
                Note(ChangeModify, existing, existing.Count, SlotOf(existing));
                return existing;
            }
        }

        var item = new ItemInstance(objectId, template, count)
        {
            EnchantLevel = enchantLevel,
            CustomType1 = customType1,
            CustomType2 = customType2,
            Option1 = option1,
            Option2 = option2
        };
        _items.Add(item);
        IndexAdd(item);
        Note(ChangeAdd, item, count, -1);
        return item;
    }

    public void SetAugmentation(ItemInstance item, int option1, int option2)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!ReferenceEquals(Find(item.ObjectId), item))
            throw new InvalidOperationException($"Item object {item.ObjectId} is not in this inventory.");
        if (item.Template.Stackable)
            throw new InvalidOperationException($"Stackable item {item.ItemId} cannot be augmented.");
        item.Option1 = option1;
        item.Option2 = option2;
        Note(ChangeModify, item, item.Count, SlotOf(item));
    }

    public void ClearAugmentation(ItemInstance item)
    {
        SetAugmentation(item, 0, 0);
    }

    public void SetEnchantLevel(ItemInstance item, int level)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!ReferenceEquals(Find(item.ObjectId), item))
            throw new InvalidOperationException($"Item object {item.ObjectId} is not in this inventory.");
        CheckEnchant(level);
        if (item.Template.Stackable && level != 0)
            throw new InvalidOperationException($"Stackable item {item.ItemId} cannot carry an enchant level.");
        item.EnchantLevel = level;
        Note(ChangeModify, item, item.Count, SlotOf(item));
    }

    /// <summary>
    /// Removes one from the stack, or the whole piece when the count is 1.
    /// Returns the count still in the bag. 0 means the object was removed.
    /// An equipped piece is unequipped first.
    /// </summary>
    public int TakeOne(ItemInstance item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!Reduce(item, 1, refuseIfEquipped: false, out bool removed))
            throw new InvalidOperationException($"Item object {item.ObjectId} is not in this inventory.");
        return removed ? 0 : item.Count;
    }

    /// <summary>
    /// Removes <paramref name="count"/> from a specific bag stack.
    /// Returns false when the stack is short or not in this bag.
    /// </summary>
    public bool TryRemoveFromStack(ItemInstance item, int count, out bool removedEntirely) =>
        Reduce(item, count, refuseIfEquipped: false, out removedEntirely);

    public void Remove(ItemInstance item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!ReferenceEquals(Find(item.ObjectId), item))
            throw new InvalidOperationException($"Item object {item.ObjectId} is not in this inventory.");
        if (item.Count < 1)
            throw new InvalidOperationException($"Item object {item.ObjectId} has count {item.Count}.");
        if (!Reduce(item, item.Count, refuseIfEquipped: false, out _))
            throw new InvalidOperationException($"Item object {item.ObjectId} could not be removed.");
    }

    public void AddStarterKit(StarterKit kit)
    {
        ArgumentNullException.ThrowIfNull(kit);
        foreach (var entry in kit.Entries)
        {
            var item = Add(entry.ItemId, entry.Count);
            if (entry.Equipped)
                Equip(item);
        }
    }
}
