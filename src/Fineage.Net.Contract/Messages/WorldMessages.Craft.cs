namespace Fineage.Net.Contract
{
    public sealed class RecipeBookOpenNetMsg
    {
        public int Dwarven { get; set; }
    }

    public sealed class RecipeMakeSelfNetMsg
    {
        public int RecipeId { get; set; }
    }

    public sealed class RecipeBookListNetMsg
    {
        public int Dwarven { get; set; }
        public int[] RecipeIds { get; set; } = System.Array.Empty<int>();
    }

    /// <summary>0 = success, 1 = fail (materials lost), 2 = cannot craft (no consume).</summary>

    public sealed class RecipeItemMakeInfoNetMsg
    {
        public int RecipeId { get; set; }
        public int Result { get; set; }
        public int CurrentMp { get; set; }
        public int MaxMp { get; set; }
    }

    public sealed class RecipeShopRowNet
    {
        public int RecipeId { get; set; }
        public long Price { get; set; }
    }

    public sealed class SetRecipeShopNetMsg
    {
        public string Message { get; set; } = "";
        public RecipeShopRowNet[] Items { get; set; } = System.Array.Empty<RecipeShopRowNet>();
    }

    public sealed class RecipeShopMakeInfoNetMsg
    {
        public int ManufacturerObjectId { get; set; }
        public int RecipeId { get; set; }
    }

    public sealed class RecipeShopMakeItemNetMsg
    {
        public int ManufacturerObjectId { get; set; }
        public int RecipeId { get; set; }
    }

    public sealed class RecipeShopSellListNetMsg
    {
        public int ManufacturerObjectId { get; set; }
        public int CurrentMp { get; set; }
        public int MaxMp { get; set; }
        public long BuyerAdena { get; set; }
        public RecipeShopRowNet[] Items { get; set; } = System.Array.Empty<RecipeShopRowNet>();
    }

    public sealed class RecipeShopItemInfoNetMsg
    {
        public int ManufacturerObjectId { get; set; }
        public int RecipeId { get; set; }
        public int CurrentMp { get; set; }
        public int MaxMp { get; set; }
        /// <summary>0 = ok / success, 1 = fail (fee+mats consumed).</summary>
        public int Result { get; set; }
    }
}
