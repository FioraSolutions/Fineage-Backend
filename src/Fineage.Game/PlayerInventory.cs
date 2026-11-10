using Fineage.Game.Quests;

namespace Fineage.Game;

/// <summary>
/// A player's bag and paperdoll. Equip follows L2J Mobius Interlude inventory rules:
/// a new item in a slot unequips the previous one, a two-handed weapon clears the
/// left hand (and a left-hand item clears a two-handed weapon, except arrows on a bow
/// and lures on a fishing rod), full armor clears the legs, legs cannot be worn while
/// full armor is equipped, and stackable items merge into the existing stack.
/// Class, race, and other item conditions are checked by the zone before <see cref="Equip"/>.
/// </summary>
public sealed partial class PlayerInventory
{
    /// <summary>
    /// First object id assigned to an item instance.
    /// Character ids stay below 50000. Legacy NPC rows use 50000–59999.
    /// Town NPC rows use 60000–99999 (<see cref="NpcObjectIdAllocator"/>).
    /// </summary>
    public const int FirstObjectId = 100_000;

    const int SlotCount = (int)PaperdollSlot.Hair2 + 1;

    readonly ItemCatalog _catalog;
    readonly ItemObjectIdAllocator _ids;
    readonly List<ItemInstance> _items = [];
    readonly ItemInstance?[] _equipped = new ItemInstance?[SlotCount];

    public PlayerInventory(ItemCatalog catalog, int firstObjectId = FirstObjectId)
        : this(catalog, new ItemObjectIdAllocator(firstObjectId))
    {
    }

    public PlayerInventory(ItemCatalog catalog, ItemObjectIdAllocator ids)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(ids);
        _catalog = catalog;
        _ids = ids;
    }

    public IReadOnlyList<ItemInstance> Items => _items;

    public ItemInstance? Find(int objectId)
    {
        foreach (var item in _items)
        {
            if (item.ObjectId == objectId)
                return item;
        }

        return null;
    }

    public int CountOf(int itemId)
    {
        int total = 0;
        foreach (var item in _items)
        {
            if (item.ItemId == itemId)
                total += item.Count;
        }

        return total;
    }

    /// <summary>Stackable items return the merged stack. Other items return every new piece.</summary>
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
            if (stack.Count < count)
                return false;
            if (stack.Count == count)
            {
                stack.Count = 0;
                _items.Remove(stack);
            }
            else
            {
                stack.Count -= count;
            }

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
            piece.Count = 0;
            _items.Remove(piece);
            touched.Add(piece);
        }

        return true;
    }

    /// <summary>
    /// Removes <paramref name="count"/> from one unequipped stack or piece.
    /// Returns false and changes nothing when the object is missing, worn, or short.
    /// </summary>
    public bool TryReduce(ItemInstance item, int count, out bool removed)
    {
        removed = false;
        if (count < 1 || item.IsEquipped || !ReferenceEquals(Find(item.ObjectId), item) || item.Count < count)
            return false;
        if (item.Count == count)
        {
            item.Count = 0;
            _items.Remove(item);
            removed = true;
            return true;
        }

        item.Count -= count;
        return true;
    }

    /// <summary>
    /// Removes <paramref name="count"/> of <paramref name="itemId"/>.
    /// Returns false and changes nothing when the bag is short.
    /// </summary>
    public bool TryTake(int itemId, int count, ICollection<BagTouch> touches)
    {
        ArgumentNullException.ThrowIfNull(touches);
        if (count < 1 || CountOf(itemId) < count)
            return false;

        int left = count;
        for (int i = _items.Count - 1; i >= 0 && left > 0; i--)
        {
            var item = _items[i];
            if (item.ItemId != itemId)
                continue;
            if (item.IsEquipped)
                Unequip(item);
            if (item.Count <= left)
            {
                left -= item.Count;
                _items.RemoveAt(i);
                touches.Add(new BagTouch(BagTouchKind.Remove, item));
            }
            else
            {
                item.Count -= left;
                left = 0;
                touches.Add(new BagTouch(BagTouchKind.Modify, item));
            }
        }

        return true;
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
    public ItemInstance Add(int itemId, int count = 1, int enchantLevel = 0)
    {
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Count must be at least 1.");
        CheckEnchant(enchantLevel);

        var template = _catalog.Get(itemId);
        if (template.Stackable)
        {
            if (enchantLevel != 0)
                throw new InvalidOperationException($"Stackable item {itemId} cannot carry an enchant level.");
            var existing = FindByItemId(itemId);
            if (existing != null)
            {
                existing.Count = checked(existing.Count + count);
                return existing;
            }

            var stack = Create(template, count, 0);
            _items.Add(stack);
            return stack;
        }

        var first = Create(template, 1, enchantLevel);
        _items.Add(first);
        for (var i = 1; i < count; i++)
            _items.Add(Create(template, 1, enchantLevel));
        return first;
    }

    /// <summary>
    /// Restores a persisted stack/piece with a durable <paramref name="objectId"/>.
    /// Stackable rows with a known id do not merge into another stack.
    /// </summary>
    public ItemInstance AddRestored(int objectId, int itemId, int count, int enchantLevel = 0)
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
        var item = new ItemInstance(objectId, template, count) { EnchantLevel = enchantLevel };
        _items.Add(item);
        return item;
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
    }

    /// <summary>
    /// Removes one from the stack, or the whole piece when the count is 1.
    /// Returns the count still in the bag. 0 means the object was removed.
    /// An equipped piece is unequipped first.
    /// </summary>
    public int TakeOne(ItemInstance item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!ReferenceEquals(Find(item.ObjectId), item))
            throw new InvalidOperationException($"Item object {item.ObjectId} is not in this inventory.");
        if (item.Count > 1)
        {
            item.Count -= 1;
            return item.Count;
        }

        Remove(item);
        return 0;
    }

    /// <summary>
    /// Removes <paramref name="count"/> from a specific bag stack.
    /// Returns false when the stack is short or not in this bag.
    /// </summary>
    public bool TryRemoveFromStack(ItemInstance item, int count, out bool removedEntirely)
    {
        removedEntirely = false;
        ArgumentNullException.ThrowIfNull(item);
        if (count < 1 || !ReferenceEquals(Find(item.ObjectId), item) || item.Count < count)
            return false;
        if (item.IsEquipped)
            Unequip(item);
        if (item.Count == count)
        {
            Remove(item);
            removedEntirely = true;
            return true;
        }

        item.Count -= count;
        return true;
    }

    public void Remove(ItemInstance item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!ReferenceEquals(Find(item.ObjectId), item))
            throw new InvalidOperationException($"Item object {item.ObjectId} is not in this inventory.");
        if (item.IsEquipped)
            Unequip(item);
        _items.Remove(item);
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
