namespace Fineage.Net.Contract
{
    public sealed class ShortCutNetMsg
    {
        public int Type { get; set; }
        public int Slot { get; set; }
        public int Id { get; set; }
        public int Level { get; set; }
    }

    /// <summary>Payload of <c>ShortCutInit</c>: every saved slot for the character.</summary>

    public sealed class ShortCutInitNetMsg
    {
        public ShortCutNetMsg[] Shortcuts { get; set; } = System.Array.Empty<ShortCutNetMsg>();
    }

    /// <summary>Payload of <c>RequestShortCutDel</c> and <c>ShortCutDelete</c> (world slot).</summary>

    public sealed class ShortCutDelNetMsg
    {
        public int Slot { get; set; }
    }

    public sealed class SkillListEntryNet
    {
        public int SkillId { get; set; }
        public int Level { get; set; }
        public int ReuseRemainingMs { get; set; }
    }

    public sealed class SkillListNetMsg
    {
        public SkillListEntryNet[] Skills { get; set; } = System.Array.Empty<SkillListEntryNet>();
    }

    /// <summary>
    /// Mobius <c>AcquireSkillList</c> row: one trainer offer (skill id, next level, max level, SP price, requirement mask).
    /// </summary>

    public sealed class AcquireSkillEntryNet
    {
        public int SkillId { get; set; }
        public int NextLevel { get; set; }
        public int MaxLevel { get; set; }
        public int SpCost { get; set; }
        /// <summary>Mobius <c>requirements</c> bitmask (1 = needs the previous level, 2 = needs a book/item).</summary>
        public int Requirements { get; set; }
    }

    /// <summary>Zone → client: Mobius <c>AcquireSkillList</c> (the trainer's learnable rows).</summary>

    public sealed class AcquireSkillListNetMsg
    {
        /// <summary>Mobius <c>AcquireSkillType</c> ordinal: 0 = class, 1 = fishing, 2 = pledge.</summary>
        public int SkillType { get; set; }
        public AcquireSkillEntryNet[] Skills { get; set; } = System.Array.Empty<AcquireSkillEntryNet>();
    }

    /// <summary>Mobius <c>AcquireSkillInfo</c> requirement row (item or prerequisite).</summary>

    public sealed class AcquireSkillRequirementNet
    {
        public int Type { get; set; }
        public int ItemId { get; set; }
        public long Count { get; set; }
        public int Unk { get; set; }
    }

    /// <summary>Zone → client: Mobius <c>AcquireSkillInfo</c> (the confirmation page for one offer).</summary>

    public sealed class AcquireSkillInfoNetMsg
    {
        public int SkillId { get; set; }
        public int Level { get; set; }
        public int SpCost { get; set; }
        /// <summary>Mobius <c>AcquireSkillType</c> ordinal.</summary>
        public int SkillType { get; set; }
        public AcquireSkillRequirementNet[] Requirements { get; set; } = System.Array.Empty<AcquireSkillRequirementNet>();
    }

    /// <summary>Client → zone: a click on the scroll in the bag (Mobius <c>RequestEnchantItem</c>).</summary>

    public sealed class EnchantRequestNetMsg
    {
        public int ScrollObjectId { get; set; }
    }

    /// <summary>Client → zone: the item the player placed in the enchant slot.</summary>

    public sealed class EnchantConfirmNetMsg
    {
        public int TargetObjectId { get; set; }
    }

    /// <summary>
    /// Zone → client: the finished enchant roll, mirroring <c>EnchantResultMsg</c>.
    /// Result ordinals follow <c>EnchantProtocol</c>: 0 cancel, 1 success, 2 keep, 3 reset, 4 crystallize.
    /// </summary>

    public sealed class EnchantResultNetMsg
    {
        public int Result { get; set; }
        /// <summary>Enchant level after the roll. 0 when the item was crystallized or reset.</summary>
        public int EnchantLevel { get; set; }
        /// <summary>Target object id. 0 on cancel.</summary>
        public int ObjectId { get; set; }
        /// <summary>Crystal item id gained from a normal failure. 0 when none are gained.</summary>
        public int CrystalItemId { get; set; }
        public int CrystalCount { get; set; }
    }

    /// <summary>Client → zone: Mobius <c>RequestCreatePledge</c> (the clan name).</summary>
}
