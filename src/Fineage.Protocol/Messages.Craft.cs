namespace Fineage.Protocol;

// Plain DTOs for the zone world and the lobby. They are no longer serialized: the Net
// PacketEnvelope codec in Fineage.Net.Contract owns the wire, so the old key numbers are gone
// with the V2 transport.

/// <summary>Open the recipe book (Mobius RequestRecipeBookOpen). Dwarven=true → dwarven tab.</summary>
public sealed class RecipeBookOpenMsg
{
    public bool Dwarven { get; set; }
}

/// <summary>Craft one recipe (Mobius RequestRecipeItemMakeSelf). <see cref="RecipeId"/> is the list id.</summary>
public sealed class RecipeMakeSelfMsg
{
    public int RecipeId { get; set; }
}

/// <summary>One manufacture listing (Mobius <c>ManufactureItem</c>: recipe list id + fee).</summary>
public sealed class RecipeShopRowMsg
{
    public int RecipeId { get; set; }
    public long Price { get; set; }
}

/// <summary>Open or refresh a manufacture shop (Mobius RequestRecipeShopListSet).</summary>
public sealed class SetRecipeShopMsg
{
    public string Message { get; set; } = "";
    public RecipeShopRowMsg[] Items { get; set; } = [];
}

/// <summary>Ask manufacturer MP / recipe status (Mobius RequestRecipeShopMakeInfo).</summary>
public sealed class RecipeShopMakeInfoMsg
{
    public int ManufacturerObjectId { get; set; }
    public int RecipeId { get; set; }
}

/// <summary>Request manufacture of one recipe (Mobius RequestRecipeShopMakeItem).</summary>
public sealed class RecipeShopMakeItemMsg
{
    public int ManufacturerObjectId { get; set; }
    public int RecipeId { get; set; }
}

/// <summary>Zone → client: known recipe ids for one craft book tab.</summary>
public sealed class RecipeBookListMsg
{
    public int Dwarven { get; set; }
    public int[] RecipeIds { get; set; } = [];
}

/// <summary>Zone → client: self-craft result (0 success, 1 fail, 2 cannot).</summary>
public sealed class RecipeItemMakeInfoMsg
{
    public int RecipeId { get; set; }
    public int Result { get; set; }
    public int CurrentMp { get; set; }
    public int MaxMp { get; set; }
}

/// <summary>Zone → client: manufacture recipe list for a visitor.</summary>
public sealed class RecipeShopSellListMsg
{
    public int ManufacturerObjectId { get; set; }
    public int CurrentMp { get; set; }
    public int MaxMp { get; set; }
    public long BuyerAdena { get; set; }
    public RecipeShopRowMsg[] Items { get; set; } = [];
}

/// <summary>Zone → client: manufacturer MP + recipe after make-info / make-item.</summary>
public sealed class RecipeShopItemInfoMsg
{
    public int ManufacturerObjectId { get; set; }
    public int RecipeId { get; set; }
    public int CurrentMp { get; set; }
    public int MaxMp { get; set; }
    /// <summary>0 = ok / success, 1 = fail (fee+mats consumed).</summary>
    public int Result { get; set; }
}
