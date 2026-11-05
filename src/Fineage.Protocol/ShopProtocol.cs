namespace Fineage.Protocol;

/// <summary>
/// Merchant buylists, sell, and multisell. Layout: <c>docs/shop-v2-protocol.md</c>.
/// Requests are 96–99, after NPC bypass 95. Replies are 1095–1098, after <see cref="NpcHtmlProtocol.Html"/> 1094.
/// System message ids are 180–189. Inventory changes stay on <see cref="MsgType.InventoryUpdate"/> 1015.
/// </summary>
public static class ShopProtocol
{
    public const ushort BuyRequest = 96;
    public const ushort SellRequest = 97;
    public const ushort MultiSellRequest = 98;
    public const ushort PreviewRequest = 99;
    public const ushort BuyList = 1095;
    public const ushort SellList = 1096;
    public const ushort MultiSellList = 1097;
    public const ushort Balance = 1098;

    public const int NotEnoughAdena = 180;
    public const int WeightLimit = 181;
    public const int InventoryFull = 182;
    public const int TransactionComplete = 183;
    public const int Quantity = 184;
    public const int PocketLimit = 185;
    public const int Ingredients = 186;
    public const int NotTradable = 187;
    public const int OutOfStock = 188;
    public const int WearUnavailable = 189;

    public const string NotEnoughAdenaText = "You do not have enough adena.";
    public const string WeightLimitText = "You have exceeded the weight limit.";
    public const string InventoryFullText = "Your inventory is full.";
    public const string TransactionCompleteText = "The transaction is complete.";
    public const string QuantityText = "You have exceeded the quantity that can be inputted.";
    public const string PocketLimitText = "You have exceeded your out-of-pocket adena limit.";
    public const string NotTradableText = "That item cannot be traded.";
    public const string OutOfStockText = "That item is out of stock.";
    public const string WearUnavailableText = "Wear is not available.";
    public const string NoListHtml = "<html><body>%npcname%:<br>I have no such list.</body></html>";
    public const string NotMerchantHtml = "<html><body>%npcname%:<br>I do not buy or sell that.</body></html>";
}
