using Fineage.Protocol;
using Fineage.Zone.Trade;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class TradeRulesTests
{
    [Fact]
    public void Offer_item_rejects_missing_equipped_and_bad_counts()
    {
        Assert.Equal(InventoryProtocol.NotOwned, TradeRules.OfferItemError(false, false, true, true, 1, 0));
        Assert.Equal(TradeProtocol.Item, TradeRules.OfferItemError(true, true, true, true, 1, 5));
        Assert.Equal(TradeProtocol.Item, TradeRules.OfferItemError(true, false, false, true, 1, 5));
        Assert.Equal(TradeProtocol.Item, TradeRules.OfferItemError(true, false, true, true, 6, 5));
        Assert.Equal(TradeProtocol.Item, TradeRules.OfferItemError(true, false, true, false, 2, 2));
        Assert.Null(TradeRules.OfferItemError(true, false, true, true, 3, 5));
        Assert.Null(TradeRules.OfferItemError(true, false, true, false, 1, 1));
    }

    [Fact]
    public void New_offer_row_hits_max_rows()
    {
        Assert.Null(TradeRules.NewOfferRowError(TradeProtocol.MaxOfferRows - 1));
        Assert.Equal(TradeProtocol.Item, TradeRules.NewOfferRowError(TradeProtocol.MaxOfferRows));
    }

    [Fact]
    public void Incoming_deny_prefers_weight_then_slots()
    {
        Assert.Equal(
            TradeRules.IncomingDeny.Weight,
            TradeRules.IncomingDenyOf(currentWeight: 90, incomingWeight: 20, maxLoad: 100, currentItemCount: 0, incomingSlots: 1, maxSlots: 80));
        Assert.Equal(
            TradeRules.IncomingDeny.Slots,
            TradeRules.IncomingDenyOf(currentWeight: 0, incomingWeight: 1, maxLoad: 100, currentItemCount: 79, incomingSlots: 2, maxSlots: 80));
        Assert.Equal(
            TradeRules.IncomingDeny.None,
            TradeRules.IncomingDenyOf(currentWeight: 0, incomingWeight: 1, maxLoad: 100, currentItemCount: 1, incomingSlots: 1, maxSlots: 80));
    }
}
