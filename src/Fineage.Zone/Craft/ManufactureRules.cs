using Fineage.Game.Shops;
using Fineage.Protocol;

namespace Fineage.Zone.Craft;

/// <summary>
/// Pure Mobius manufacture-shop checks (open list shape, visitor make prechecks).
/// World owns range, inventory mutation, and fee transfer under the gate.
/// </summary>
public static class ManufactureRules
{
    /// <summary>Refusal for one open-workshop row before bag lookups.</summary>
    public static string? OpenRowError(
        int recipeId,
        long price,
        bool duplicateRecipeId,
        bool recipeKnown,
        bool recipeLearned)
    {
        if (recipeId < 1 || price < 0 || duplicateRecipeId)
            return PrivateStoreProtocol.Item;
        if (!recipeKnown)
            return CraftProtocol.Unknown;
        if (!recipeLearned)
            return CraftProtocol.NotLearned;
        if (price > ShopTrade.MaxAdena)
            return PrivateStoreProtocol.Adena;
        return null;
    }

    public static string? OpenListLimitError(int rowCount) =>
        rowCount > CraftProtocol.ManufactureSlots ? CraftProtocol.Limit : null;

    /// <summary>
    /// Refusal for visitor make-item before materials / weight. Caller supplies maker/customer flags.
    /// </summary>
    public static string? MakeItemError(
        bool customerDead,
        bool customerStoreOpen,
        bool customerInTrade,
        bool makerValidManufacture,
        bool selfTarget,
        bool inRange,
        bool makerDead,
        bool listingPresent,
        bool recipeKnown,
        bool recipeLearned,
        bool skillLevelOk,
        int makerCurMp,
        int mpConsume,
        long fee,
        bool customerCanPayFee,
        bool makerCanReceiveFee)
    {
        if (customerDead)
            return InventoryProtocol.Dead;
        if (customerStoreOpen || customerInTrade)
            return CraftProtocol.Busy;
        if (selfTarget)
            return "bad_request";
        if (!makerValidManufacture || makerDead || !listingPresent)
            return CraftProtocol.Closed;
        if (!inRange)
            return CraftProtocol.Range;
        if (!recipeKnown)
            return CraftProtocol.Unknown;
        if (!recipeLearned)
            return CraftProtocol.NotLearned;
        if (!skillLevelOk)
            return CraftProtocol.Skill;
        if (makerCurMp < mpConsume)
            return CraftProtocol.Mp;
        if (fee > ShopTrade.MaxAdena || (fee > 0 && !customerCanPayFee) || (fee > 0 && !makerCanReceiveFee))
            return CraftProtocol.Adena;
        return null;
    }

    /// <summary>Refusal when visitor asks make-info against a closed / OOR workshop.</summary>
    public static string? MakeInfoError(bool makerPlayer, bool listingOpen, bool inRange)
    {
        if (!makerPlayer || !listingOpen)
            return CraftProtocol.Closed;
        if (!inRange)
            return CraftProtocol.Range;
        return null;
    }
}
