using Fineage.Game.Shops;

namespace Fineage.Zone.Shops;

/// <summary>The merchant window a player has open. The next buy, sell, or multisell packet must match it.</summary>
public sealed class ShopSession
{
    public int NpcObjectId { get; init; }
    public int BuyListId { get; set; }
    public bool SellOpen { get; set; }
    public PreparedMultiSell? Multi { get; set; }
}
