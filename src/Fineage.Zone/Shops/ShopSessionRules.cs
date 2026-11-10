using Fineage.Protocol;

namespace Fineage.Zone.Shops;

/// <summary>
/// Pure merchant-window session gates before <see cref="Fineage.Game.Shops.ShopTrade"/> runs.
/// World still owns NPC lookup, stock, tax, and inventory mutation.
/// </summary>
public static class ShopSessionRules
{
    public static string? BuyWindowError(
        bool itemsPresent,
        bool dead,
        bool shopMatchesNpcAndList,
        bool listAllowsNpc) =>
        !itemsPresent
            ? "bad_request"
            : dead
                ? InventoryProtocol.Dead
                : !shopMatchesNpcAndList || !listAllowsNpc
                    ? "bad_request"
                    : null;

    public static string? SellWindowError(
        bool itemsPresent,
        bool sellOpenOnNpc,
        bool npcCanSell) =>
        !itemsPresent || !sellOpenOnNpc || !npcCanSell ? "bad_request" : null;

    public static string? MultiWindowError(
        bool multiMatchesNpcAndList,
        bool inventoryPresent,
        bool entryFound) =>
        !multiMatchesNpcAndList || !inventoryPresent || !entryFound ? "bad_request" : null;
}
