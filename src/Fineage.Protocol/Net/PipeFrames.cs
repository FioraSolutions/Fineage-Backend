using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using Fineage.Protocol.V2;

namespace Fineage.Protocol.Net;

public readonly record struct V2Frame(ushort Type, byte[] Body);

/// <summary>
/// Length-prefixed frame parsers over <see cref="PipeReader"/>.
/// V2: uint16 total (includes the 4-byte header) | uint16 msgType | body.
/// Interlude: uint16 total (includes the 2-byte header) | body.
/// </summary>
public static class PipeFrames
{
    public static async ValueTask<V2Frame?> ReadV2Async(PipeReader reader, CancellationToken ct)
    {
        while (true)
        {
            ReadResult result = await reader.ReadAsync(ct).ConfigureAwait(false);
            ReadOnlySequence<byte> buffer = result.Buffer;
            if (TryReadV2(ref buffer, out V2Frame frame))
            {
                reader.AdvanceTo(buffer.Start);
                return frame;
            }

            reader.AdvanceTo(buffer.Start, buffer.End);
            if (result.IsCompleted)
                return null;
        }
    }

    public static async ValueTask<byte[]?> ReadInterludeAsync(PipeReader reader, CancellationToken ct)
    {
        while (true)
        {
            ReadResult result = await reader.ReadAsync(ct).ConfigureAwait(false);
            ReadOnlySequence<byte> buffer = result.Buffer;
            if (TryReadInterlude(ref buffer, out byte[] body))
            {
                reader.AdvanceTo(buffer.Start);
                return body;
            }

            reader.AdvanceTo(buffer.Start, buffer.End);
            if (result.IsCompleted)
                return null;
        }
    }

    public static bool TryReadV2(ref ReadOnlySequence<byte> buffer, out V2Frame frame)
    {
        frame = default;
        if (buffer.Length < V2Codec.HeaderSize)
            return false;

        Span<byte> header = stackalloc byte[V2Codec.HeaderSize];
        buffer.Slice(0, V2Codec.HeaderSize).CopyTo(header);
        ushort total = BinaryPrimitives.ReadUInt16LittleEndian(header);
        if (total < V2Codec.HeaderSize)
            throw new InvalidDataException($"V2 length {total} is smaller than the header.");
        if (buffer.Length < total)
            return false;

        ushort type = BinaryPrimitives.ReadUInt16LittleEndian(header[2..]);
        byte[] body = buffer.Slice(V2Codec.HeaderSize, total - V2Codec.HeaderSize).ToArray();
        buffer = buffer.Slice(buffer.GetPosition(total));
        frame = new V2Frame(type, body);
        return true;
    }

    public static bool TryReadInterlude(ref ReadOnlySequence<byte> buffer, out byte[] body)
    {
        body = [];
        if (buffer.Length < 2)
            return false;

        Span<byte> header = stackalloc byte[2];
        buffer.Slice(0, 2).CopyTo(header);
        int total = header[0] | (header[1] << 8);
        if (total < 2)
            throw new InvalidDataException($"Interlude length {total} is smaller than the header.");
        if (buffer.Length < total)
            return false;

        body = buffer.Slice(2, total - 2).ToArray();
        buffer = buffer.Slice(buffer.GetPosition(total));
        return true;
    }

    public static byte[] FrameInterlude(ReadOnlySpan<byte> body)
    {
        int total = body.Length + 2;
        if (total > ushort.MaxValue)
            throw new InvalidOperationException($"Interlude frame is {total} bytes.");

        byte[] packet = new byte[total];
        packet[0] = (byte)total;
        packet[1] = (byte)(total >> 8);
        body.CopyTo(packet.AsSpan(2));
        return packet;
    }
}
