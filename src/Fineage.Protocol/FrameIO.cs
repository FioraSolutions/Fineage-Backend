using System.Buffers.Binary;

namespace Fineage.Protocol;

/// <summary>
/// Fineage v2 frame, same layout as ZoneServer <c>FrameIO</c>:
/// uint16 LE totalLength (includes this 4-byte header) | uint16 LE msgType | MessagePack body.
/// </summary>
public static class FrameIO
{
    public const int HeaderSize = 4;

    public static async Task SendAsync(Stream stream, ushort msgType, byte[] body, CancellationToken ct = default)
    {
        int total = HeaderSize + body.Length;
        if (total > ushort.MaxValue)
            throw new InvalidOperationException($"frame too large: {total}");

        byte[] packet = new byte[total];
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), (ushort)total);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), msgType);
        Buffer.BlockCopy(body, 0, packet, HeaderSize, body.Length);
        await stream.WriteAsync(packet, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public static async Task<(ushort Type, byte[] Body)?> ReadAsync(Stream stream, CancellationToken ct = default)
    {
        byte[] header = new byte[HeaderSize];
        if (!await ReadExactAsync(stream, header, ct).ConfigureAwait(false))
            return null;

        ushort total = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(0, 2));
        ushort type = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(2, 2));
        if (total < HeaderSize)
            return null;

        int bodyLen = total - HeaderSize;
        byte[] body = bodyLen == 0 ? [] : new byte[bodyLen];
        if (bodyLen > 0 && !await ReadExactAsync(stream, body, ct).ConfigureAwait(false))
            return null;

        return (type, body);
    }

    static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int off = 0;
        while (off < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(off, buffer.Length - off), ct).ConfigureAwait(false);
            if (n == 0)
                return false;
            off += n;
        }

        return true;
    }
}
