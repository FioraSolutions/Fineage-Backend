using System.Buffers.Binary;
using MessagePack;

namespace Fineage.Protocol.V2;

public static class V2Codec
{
    public const int HeaderSize = 4;
    public const int MaxFrame = ushort.MaxValue;

    public static byte[] Serialize<T>(T message) => MessagePackSerializer.Serialize(message);

    public static T Deserialize<T>(ReadOnlyMemory<byte> body) => MessagePackSerializer.Deserialize<T>(body);

    public static byte[] Frame<T>(MsgType type, T message) => Frame(type, Serialize(message));

    public static byte[] Frame(MsgType type, ReadOnlySpan<byte> body)
    {
        int total = HeaderSize + body.Length;
        if (total > MaxFrame)
            throw new InvalidOperationException($"V2 frame is {total} bytes; the uint16 length caps a frame at {MaxFrame}.");

        byte[] packet = new byte[total];
        BinaryPrimitives.WriteUInt16LittleEndian(packet, (ushort)total);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), (ushort)type);
        body.CopyTo(packet.AsSpan(HeaderSize));
        return packet;
    }
}
