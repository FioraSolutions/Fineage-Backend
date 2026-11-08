using Fineage.Protocol;

namespace Fineage.Zone.Trade;

/// <summary>
/// Pure Mobius trade checks (offer rows, item eligibility, receiver weight/slots).
/// World keeps session state and inventory mutation under the gate.
/// </summary>
public static class TradeRules
{
    /// <summary>Which overflow message both partners get when exchange is refused.</summary>
    public enum IncomingDeny
    {
        None = 0,
        Weight = 1,
        Slots = 2
    }

    /// <summary>Refusal when adding a brand-new offer row (updates of an existing row are fine).</summary>
    public static string? NewOfferRowError(int currentRows) =>
        currentRows >= TradeProtocol.MaxOfferRows ? TradeProtocol.Item : null;

    /// <summary>
    /// Refusal for a client offer against one bag item. <paramref name="found"/> is false when the object id is missing.
    /// </summary>
    public static string? OfferItemError(
        bool found,
        bool equipped,
        bool canDropTradeStore,
        bool stackable,
        long requestedCount,
        long availableCount)
    {
        if (!found)
            return InventoryProtocol.NotOwned;
        if (equipped || !canDropTradeStore)
            return TradeProtocol.Item;
        if (requestedCount > availableCount)
            return TradeProtocol.Item;
        if (!stackable && requestedCount != 1)
            return TradeProtocol.Item;
        return null;
    }

    /// <summary>True when a saved offer row still matches the giver's bag at exchange time.</summary>
    public static bool OfferStillValid(
        bool found,
        bool equipped,
        bool stackable,
        long offeredCount,
        long availableCount)
    {
        if (!found || equipped || availableCount < offeredCount)
            return false;
        if (!stackable && offeredCount != 1)
            return false;
        return true;
    }

    /// <summary>
    /// Mobius <c>TradeList.doExchange</c>: weight first, then capacity, against the receiving side.
    /// </summary>
    public static IncomingDeny IncomingDenyOf(
        long currentWeight,
        long incomingWeight,
        long maxLoad,
        int currentItemCount,
        int incomingSlots,
        int maxSlots)
    {
        if (currentWeight + incomingWeight > maxLoad)
            return IncomingDeny.Weight;
        if (currentItemCount + incomingSlots > maxSlots)
            return IncomingDeny.Slots;
        return IncomingDeny.None;
    }
}
