using Fineage.Game;
using Fineage.Protocol;

namespace Fineage.Zone;

/// <summary>Maps a <see cref="PlayerInventory"/> onto the enter-world item messages.</summary>
public static class InventoryMessages
{
    public const int PaperdollSlotCount = 17;

    /// <summary>Vitals and class-template combat stats carried on UserInfo keys 15–37.</summary>
    public readonly record struct UserInfoVitals(
        int Level,
        int CurHp,
        int MaxHp,
        int CurMp,
        int MaxMp,
        int CurCp,
        int MaxCp,
        long Exp,
        int Sp,
        int Str = 0,
        int Dex = 0,
        int Con = 0,
        int Int = 0,
        int Wit = 0,
        int Men = 0,
        int PAtk = 0,
        int MAtk = 0,
        int PDef = 0,
        int MDef = 0,
        int PAtkSpd = 0,
        int MAtkSpd = 0,
        int Accuracy = 0,
        int CritRate = 0);

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
        PlayerInventory inventory,
        int weaponGradePenalty = 0,
        int armorGradePenalty = 0,
        int clanId = 0,
        int clanCrestId = 0,
        UserInfoVitals? vitals = null)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        var penalty = new GradePenalty(weaponGradePenalty, armorGradePenalty);
        var msg = new UserInfoMsg
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
            Paperdoll = Paperdoll(inventory),
            WeaponGradePenalty = penalty.Weapon,
            ArmorGradePenalty = penalty.Armor,
            GradePenalty = penalty.SkillLevel,
            ClanId = clanId,
            ClanCrestId = clanCrestId
        };
        if (vitals is { } v)
        {
            msg.Level = v.Level;
            msg.CurHp = v.CurHp;
            msg.MaxHp = v.MaxHp;
            msg.CurMp = v.CurMp;
            msg.MaxMp = v.MaxMp;
            msg.CurCp = v.CurCp;
            msg.MaxCp = v.MaxCp;
            msg.Exp = v.Exp;
            msg.Sp = v.Sp;
            msg.Str = v.Str;
            msg.Dex = v.Dex;
            msg.Con = v.Con;
            msg.Int = v.Int;
            msg.Wit = v.Wit;
            msg.Men = v.Men;
            msg.PAtk = v.PAtk;
            msg.MAtk = v.MAtk;
            msg.PDef = v.PDef;
            msg.MDef = v.MDef;
            msg.PAtkSpd = v.PAtkSpd;
            msg.MAtkSpd = v.MAtkSpd;
            msg.Accuracy = v.Accuracy;
            msg.CritRate = v.CritRate;
        }

        return msg;
    }

    public static ItemListMsg ItemList(PlayerInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        var rows = new ItemListEntryMsg[inventory.Items.Count];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = Entry(inventory.Items[i]);
        return new ItemListMsg { Items = rows };
    }

    /// <summary>One add, modify, or remove row.</summary>
    public static InventoryUpdateMsg Update(int change, ItemInstance item) =>
        new() { Changes = [ChangeRow(change, item)] };

    /// <summary>Modify row for each item whose paperdoll slot changed. Caller orders the list.</summary>
    public static InventoryUpdateMsg Modify(IReadOnlyList<ItemInstance> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var changes = new InventoryUpdateEntryMsg[items.Count];
        for (int i = 0; i < items.Count; i++)
            changes[i] = ChangeRow(InventoryChange.Modify, items[i]);
        return new InventoryUpdateMsg { Changes = changes };
    }

    public static InventoryUpdateEntryMsg ChangeRow(int change, ItemInstance item)
    {
        var row = Entry(item);
        return new InventoryUpdateEntryMsg
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
            ArmorType = row.ArmorType,
            EnchantLevel = row.EnchantLevel
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
                ItemId = item?.ItemId ?? 0,
                EnchantLevel = item?.EnchantLevel ?? 0
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
            ArmorType = item.Template.ArmorType == ArmorType.None ? "" : ArmorToken(item.Template.ArmorType),
            EnchantLevel = item.EnchantLevel
        };
    }

    /// <summary>Enchant of each paperdoll slot. Empty slots are 0. Same order as <see cref="ItemIds"/>.</summary>
    public static int[] EnchantLevels(PlayerInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        var levels = new int[PaperdollSlotCount];
        for (int i = 0; i < PaperdollSlotCount; i++)
        {
            var item = inventory.GetEquipped((PaperdollSlot)i);
            if (item != null)
                levels[i] = item.EnchantLevel;
        }

        return levels;
    }

    /// <summary>Remove row. Count is 0. The slot is the slot before the piece left the paperdoll, or -1.</summary>
    public static InventoryUpdateEntryMsg RemoveRow(ItemInstance item, int equippedSlot)
    {
        var row = ChangeRow(InventoryChange.Remove, item);
        row.Count = 0;
        row.EquippedSlot = equippedSlot;
        return row;
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
