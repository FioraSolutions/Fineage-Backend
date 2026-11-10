namespace Fineage.Protocol;

// Plain DTOs for the zone world and the lobby. They are no longer serialized: the Net
// PacketEnvelope codec in Fineage.Net.Contract owns the wire, so the old key numbers are gone
// with the V2 transport.

/// <summary>Opcode <see cref="MsgType.TradeRequest"/> (28).</summary>
public sealed class TradeRequestMsg
{
    public int TargetObjectId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.TradeAnswerRequest"/> (29).</summary>
public sealed class TradeAnswerRequestMsg
{
    public int RequestorObjectId { get; set; }
    public int Response { get; set; }
}

/// <summary>Opcode <see cref="MsgType.TradeAddItemRequest"/> (54).</summary>
public sealed class TradeAddItemRequestMsg
{
    public int InventoryObjectId { get; set; }
    public long Count { get; set; } = 1;
}

/// <summary>Opcode <see cref="MsgType.TradeDoneRequest"/> (55). Response 1 confirm, 0 cancel.</summary>
public sealed class TradeDoneRequestMsg
{
    public int Response { get; set; }
}

/// <summary>Opcode <see cref="MsgType.TradeAsk"/> (1073).</summary>
public sealed class TradeAskMsg
{
    public int RequestorObjectId { get; set; }
    public string RequestorName { get; set; } = "";
}

/// <summary>Opcode <see cref="MsgType.TradeRequestResult"/> (1074).</summary>
public sealed class TradeRequestResultMsg
{
    public int Response { get; set; }
}

/// <summary>Opcode <see cref="MsgType.TradeStart"/> (1075).</summary>
public sealed class TradeStartMsg
{
    public int PartnerObjectId { get; set; }
    public string PartnerName { get; set; } = "";
}

/// <summary>Opcode <see cref="MsgType.TradeOfferUpdate"/> (1076).</summary>
public sealed class TradeOfferUpdateMsg
{
    public int IsOwn { get; set; }
    public int InventoryObjectId { get; set; }
    public int ItemId { get; set; }
    public long Count { get; set; }
    public int EnchantLevel { get; set; }
}

/// <summary>Opcode <see cref="MsgType.TradeConfirmState"/> (1077).</summary>
public sealed class TradeConfirmStateMsg
{
    public int SelfConfirmed { get; set; }
    public int PartnerConfirmed { get; set; }
}

/// <summary>Opcode <see cref="MsgType.TradeFinished"/> (1078).</summary>
public sealed class TradeFinishedMsg
{
    public int Response { get; set; }
}
