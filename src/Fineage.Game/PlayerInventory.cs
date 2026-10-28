namespace Fineage.Game;

/// <summary>
/// A player's bag and paperdoll. Equip follows L2J Mobius Interlude inventory rules:
/// a new item in a slot unequips the previous one, a two-handed weapon clears the
/// left hand (and a left-hand item clears a two-handed weapon, except arrows on a bow
/// and lures on a fishing rod), full armor clears the legs, legs cannot be worn while
/// full armor is equipped, and stackable items merge into the existing stack.
/// Class and grade restrictions are not applied.
/// </summary>
public sealed class PlayerInventory
{
    /// <summary>
    /// First object id assigned to an item instance.
    /// Character ids and Talking Island NPCs (50001–50003) sit below this.
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
    public ItemInstance Add(int itemId, int count = 1)
    {
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Count must be at least 1.");

        var template = _catalog.Get(itemId);
        if (template.Stackable)
        {
            var existing = FindByItemId(itemId);
            if (existing != null)
            {
                existing.Count = checked(existing.Count + count);
                return existing;
            }

            var stack = Create(template, count);
            _items.Add(stack);
            return stack;
        }

        var first = Create(template, 1);
        _items.Add(first);
        for (var i = 1; i < count; i++)
            _items.Add(Create(template, 1));
        return first;
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

    public void Equip(int objectId) => Equip(Require(objectId));

    public void Equip(ItemInstance item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!ReferenceEquals(Find(item.ObjectId), item))
            throw new InvalidOperationException($"Item object {item.ObjectId} is not in this inventory.");
        if (item.IsEquipped)
            return;
        if (!item.Template.Equippable)
            throw new InvalidOperationException($"Item {item.ItemId} ({item.Template.Name}) has no body slot.");
        if (RejectedByFormalWear(item.Template.BodySlot))
            throw new InvalidOperationException($"Item {item.ItemId} ({item.Template.Name}) cannot be equipped over formal wear.");

        switch (item.Template.BodySlot)
        {
            case BodySlot.LrHand:
                Place(PaperdollSlot.LHand, null);
                Place(PaperdollSlot.RHand, item);
                break;
            case BodySlot.LeftHand:
                var right = GetEquipped(PaperdollSlot.RHand);
                if (right != null && right.Template.BodySlot == BodySlot.LrHand && !OffhandFits(right, item))
                    Place(PaperdollSlot.RHand, null);
                Place(PaperdollSlot.LHand, item);
                break;
            case BodySlot.Legs:
                var chest = GetEquipped(PaperdollSlot.Chest);
                if (chest != null && chest.Template.BodySlot == BodySlot.FullArmor)
                    throw new InvalidOperationException(
                        $"Item {item.ItemId} ({item.Template.Name}) cannot be equipped while full armor is worn.");
                Place(PaperdollSlot.Legs, item);
                break;
            case BodySlot.FullArmor:
                Place(PaperdollSlot.Legs, null);
                Place(PaperdollSlot.Chest, item);
                break;
            case BodySlot.BothEars:
                PlaceInFirstOpen(PaperdollSlot.Lear, PaperdollSlot.Rear, item);
                break;
            case BodySlot.BothFingers:
                PlaceInFirstOpen(PaperdollSlot.LFinger, PaperdollSlot.RFinger, item);
                break;
            case BodySlot.Hair:
                var hair = GetEquipped(PaperdollSlot.Hair);
                if (hair != null && hair.Template.BodySlot == BodySlot.HairAll)
                    Place(PaperdollSlot.Hair2, null);
                else
                    Place(PaperdollSlot.Hair, null);
                Place(PaperdollSlot.Hair, item);
                break;
            case BodySlot.Hair2:
                var wornHair = GetEquipped(PaperdollSlot.Hair);
                if (wornHair != null && wornHair.Template.BodySlot == BodySlot.HairAll)
                    Place(PaperdollSlot.Hair, null);
                else
                    Place(PaperdollSlot.Hair2, null);
                Place(PaperdollSlot.Hair2, item);
                break;
            case BodySlot.HairAll:
                Place(PaperdollSlot.Hair2, null);
                Place(PaperdollSlot.Hair, item);
                break;
            case BodySlot.AllDress:
                Place(PaperdollSlot.Legs, null);
                Place(PaperdollSlot.LHand, null);
                Place(PaperdollSlot.RHand, null);
                Place(PaperdollSlot.Head, null);
                Place(PaperdollSlot.Feet, null);
                Place(PaperdollSlot.Gloves, null);
                Place(PaperdollSlot.Chest, item);
                break;
            default:
                var slot = DirectSlot(item.Template.BodySlot);
                if (slot == null)
                    throw new InvalidOperationException($"No paperdoll rule for body slot {item.Template.BodySlot}.");
                Place(slot.Value, item);
                break;
        }
    }

    public void Unequip(int objectId) => Unequip(Require(objectId));

    public void Unequip(ItemInstance item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!ReferenceEquals(Find(item.ObjectId), item))
            throw new InvalidOperationException($"Item object {item.ObjectId} is not in this inventory.");
        if (item.EquippedSlot is PaperdollSlot slot)
            Place(slot, null);
    }

    ItemInstance Require(int objectId) =>
        Find(objectId) ?? throw new InvalidOperationException($"Item object {objectId} is not in this inventory.");

    ItemInstance Create(ItemTemplate template, int count)
    {
        return new ItemInstance(_ids.Next(), template, count);
    }

    bool RejectedByFormalWear(BodySlot body)
    {
        var chest = GetEquipped(PaperdollSlot.Chest);
        if (chest?.Template.BodySlot != BodySlot.AllDress)
            return false;

        switch (body)
        {
            case BodySlot.LrHand:
            case BodySlot.LeftHand:
            case BodySlot.RightHand:
                Place(PaperdollSlot.Chest, null);
                return false;
            case BodySlot.Legs:
            case BodySlot.Feet:
            case BodySlot.Gloves:
            case BodySlot.Head:
                return true;
            default:
                return false;
        }
    }

    void PlaceInFirstOpen(PaperdollSlot first, PaperdollSlot second, ItemInstance item)
    {
        if (GetEquipped(first) == null)
            Place(first, item);
        else if (GetEquipped(second) == null)
            Place(second, item);
        else
            Place(first, item);
    }

    void Place(PaperdollSlot slot, ItemInstance? item)
    {
        var index = (int)slot;
        var old = _equipped[index];
        if (ReferenceEquals(old, item))
            return;

        if (old != null)
        {
            _equipped[index] = null;
            if (old.EquippedSlot == slot)
                old.EquippedSlot = null;
            if (slot == PaperdollSlot.RHand && old.Template.WeaponType is WeaponType.Bow or WeaponType.FishingRod)
                Place(PaperdollSlot.LHand, null);
        }

        if (item == null)
            return;

        if (item.EquippedSlot is PaperdollSlot previous)
            _equipped[(int)previous] = null;

        _equipped[index] = item;
        item.EquippedSlot = slot;
    }

    static bool OffhandFits(ItemInstance weapon, ItemInstance offhand) =>
        (weapon.Template.WeaponType == WeaponType.Bow && offhand.Template.EtcType == EtcItemType.Arrow)
        || (weapon.Template.WeaponType == WeaponType.FishingRod && offhand.Template.EtcType == EtcItemType.Lure);

    static PaperdollSlot? DirectSlot(BodySlot body) => body switch
    {
        BodySlot.Underwear => PaperdollSlot.Under,
        BodySlot.LeftEar => PaperdollSlot.Lear,
        BodySlot.RightEar => PaperdollSlot.Rear,
        BodySlot.Neck => PaperdollSlot.Neck,
        BodySlot.LeftFinger => PaperdollSlot.LFinger,
        BodySlot.RightFinger => PaperdollSlot.RFinger,
        BodySlot.Head => PaperdollSlot.Head,
        BodySlot.RightHand => PaperdollSlot.RHand,
        BodySlot.Gloves => PaperdollSlot.Gloves,
        BodySlot.Chest => PaperdollSlot.Chest,
        BodySlot.Feet => PaperdollSlot.Feet,
        BodySlot.Back => PaperdollSlot.Cloak,
        _ => null
    };
}
