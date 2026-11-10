using System.Text.Json;
using Fineage.Game.Quests;
using Fineage.Game.Recipes;
using Fineage.Game.Skills;

namespace Fineage.Game;

public sealed partial class GameData
{
    sealed class ItemFileDto
    {
        public List<ItemDto>? Items { get; set; }
    }

    sealed class ItemDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Kind { get; set; }
        public string? BodySlot { get; set; }
        public string? WeaponType { get; set; }
        public string? ArmorType { get; set; }
        public string? EtcType { get; set; }
        public string? Icon { get; set; }
        public bool Magic { get; set; }
        public bool Stackable { get; set; }
        public int PDef { get; set; }
        public string? Crystal { get; set; }
        public int CrystalCount { get; set; }
        public bool Enchantable { get; set; }
        public JsonElement Equip { get; set; }
        public JsonElement Scroll { get; set; }
        public int Price { get; set; }
        public int Weight { get; set; }
        /// <summary>Null when the file omits the flag. Omitted means the item can be sold.</summary>
        public bool? Sellable { get; set; }
        public bool Quest { get; set; }
        /// <summary>Mobius weapon <c>set stat</c> values (replace the class fist base when equipped).</summary>
        public int PAtk { get; set; }
        public int MAtk { get; set; }
        public int PAtkSpd { get; set; }
        public int CritRate { get; set; }
        /// <summary>Mobius <c>attack_range</c>: 40 melee, 80 polearm, 500 bow.</summary>
        public int AttackRange { get; set; }
        public int RandomDamage { get; set; }
        /// <summary>Mobius jewel <c>add stat="mDef"</c>.</summary>
        public int MDef { get; set; }
        /// <summary>Mobius jewel/armor <c>add stat="maxMp"</c>.</summary>
        public int MaxMp { get; set; }
        /// <summary>Mobius item <c>add stat="maxHp"</c>.</summary>
        public int MaxHp { get; set; }
        /// <summary>Mobius weapon <c>soulshots</c>: shots consumed per charge.</summary>
        public int Soulshots { get; set; }
        /// <summary>Mobius shield <c>sDef</c> / <c>rShld</c> / <c>sub rEvas</c>.</summary>
        public int ShieldDef { get; set; }
        public int ShieldRate { get; set; }
        public int ShieldEvasionPenalty { get; set; }
        /// <summary>Mobius etc item with <c>default_action SOULSHOT</c>.</summary>
        public bool Soulshot { get; set; }
        public string? ShotGrade { get; set; }
        /// <summary>Mobius weapon <c>spiritshots</c> per cast; shot items <c>handler SpiritShot / BlessedSpiritShot</c>.</summary>
        public int Spiritshots { get; set; }
        public bool Spiritshot { get; set; }
        public bool BlessedSpiritshot { get; set; }
    }

    sealed class KitFileDto
    {
        public List<KitDto>? Kits { get; set; }
    }

    sealed class KitDto
    {
        public int ClassId { get; set; }
        public List<KitItemDto>? Items { get; set; }
    }

    sealed class KitItemDto
    {
        public int ItemId { get; set; }
        public int Count { get; set; } = 1;
        public bool Equipped { get; set; }
    }
}
