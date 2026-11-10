namespace Fineage.Net.Contract
{
    public sealed class ItemListEntryNet
    {
        public int ObjectId { get; set; }
        public int ItemId { get; set; }
        public int Count { get; set; }
        public int EnchantLevel { get; set; }
        /// <summary>Paperdoll slot index, or -1 when in the bag.</summary>
        public int EquippedSlot { get; set; } = -1;
        /// <summary>Body token ("rhand", "lrhand", "chest", "none", ...). Lets the client resolve the paperdoll slot without guessing.</summary>
        public string BodySlot { get; set; } = "";
        /// <summary>Item kind token: "weapon", "armor", "etc", "jewel".</summary>
        public string Kind { get; set; } = "";
        /// <summary>Client icon key from the item template (icon.weapon_..._i00).</summary>
        public string Icon { get; set; } = "";
        /// <summary>Weapon token ("sword", "dagger", "blunt", ...), empty when not a weapon.</summary>
        public string WeaponType { get; set; } = "";
        /// <summary>Armor token ("light", "heavy", "magic"), empty when not armor.</summary>
        public string ArmorType { get; set; } = "";
    }

    public sealed class ItemListNetMsg
    {
        public ItemListEntryNet[] Items { get; set; } = System.Array.Empty<ItemListEntryNet>();
    }

    /// <summary>AutoSkills / known skill row (Mobius SkillList) for Net enter.</summary>

    public sealed class DropItemNetMsg
    {
        public int DropperObjectId { get; set; }
        public ulong EntityValue { get; set; }
        public uint EntityGeneration { get; set; }
        public int ItemId { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public bool Stackable { get; set; }
        public int Count { get; set; } = 1;
    }

    public sealed class PickupRequestNetMsg
    {
        public int GroundObjectId { get; set; }
    }

    /// <summary>0 add / 1 modify / 2 remove — same as V2 InventoryChange.</summary>

    public sealed class InventoryUpdateEntryNet
    {
        public int Change { get; set; }
        public int ObjectId { get; set; }
        public int ItemId { get; set; }
        public int Count { get; set; }
        public int EnchantLevel { get; set; }
        public int EquippedSlot { get; set; } = -1;
        /// <summary>Same tokens as <see cref="ItemListEntryNet"/> — client must not re-guess kind/slot from tables.</summary>
        public string BodySlot { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Icon { get; set; } = "";
        public string WeaponType { get; set; } = "";
        public string ArmorType { get; set; } = "";
    }

    public sealed class InventoryUpdateNetMsg
    {
        public InventoryUpdateEntryNet[] Changes { get; set; } = System.Array.Empty<InventoryUpdateEntryNet>();
    }

    public sealed class DropItemRequestNetMsg
    {
        public int ObjectId { get; set; }
        public int Count { get; set; } = 1;
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
    }

    /// <summary>Client → zone: Mobius <c>RequestDestroyItem</c>. No coordinates: the slot is just deleted.</summary>

    public sealed class DestroyItemRequestMsg
    {
        public int ObjectId { get; set; }
        public int Count { get; set; } = 1;
    }

    public sealed class UseItemNetMsg
    {
        public int ObjectId { get; set; }
    }

    public sealed class UnequipItemNetMsg
    {
        public int PaperdollSlot { get; set; }
    }
}
