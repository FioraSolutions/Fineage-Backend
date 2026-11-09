using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Fineage.Net.Contract
{
    public static partial class NetCodec
    {
        public static byte[] EncodeTradeAsk(TradeAskNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.RequestorObjectId);
            w.WriteString(m.RequestorName ?? "");
        });
        public static TradeAskNetMsg DecodeTradeAsk(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new TradeAskNetMsg { RequestorObjectId = r.ReadInt32(), RequestorName = r.ReadString() };
        }

        public static byte[] EncodeTradeRequestResult(TradeRequestResultNetMsg m) => Write(w => w.WriteInt32(m.Response));
        public static TradeRequestResultNetMsg DecodeTradeRequestResult(ReadOnlySpan<byte> p) =>
            new() { Response = new Reader(p).ReadInt32() };

        public static byte[] EncodeTradeStart(TradeStartNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.PartnerObjectId);
            w.WriteString(m.PartnerName ?? "");
        });
        public static TradeStartNetMsg DecodeTradeStart(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new TradeStartNetMsg { PartnerObjectId = r.ReadInt32(), PartnerName = r.ReadString() };
        }

        public static byte[] EncodeTradeOfferUpdate(TradeOfferUpdateNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.IsOwn);
            w.WriteInt32(m.InventoryObjectId);
            w.WriteInt32(m.ItemId);
            w.WriteInt64(m.Count);
            w.WriteInt32(m.EnchantLevel);
        });
        public static TradeOfferUpdateNetMsg DecodeTradeOfferUpdate(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new TradeOfferUpdateNetMsg
            {
                IsOwn = r.ReadInt32(),
                InventoryObjectId = r.ReadInt32(),
                ItemId = r.ReadInt32(),
                Count = r.ReadInt64(),
                EnchantLevel = r.ReadInt32()
            };
        }

        public static byte[] EncodeTradeConfirmState(TradeConfirmStateNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.SelfConfirmed);
            w.WriteInt32(m.PartnerConfirmed);
        });
        public static TradeConfirmStateNetMsg DecodeTradeConfirmState(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new TradeConfirmStateNetMsg { SelfConfirmed = r.ReadInt32(), PartnerConfirmed = r.ReadInt32() };
        }

        public static byte[] EncodeTradeFinished(TradeFinishedNetMsg m) => Write(w => w.WriteInt32(m.Response));
        public static TradeFinishedNetMsg DecodeTradeFinished(ReadOnlySpan<byte> p) =>
            new() { Response = new Reader(p).ReadInt32() };
        public static byte[] EncodeTradeRequest(TradeRequestNetMsg m) => Write(w => w.WriteInt32(m.TargetObjectId));
        public static TradeRequestNetMsg DecodeTradeRequest(ReadOnlySpan<byte> p) =>
            new() { TargetObjectId = new Reader(p).ReadInt32() };

        public static byte[] EncodeTradeAnswerRequest(TradeAnswerRequestNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.RequestorObjectId);
            w.WriteInt32(m.Response);
        });
        public static TradeAnswerRequestNetMsg DecodeTradeAnswerRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new TradeAnswerRequestNetMsg { RequestorObjectId = r.ReadInt32(), Response = r.ReadInt32() };
        }

        public static byte[] EncodeTradeAddItemRequest(TradeAddItemRequestNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.InventoryObjectId);
            w.WriteInt64(m.Count);
        });
        public static TradeAddItemRequestNetMsg DecodeTradeAddItemRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new TradeAddItemRequestNetMsg { InventoryObjectId = r.ReadInt32(), Count = r.ReadInt64() };
        }

        public static byte[] EncodeTradeDoneRequest(TradeDoneRequestNetMsg m) => Write(w => w.WriteInt32(m.Response));
        public static TradeDoneRequestNetMsg DecodeTradeDoneRequest(ReadOnlySpan<byte> p) =>
            new() { Response = new Reader(p).ReadInt32() };
    }
}
