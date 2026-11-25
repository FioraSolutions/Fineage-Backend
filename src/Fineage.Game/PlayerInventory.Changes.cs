namespace Fineage.Game;

public sealed partial class PlayerInventory
{
    /// <summary>
    /// One row of the mutation journal. <see cref="Change"/> is 0 add, 1 modify, 2 remove —
    /// the same numbers as the Interlude <c>InventoryUpdate</c> change type.
    /// Count and slot are the values to publish; a removed row keeps the slot it left and count 0.
    /// </summary>
    public readonly record struct PendingChange(int Change, ItemInstance Item, int Count, int EquippedSlot);

    /// <summary>A second stack of a plain stackable was folded into <see cref="KeptObjectId"/>.</summary>
    public readonly record struct CollapsedStack(int DroppedObjectId, int KeptObjectId, int ItemId, int Count);

    public const int ChangeAdd = 0;
    public const int ChangeModify = 1;
    public const int ChangeRemove = 2;

    readonly Dictionary<int, ItemInstance> _byObject = [];
    readonly Dictionary<int, PendingChange> _pending = [];
    readonly List<CollapsedStack> _collapsed = [];
    int _suppress;

    /// <summary>
    /// Cursor the client last had established. 0 means no ItemList has been sent.
    /// The first ItemList moves this to 1. Each committed InventoryUpdate adds one.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>
    /// The version a full ItemList carries. The client treats 0 as "no version", so the first snapshot is 1.
    /// A later snapshot keeps the current cursor and does not increment.
    /// </summary>
    public int EstablishCursor()
    {
        if (Version == 0)
            Version = 1;
        return Version;
    }

    public bool HasPendingChanges => _pending.Count > 0;

    public IReadOnlyList<CollapsedStack> CollapsedStacks => _collapsed;

    public void SuppressChanges() => _suppress++;

    public void ResumeChanges()
    {
        if (_suppress == 0)
            throw new InvalidOperationException("Inventory change suppression is not active.");
        _suppress--;
    }

    public void DiscardChanges() => _pending.Clear();

    public int BumpVersion() => Version = checked(Version + 1);

    public IReadOnlyList<PendingChange> DrainChanges()
    {
        if (_pending.Count == 0)
            return [];
        var list = new List<PendingChange>(_pending.Count);
        foreach (var change in _pending.Values)
            list.Add(change);
        list.Sort(static (a, b) => a.Item.ObjectId.CompareTo(b.Item.ObjectId));
        _pending.Clear();
        return list;
    }

    /// <summary>Asks the client to redraw this row. Used when a drop is refused and the slot must stay.</summary>
    public void NoteRefresh(ItemInstance item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!ReferenceEquals(Find(item.ObjectId), item))
            throw new InvalidOperationException($"Item object {item.ObjectId} is not in this inventory.");
        Note(ChangeModify, item, item.Count, SlotOf(item));
    }

    public StoredRow[] SnapshotRows()
    {
        var rows = new StoredRow[_items.Count];
        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            rows[i] = new StoredRow(
                item.ObjectId,
                item.ItemId,
                item.Count,
                item.EnchantLevel,
                SlotOf(item),
                item.CustomType1,
                item.CustomType2,
                item.Option1,
                item.Option2);
        }

        return rows;
    }

    /// <summary>Restores a partial stack after a drop whose ground spawn did not happen.</summary>
    public void RestoreCount(ItemInstance item, int count)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Count must be at least 1.");
        if (!ReferenceEquals(Find(item.ObjectId), item))
            throw new InvalidOperationException($"Item object {item.ObjectId} is not in this inventory.");
        item.Count = count;
    }

    /// <summary>Puts a fully removed piece back. Drop uses this when the ground spawn does not happen.</summary>
    public void Reinsert(ItemInstance item, int count)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Count must be at least 1.");
        if (_byObject.ContainsKey(item.ObjectId))
            throw new InvalidOperationException($"Item object {item.ObjectId} is already in this inventory.");
        item.Count = count;
        _items.Add(item);
        IndexAdd(item);
        int slot = SlotOf(item);
        if (slot >= 0)
            _equipped[slot] = item;
        Note(ChangeAdd, item, count, slot);
    }

    internal bool Reduce(ItemInstance item, int count, bool refuseIfEquipped, out bool removed)
    {
        removed = false;
        ArgumentNullException.ThrowIfNull(item);
        if (count < 1 || !ReferenceEquals(Find(item.ObjectId), item) || item.Count < count)
            return false;
        if (refuseIfEquipped && item.IsEquipped)
            return false;
        if (item.Count == count)
        {
            int slot = SlotOf(item);
            if (item.IsEquipped)
                Unequip(item);
            item.Count = 0;
            _items.Remove(item);
            IndexRemove(item);
            Note(ChangeRemove, item, 0, slot);
            removed = true;
            return true;
        }

        item.Count -= count;
        Note(ChangeModify, item, item.Count, SlotOf(item));
        return true;
    }

    void IndexAdd(ItemInstance item)
    {
        if (!_byObject.TryAdd(item.ObjectId, item))
            throw new InvalidOperationException($"Item object {item.ObjectId} is already in this inventory.");
    }

    void IndexRemove(ItemInstance item) => _byObject.Remove(item.ObjectId);

    void ResetStorage()
    {
        _byObject.Clear();
        _items.Clear();
        Array.Clear(_equipped);
        _pending.Clear();
    }

    void Note(int change, ItemInstance item, int count, int equippedSlot)
    {
        if (_suppress > 0)
            return;
        if (_pending.TryGetValue(item.ObjectId, out var existing))
        {
            if (existing.Change == ChangeRemove && change == ChangeAdd)
            {
                _pending[item.ObjectId] = new PendingChange(ChangeModify, item, count, equippedSlot);
                return;
            }

            if (change == ChangeRemove)
            {
                if (existing.Change == ChangeAdd)
                {
                    _pending.Remove(item.ObjectId);
                    return;
                }

                _pending[item.ObjectId] = new PendingChange(ChangeRemove, item, 0, equippedSlot);
                return;
            }

            int kind = existing.Change == ChangeAdd ? ChangeAdd : change;
            _pending[item.ObjectId] = new PendingChange(kind, item, count, equippedSlot);
            return;
        }

        _pending[item.ObjectId] = new PendingChange(change, item, count, equippedSlot);
    }

    /// <summary>The one plain stack of a stackable template, or null. Custom-typed stacks are not merge targets.</summary>
    ItemInstance? FindPlainStack(int itemId)
    {
        foreach (var item in _items)
        {
            if (item.ItemId != itemId || !item.Template.Stackable)
                continue;
            if (item.EnchantLevel != 0 || item.CustomType1 != 0 || item.CustomType2 != 0 || item.Option1 != 0 || item.Option2 != 0)
                continue;
            return item;
        }

        return null;
    }

    static int SlotOf(ItemInstance item) => item.EquippedSlot is { } slot ? (int)slot : -1;

    void RememberCollapsed(int droppedObjectId, ItemInstance kept, int count) =>
        _collapsed.Add(new CollapsedStack(droppedObjectId, kept.ObjectId, kept.ItemId, count));
}
