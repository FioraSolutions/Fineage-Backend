using System;

namespace Fineage.Net.Contract;

public static partial class NetCodec
{
    public static byte[] EncodeWeddingEngageAsk(WeddingEngageAskNetMsg m) => Write(w =>
    {
        w.WriteString(m.RequestorName ?? "");
        w.WriteInt32(m.RequestorObjectId);
    });

    public static WeddingEngageAskNetMsg DecodeWeddingEngageAsk(ReadOnlySpan<byte> p)
    {
        var r = new Reader(p);
        return new WeddingEngageAskNetMsg { RequestorName = r.ReadString(), RequestorObjectId = r.ReadInt32() };
    }

    public static byte[] EncodeWeddingEngageAnswer(WeddingEngageAnswerNetMsg m) =>
        Write(w => w.WriteInt32(m.Response));

    public static WeddingEngageAnswerNetMsg DecodeWeddingEngageAnswer(ReadOnlySpan<byte> p) =>
        new() { Response = new Reader(p).ReadInt32() };
}