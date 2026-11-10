namespace Fineage.Protocol;

// Plain DTOs for the zone world and the lobby. They are no longer serialized: the Net
// PacketEnvelope codec in Fineage.Net.Contract owns the wire, so the old key numbers are gone
// with the V2 transport.

/// <summary>Opcode <see cref="MsgType.RequestBuyItem"/> (96). Counts are template counts, not object ids.</summary>
public sealed class RequestBuyItemMsg
{
    public int NpcObjectId { get; set; }
    public int ListId { get; set; }
    public ShopCountMsg[] Items { get; set; } = [];
}

/// <summary>One template id and count on <see cref="RequestBuyItemMsg"/>.</summary>
public sealed class ShopCountMsg
{
    public int ItemId { get; set; }
    public int Count { get; set; }
}

/// <summary>Opcode <see cref="MsgType.RequestSellItem"/> (97). Counts come off one object id.</summary>
public sealed class RequestSellItemMsg
{
    public int NpcObjectId { get; set; }
    public ShopObjectCountMsg[] Items { get; set; } = [];
}

/// <summary>One bag object and count on <see cref="RequestSellItemMsg"/>.</summary>
public sealed class ShopObjectCountMsg
{
    public int ObjectId { get; set; }
    public int Count { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.RequestMultiSellChoose"/> (98).
/// <see cref="EntryId"/> is the prepared id from <see cref="MultiSellEntryMsg"/>, not the template id.
/// </summary>
public sealed class RequestMultiSellChooseMsg
{
    public int NpcObjectId { get; set; }
    public int ListId { get; set; }
    public int EntryId { get; set; }
    public int Amount { get; set; }
}

/// <summary>Opcode <see cref="MsgType.RequestPreviewItem"/> (99). Wear stays stubbed.</summary>
public sealed class RequestPreviewItemMsg
{
    public int NpcObjectId { get; set; }
    public int ListId { get; set; }
    public int ItemId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.BuyList"/> (1095). <see cref="Price"/> is the taxed unit price.</summary>
public sealed class BuyListMsg
{
    public int NpcObjectId { get; set; }
    public int ListId { get; set; }
    public int BaseTaxPercent { get; set; }
    public int CastleTaxPercent { get; set; }
    public BuyListItemMsg[] Items { get; set; } = [];
}

/// <summary>One buylist row. <see cref="Count"/> below 0 means the stock is not limited.</summary>
public sealed class BuyListItemMsg
{
    public int ItemId { get; set; }
    public int Price { get; set; }
    public int Count { get; set; } = -1;
}

/// <summary>Opcode <see cref="MsgType.SellList"/> (1096). Only unequipped items the merchant will buy.</summary>
public sealed class SellListMsg
{
    public int NpcObjectId { get; set; }
    public SellListItemMsg[] Items { get; set; } = [];
}

/// <summary>One sellable stack. <see cref="Price"/> is the reference price divided by 2.</summary>
public sealed class SellListItemMsg
{
    public int ObjectId { get; set; }
    public int ItemId { get; set; }
    public int Count { get; set; }
    public int Price { get; set; }
    public int EnchantLevel { get; set; }
}

/// <summary>Opcode <see cref="MsgType.MultiSellList"/> (1097).</summary>
public sealed class MultiSellListMsg
{
    public int NpcObjectId { get; set; }
    public int ListId { get; set; }
    public bool MaintainEnchantment { get; set; }
    public bool InventoryOnly { get; set; }
    public int CastleTaxPercent { get; set; }
    public MultiSellEntryMsg[] Entries { get; set; } = [];
}

/// <summary>One prepared row. <see cref="EntryId"/> is what <see cref="RequestMultiSellChooseMsg"/> sends back.</summary>
public sealed class MultiSellEntryMsg
{
    public int EntryId { get; set; }
    public MultiSellPartMsg[] Ingredients { get; set; } = [];
    public MultiSellPartMsg[] Products { get; set; } = [];
}

/// <summary>One ingredient or product. <see cref="EnchantLevel"/> is 0 when the row does not keep enchant.</summary>
public sealed class MultiSellPartMsg
{
    public int ItemId { get; set; }
    public int Count { get; set; }
    public int EnchantLevel { get; set; }
}

/// <summary>Opcode <see cref="MsgType.ShopBalance"/> (1098). Castle tax is 0 when that castle has no owner.</summary>
public sealed class ShopBalanceMsg
{
    public int Adena { get; set; }
    public int Weight { get; set; }
    public int MaxWeight { get; set; }
    public int Slots { get; set; }
    public int MaxSlots { get; set; }
    public int BaseTaxPercent { get; set; }
    public int CastleTaxPercent { get; set; }
}
