using Fineage.Game;
using Fineage.Protocol;

namespace Fineage.Zone;

/// <summary>Maps a <see cref="PlayerInventory"/> onto the enter-world item messages.</summary>
public static class InventoryMessages
{
    public const int PaperdollSlotCount = 17;

    public static UserInfoMsg UserInfo(
        int objectId,
        string name,
        int classId,
        int x,
        int y,
        int z,
        float runSpeed,
        float walkSpeed,
        bool running,
        PlayerInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        return new UserInfoMsg
        {
            ObjectId = objectId,
            Name = name,
            ClassId = classId,
            X = x,
            Y = y,
            Z = z,
            RunSpeed = runSpeed,
            WalkSpeed = walkSpeed,
            Running = running,
            Paperdoll = Paperdoll(inventory)
        };
    }

    public static ItemListMsg ItemList(PlayerInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        var rows = new ItemListEntryMsg[inventory.Items.Count];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = Entry(inventory.Items[i]);
        return new ItemListMsg { Items = rows };
    }

    /// <summary>One add, modify, or remove row. The zone has no equip request yet; tests and a later equip path share this shape.</summary>
    public static InventoryUpdateMsg Update(int change, ItemInstance item)
    {
        var row = Entry(item);
        return new InventoryUpdateMsg
        {
            Changes =
            [
                new InventoryUpdateEntryMsg
                {
                    Change = change,
                    ObjectId = row.ObjectId,
                    ItemId = row.ItemId,
                    Count = row.Count,
                    EquippedSlot = row.EquippedSlot,
                    BodySlot = row.BodySlot,
                    Kind = row.Kind,
                    Icon = row.Icon,
                    WeaponType = row.WeaponType,
                    ArmorType = row.ArmorType
                }
            ]
        };
    }

    public static int[] ItemIds(PlayerInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        var ids = new int[PaperdollSlotCount];
        for (int i = 0; i < PaperdollSlotCount; i++)
        {
            var item = inventory.GetEquipped((PaperdollSlot)i);
            if (item != null)
                ids[i] = item.ItemId;
        }

        return ids;
    }

    public static PaperdollSlotMsg[] Paperdoll(PlayerInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        var slots = new PaperdollSlotMsg[PaperdollSlotCount];
        for (int i = 0; i < PaperdollSlotCount; i++)
        {
            var item = inventory.GetEquipped((PaperdollSlot)i);
            slots[i] = new PaperdollSlotMsg
            {
                ObjectId = item?.ObjectId ?? 0,
                ItemId = item?.ItemId ?? 0
            };
        }

        return slots;
    }

    public static ItemListEntryMsg Entry(ItemInstance item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new ItemListEntryMsg
        {
            ObjectId = item.ObjectId,
            ItemId = item.ItemId,
            Count = item.Count,
            EquippedSlot = item.EquippedSlot is { } slot ? (int)slot : -1,
            BodySlot = BodyToken(item.Template.BodySlot),
            Kind = KindToken(item.Template.Kind),
            Icon = item.Template.Icon,
            WeaponType = item.Template.WeaponType == WeaponType.None ? "" : WeaponToken(item.Template.WeaponType),
            ArmorType = item.Template.ArmorType == ArmorType.None ? "" : ArmorToken(item.Template.ArmorType)
        };
    }

    static string KindToken(ItemKind kind) => kind switch
    {
        ItemKind.Weapon => "weapon",
        ItemKind.Armor => "armor",
        ItemKind.Etc => "etc",
        _ => throw new InvalidOperationException($"Unknown item kind {kind}.")
    };

    static string BodyToken(BodySlot slot) => slot switch
    {
        BodySlot.None => "none",
        BodySlot.Underwear => "underwear",
        BodySlot.LeftEar => "lear",
        BodySlot.RightEar => "rear",
        BodySlot.BothEars => "both_ears",
        BodySlot.Neck => "neck",
        BodySlot.LeftFinger => "lfinger",
        BodySlot.RightFinger => "rfinger",
        BodySlot.BothFingers => "both_fingers",
        BodySlot.Head => "head",
        BodySlot.RightHand => "rhand",
        BodySlot.LeftHand => "lhand",
        BodySlot.Gloves => "gloves",
        BodySlot.Chest => "chest",
        BodySlot.Legs => "legs",
        BodySlot.Feet => "feet",
        BodySlot.Back => "back",
        BodySlot.LrHand => "lrhand",
        BodySlot.FullArmor => "fullarmor",
        BodySlot.Hair => "hair",
        BodySlot.AllDress => "alldress",
        BodySlot.Hair2 => "hair2",
        BodySlot.HairAll => "hairall",
        _ => throw new InvalidOperationException($"Unknown body slot {slot}.")
    };

    static string WeaponToken(WeaponType type) => type switch
    {
        WeaponType.None => "",
        WeaponType.Sword => "sword",
        WeaponType.Blunt => "blunt",
        WeaponType.Dagger => "dagger",
        WeaponType.Bow => "bow",
        WeaponType.Pole => "pole",
        WeaponType.Dual => "dual",
        WeaponType.Fist => "fist",
        WeaponType.FishingRod => "fishingrod",
        WeaponType.Etc => "etc",
        _ => throw new InvalidOperationException($"Unknown weapon type {type}.")
    };

    static string ArmorToken(ArmorType type) => type switch
    {
        ArmorType.None => "",
        ArmorType.Light => "light",
        ArmorType.Heavy => "heavy",
        ArmorType.Magic => "magic",
        _ => throw new InvalidOperationException($"Unknown armor type {type}.")
    };
}
