using Fineage.Game.Quests;
namespace Fineage.Game;

public sealed partial class PlayerInventory
{
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

    /// <summary>
    /// Removes <paramref name="count"/> from the stack of <paramref name="itemId"/>.
    /// <paramref name="removed"/> is true when the stack is gone; <paramref name="stack"/> is that row with count 0.
    /// Otherwise <paramref name="stack"/> is the reduced stack.
    /// </summary>
    /// <summary>
    /// Removes <paramref name="count"/> from the stack with this object id.
    /// An equipped piece is left in place and the call returns false.
    /// <paramref name="removed"/> is true when the stack is gone; <paramref name="item"/> then has count 0.
    /// </summary>
    public bool TryTakeFrom(int objectId, int count, out ItemInstance? item, out bool removed)
    {
        item = null;
        removed = false;
        if (count < 1)
            return false;
        var found = Find(objectId);
        if (found == null || found.IsEquipped || found.Count < count)
            return false;
        if (found.Count == count)
        {
            found.Count = 0;
            _items.Remove(found);
            item = found;
            removed = true;
            return true;
        }

        found.Count -= count;
        item = found;
        return true;
    }

    public bool TryConsume(int itemId, int count, out ItemInstance? stack, out bool removed)
    {
        stack = null;
        removed = false;
        if (count < 1)
            return false;

        var found = FindByItemId(itemId);
        if (found == null || found.Count < count)
            return false;

        if (found.Count == count)
        {
            if (found.IsEquipped)
                Unequip(found);
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

    ItemInstance Create(ItemTemplate template, int count, int enchantLevel)
    {
        var item = new ItemInstance(_ids.Next(), template, count);
        item.EnchantLevel = enchantLevel;
        return item;
    }

    static void CheckEnchant(int level)
    {
        if (level < 0 || level > EnchantRules.AbsoluteMax)
            throw new ArgumentOutOfRangeException(nameof(level), level, "Enchant level must be 0..65535.");
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
