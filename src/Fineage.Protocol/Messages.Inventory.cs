namespace Fineage.Protocol;

// Plain DTOs for the zone world and the lobby. They are no longer serialized: the Net
// PacketEnvelope codec in Fineage.Net.Contract owns the wire, so the old key numbers are gone
// with the V2 transport.

/// <summary>One row of <see cref="ItemListMsg"/> or the item fields of <see cref="InventoryUpdateEntryMsg"/>.</summary>
public sealed class ItemListEntryMsg
{
    public int ObjectId { get; set; }
    public int ItemId { get; set; }
    public int Count { get; set; }
    /// <summary>Paperdoll index while worn, or -1 in the bag.</summary>
    public int EquippedSlot { get; set; } = -1;
    /// <summary>none, rhand, lhand, lrhand, chest, legs, fullarmor, and the other body tokens.</summary>
    public string BodySlot { get; set; } = "none";
    /// <summary>weapon, armor, or etc.</summary>
    public string Kind { get; set; } = "etc";
    public string Icon { get; set; } = "";
    /// <summary>sword, dagger, blunt, and the other weapon tokens, or empty when this is not a weapon.</summary>
    public string WeaponType { get; set; } = "";
    /// <summary>light, heavy, magic, or empty when this is not armor.</summary>
    public string ArmorType { get; set; } = "";
    /// <summary>Enchant level. 0 when the piece is not enchanted. Older clients ignore this key.</summary>
    public int EnchantLevel { get; set; }
}

/// <summary>Opcode <see cref="MsgType.ItemList"/> (1014). Every item in the entering player's inventory.</summary>
public sealed class ItemListMsg
{
    public ItemListEntryMsg[] Items { get; set; } = [];
}

/// <summary>0 add, 1 modify, 2 remove. Equip and unequip send modify rows. Give and grant send add rows.</summary>
public static class InventoryChange
{
    public const int Add = 0;
    public const int Modify = 1;
    public const int Remove = 2;
}

/// <summary>One row of <see cref="InventoryUpdateMsg"/>.</summary>
public sealed class InventoryUpdateEntryMsg
{
    /// <summary><see cref="InventoryChange.Add"/>, <see cref="InventoryChange.Modify"/>, or <see cref="InventoryChange.Remove"/>.</summary>
    public int Change { get; set; }
    public int ObjectId { get; set; }
    public int ItemId { get; set; }
    public int Count { get; set; }
    /// <summary>Paperdoll index while worn, or -1 in the bag. Remove rows still carry the last slot or -1.</summary>
    public int EquippedSlot { get; set; } = -1;
    public string BodySlot { get; set; } = "none";
    public string Kind { get; set; } = "etc";
    public string Icon { get; set; } = "";
    public string WeaponType { get; set; } = "";
    public string ArmorType { get; set; } = "";
    /// <summary>Enchant level. Remove rows still carry the level the piece had. Older clients ignore this key.</summary>
    public int EnchantLevel { get; set; }
}

/// <summary>Opcode <see cref="MsgType.InventoryUpdate"/> (1015).</summary>
public sealed class InventoryUpdateMsg
{
    public InventoryUpdateEntryMsg[] Changes { get; set; } = [];
}

/// <summary>Opcode <see cref="MsgType.GrantItemRequest"/> (13). Adds items to the requesting player's bag.</summary>
public sealed class GrantItemRequestMsg
{
    public int ItemId { get; set; }
    public int Count { get; set; } = 1;
}

/// <summary>Opcode <see cref="MsgType.PickupRequest"/> (14). Picks up a visible ground item.</summary>
public sealed class PickupRequestMsg
{
    public int ObjectId { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.UseItem"/> (15).
/// An equippable item in the bag is equipped. The same object id, already worn, is unequipped.
/// </summary>
public sealed class UseItemMsg
{
    /// <summary>Inventory object id. Not an item template id.</summary>
    public int ObjectId { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.UnequipItem"/> (16).
/// <see cref="PaperdollSlot"/> is a paperdoll index (0 Under … 16 Hair2), not a body-slot token.
/// </summary>
public sealed class UnequipItemMsg
{
    public int PaperdollSlot { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.GiveItem"/> (17). Admin/dev bag grant.
/// The zone accepts it only when <c>FINEAGE_ZONE_ADMIN</c> is <c>1</c>, <c>true</c>, or <c>yes</c>.
/// </summary>
public sealed class GiveItemMsg
{
    public int ItemId { get; set; }
    public int Count { get; set; } = 1;
}

/// <summary>Opcode <see cref="MsgType.EnchantRequest"/> (39). Selects the scroll and opens the window.</summary>
public sealed class EnchantRequestMsg
{
    /// <summary>Inventory object id of the scroll. Not an item template id.</summary>
    public int ObjectId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.EnchantConfirm"/> (40). The scroll is the one selected by the request.</summary>
public sealed class EnchantConfirmMsg
{
    /// <summary>Inventory object id of the weapon or armor to enchant.</summary>
    public int ObjectId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.EnchantCancel"/> (41). An empty map is enough.</summary>
public sealed class EnchantCancelMsg
{
}

/// <summary>
/// Opcode <see cref="MsgType.EnchantResult"/> (1060).
/// <see cref="Result"/> is <see cref="EnchantProtocol.ResultCancel"/>, <see cref="EnchantProtocol.ResultSuccess"/>,
/// <see cref="EnchantProtocol.ResultKeep"/>, <see cref="EnchantProtocol.ResultReset"/>, or <see cref="EnchantProtocol.ResultCrystallize"/>.
/// On success <see cref="EnchantLevel"/> is the level after the increase. On reset it is 0.
/// On keep and on a success that does not increase (base chance 0) it is the unchanged level.
/// </summary>
public sealed class EnchantResultMsg
{
    public int Result { get; set; }
    public int EnchantLevel { get; set; }
    /// <summary>Target object id. 0 on cancel.</summary>
    public int ObjectId { get; set; }
    /// <summary>Crystal item id gained from a normal failure. 0 when none are gained.</summary>
    public int CrystalItemId { get; set; }
    public int CrystalCount { get; set; }
}

/// <summary>Opcode <see cref="MsgType.SpawnItem"/> (1018). Ground item in AOI.</summary>
public sealed class SpawnItemMsg
{
    public int ObjectId { get; set; }
    public int ItemId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public bool Stackable { get; set; }
    public int Count { get; set; } = 1;
    public int Unknown { get; set; }
}

/// <summary>Opcode <see cref="MsgType.DropItemRequest"/> (56). Discard from bag onto the ground.</summary>
public sealed class DropItemRequestMsg
{
    public int ObjectId { get; set; }
    public int Count { get; set; } = 1;
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
}

/// <summary>Opcode <see cref="MsgType.DropItem"/> (1019). Ground drop with optional dropper for FX.</summary>
public sealed class DropItemMsg
{
    public int CharObjectId { get; set; }
    public int ObjectId { get; set; }
    public int ItemId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public bool Stackable { get; set; }
    public int Count { get; set; } = 1;
    public int Unknown { get; set; }
}

/// <summary>One deposit or withdraw line. Object id is the inventory or warehouse instance.</summary>
public sealed class WarehouseCountMsg
{
    public int ObjectId { get; set; }
    public int Count { get; set; }
}

/// <summary>Opcode <see cref="MsgType.WarehouseDepositRequest"/> (106).</summary>
public sealed class WarehouseDepositRequestMsg
{
    public int NpcObjectId { get; set; }
    public WarehouseCountMsg[] Lines { get; set; } = [];
}

/// <summary>Opcode <see cref="MsgType.WarehouseWithdrawRequest"/> (107).</summary>
public sealed class WarehouseWithdrawRequestMsg
{
    public int NpcObjectId { get; set; }
    public WarehouseCountMsg[] Lines { get; set; } = [];
}

/// <summary>Opcode <see cref="MsgType.WarehouseDepositList"/> (1106).</summary>
public sealed class WarehouseDepositListMsg
{
    public int NpcObjectId { get; set; }
    public int FeePerLine { get; set; }
    public int SlotsUsed { get; set; }
    public int SlotsMax { get; set; }
    public int Adena { get; set; }
    public ItemListEntryMsg[] Items { get; set; } = [];
}

/// <summary>Opcode <see cref="MsgType.WarehouseWithdrawList"/> (1107).</summary>
public sealed class WarehouseWithdrawListMsg
{
    public int NpcObjectId { get; set; }
    public int SlotsUsed { get; set; }
    public int SlotsMax { get; set; }
    public int InventorySlotsUsed { get; set; }
    public int InventorySlotsMax { get; set; }
    public int CurrentWeight { get; set; }
    public int MaxWeight { get; set; }
    public ItemListEntryMsg[] Items { get; set; } = [];
}
