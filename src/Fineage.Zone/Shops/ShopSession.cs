using Fineage.Game.Shops;

namespace Fineage.Zone.Shops;

/// <summary>The merchant window a player has open. The next buy, sell, wear, or multisell packet must match it.</summary>
public sealed class ShopSession
{
    public int NpcObjectId { get; init; }
    public int BuyListId { get; set; }
    public bool SellOpen { get; set; }
    /// <summary>Mobius Wear try-on window is open for <see cref="BuyListId"/>.</summary>
    public bool WearOpen { get; set; }
    /// <summary>When non-zero, clear the try-on overlay (Mobius <c>RemoveWearItemsTask</c>).</summary>
    public long WearPreviewClearAtMs { get; set; }
    public PreparedMultiSell? Multi { get; set; }
    /// <summary>Mobius <c>admin_buy</c>: no NPC, tax 0, buylist NPC allow-list skipped.</summary>
    public bool IsAdmin { get; init; }
}
