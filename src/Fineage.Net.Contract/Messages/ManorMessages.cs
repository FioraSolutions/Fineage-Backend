namespace Fineage.Net.Contract
{
    /// <summary>Manor actions (<see cref="ManorCommandNetMsg.Kind"/>), after the Mobius manor packets.</summary>
    public static class ManorCommandKind
    {
        /// <summary>Seed sale page for <c>CastleId</c> (Mobius ExShowSeedInfo; <c>Next</c> = next period).</summary>
        public const int SeedInfo = 0;
        /// <summary>Crop purchase page for <c>CastleId</c> (Mobius ExShowCropInfo).</summary>
        public const int CropInfo = 1;
        /// <summary>Every crop with its levels, rewards and reference prices (Mobius ExShowManorDefaultInfo).</summary>
        public const int DefaultInfo = 2;
        /// <summary>Seeds the manager sells this period (Mobius BuyListSeed). The manager npc must be in range.</summary>
        public const int BuyList = 3;
        /// <summary>Rows: ItemId = seed id, Count (Mobius RequestBuySeed).</summary>
        public const int BuySeeds = 4;
        /// <summary>Crops in the bag and the castles that buy them (Mobius ExShowSellCropList).</summary>
        public const int SellList = 5;
        /// <summary>Rows: ObjectId, ItemId = crop id, ManorId, Count (Mobius RequestProcureCropList).</summary>
        public const int SellCrops = 6;
        /// <summary>Lord: next period seed sales. Rows: ItemId = seed id, Count = amount, Price (Mobius RequestSetSeed).</summary>
        public const int SetSeeds = 7;
        /// <summary>Lord: next period crop purchases. Rows: ItemId = crop id, Count, Price, RewardType 1 / 2 (Mobius RequestSetCrop).</summary>
        public const int SetCrops = 8;
        /// <summary>Lord: next period seed settings page with limits and price bounds (Mobius ExShowSeedSetting).</summary>
        public const int SeedSetting = 9;
        /// <summary>Lord: next period crop settings page (Mobius ExShowCropSetting).</summary>
        public const int CropSetting = 10;
    }

    /// <summary>One request row. Fields not used by a kind are 0.</summary>
    public sealed class ManorRowNet
    {
        public int ItemId { get; set; }
        public int ObjectId { get; set; }
        public int ManorId { get; set; }
        public long Count { get; set; }
        public long Price { get; set; }
        public int RewardType { get; set; }
    }

    /// <summary>Client → zone: one manor action. <see cref="NpcObjectId"/> is the manor manager or chamberlain spoken to.</summary>
    public sealed class ManorCommandNetMsg
    {
        public int Kind { get; set; }
        public int CastleId { get; set; }
        public int NpcObjectId { get; set; }
        public bool Next { get; set; }
        public ManorRowNet[] Rows { get; set; } = System.Array.Empty<ManorRowNet>();
    }

    /// <summary>One seed row: template data plus the castle's sale row (amounts 0 when it does not sell it).</summary>
    public sealed class ManorSeedNet
    {
        public int SeedId { get; set; }
        public int CropId { get; set; }
        public int Level { get; set; }
        public int Reward1 { get; set; }
        public int Reward2 { get; set; }
        public bool Alternative { get; set; }
        public long Limit { get; set; }
        public long ReferencePrice { get; set; }
        public long MinPrice { get; set; }
        public long MaxPrice { get; set; }
        public long Amount { get; set; }
        public long StartAmount { get; set; }
        public long Price { get; set; }
        /// <summary>Settings pages: the current period row next to the next period one.</summary>
        public long CurrentStartAmount { get; set; }
        public long CurrentPrice { get; set; }
    }

    /// <summary>One crop row: template data plus a castle's purchase row; the sell list adds the bag stack.</summary>
    public sealed class ManorCropNet
    {
        public int CropId { get; set; }
        public int SeedId { get; set; }
        public int Level { get; set; }
        public int Reward1 { get; set; }
        public int Reward2 { get; set; }
        public long Limit { get; set; }
        public long ReferencePrice { get; set; }
        public long MinPrice { get; set; }
        public long MaxPrice { get; set; }
        public int ManorId { get; set; }
        public long Amount { get; set; }
        public long StartAmount { get; set; }
        public long Price { get; set; }
        public int RewardType { get; set; }
        public int InventoryObjectId { get; set; }
        public long InventoryCount { get; set; }
        public long CurrentStartAmount { get; set; }
        public long CurrentPrice { get; set; }
        public int CurrentRewardType { get; set; }
    }

    /// <summary>
    /// Zone → client: a manor window. <see cref="View"/> echoes the <see cref="ManorCommandKind"/> that produced it;
    /// <see cref="Mode"/> is 0 disabled, 1 next period editable, 2 maintenance, 3 approved.
    /// </summary>
    public sealed class ManorInfoNetMsg
    {
        public int View { get; set; }
        public int CastleId { get; set; }
        public bool Next { get; set; }
        public int Mode { get; set; }
        public long NextChangeUnixMs { get; set; }
        /// <summary>Lord pages only: castle treasury and the next period cost (Mobius getManorCost).</summary>
        public long Treasury { get; set; }
        public long ManorCost { get; set; }
        public long Adena { get; set; }
        public ManorSeedNet[] Seeds { get; set; } = System.Array.Empty<ManorSeedNet>();
        public ManorCropNet[] Crops { get; set; } = System.Array.Empty<ManorCropNet>();
    }
}
