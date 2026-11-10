namespace Fineage.Net.Contract
{
    public sealed class TradeRequestNetMsg
    {
        public int TargetObjectId { get; set; }
    }

    public sealed class TradeAnswerRequestNetMsg
    {
        public int RequestorObjectId { get; set; }
        public int Response { get; set; }
    }

    public sealed class TradeAddItemRequestNetMsg
    {
        public int InventoryObjectId { get; set; }
        public long Count { get; set; } = 1;
    }

    public sealed class TradeDoneRequestNetMsg
    {
        public int Response { get; set; }
    }

    public sealed class TradeAskNetMsg
    {
        public int RequestorObjectId { get; set; }
        public string RequestorName { get; set; } = "";
    }

    public sealed class TradeRequestResultNetMsg
    {
        public int Response { get; set; }
    }

    public sealed class TradeStartNetMsg
    {
        public int PartnerObjectId { get; set; }
        public string PartnerName { get; set; } = "";
    }

    public sealed class TradeOfferUpdateNetMsg
    {
        public int IsOwn { get; set; }
        public int InventoryObjectId { get; set; }
        public int ItemId { get; set; }
        public long Count { get; set; }
        public int EnchantLevel { get; set; }
    }

    public sealed class TradeConfirmStateNetMsg
    {
        public int SelfConfirmed { get; set; }
        public int PartnerConfirmed { get; set; }
    }

    public sealed class TradeFinishedNetMsg
    {
        public int Response { get; set; }
    }
}
