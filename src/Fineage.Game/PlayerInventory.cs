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
}
