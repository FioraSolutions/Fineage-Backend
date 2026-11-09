using Fineage.Protocol;

namespace Fineage.Zone.Inventory;

/// <summary>
/// Pure enchant window session gates before <see cref="Fineage.Game.EnchantRules"/> rolls.
/// World owns inventory mutation, roll apply, and fanout.
/// </summary>
public static class EnchantSessionRules
{
    public static string? RequestError(bool owned, bool isScroll) =>
        !owned ? InventoryProtocol.NotOwned : !isScroll ? EnchantProtocol.NotScroll : null;

    public static string? CancelError(bool hasActiveRequest) =>
        hasActiveRequest ? null : EnchantProtocol.NoRequest;

    /// <summary>
    /// <paramref name="scrollOwned"/> / <paramref name="scrollIsScroll"/> describe the active request scroll;
    /// <paramref name="targetOwned"/> is the confirm target.
    /// </summary>
    public static string? ConfirmError(
        bool hasActiveRequest,
        bool scrollOwned,
        bool scrollIsScroll,
        bool targetOwned)
    {
        if (!hasActiveRequest)
            return EnchantProtocol.NoRequest;
        if (!scrollOwned)
            return InventoryProtocol.NotOwned;
        if (!scrollIsScroll)
            return EnchantProtocol.NotScroll;
        if (!targetOwned)
            return InventoryProtocol.NotOwned;
        return null;
    }
}
