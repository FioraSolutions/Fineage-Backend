using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Fineage.Net.Contract
{
    /// <summary>
    /// Frame = u32 LE totalLength (header+payload) + PacketEnvelope header + payload.
    /// Network thread only: decode/validate/enqueue — never mutate world state.
    /// </summary>
    public static class NetFraming
    {
        public static byte[] Encode(PacketEnvelope envelope)
        {
            var frame = new byte[EncodedFrameByteCount(envelope)];
            EncodeInto(envelope, frame);
            return frame;
        }

        /// <summary>4-byte length prefix + header + payload.</summary>
        public static int EncodedFrameByteCount(PacketEnvelope envelope) =>
            4 + PacketEnvelope.HeaderSize + envelope.Payload.Length;

        public static void EncodeInto(PacketEnvelope envelope, Span<byte> destination)
        {
            if (envelope.Payload.Length > PacketEnvelope.MaxPayloadBytes)
                throw new InvalidOperationException("payload too large");

            int total = PacketEnvelope.HeaderSize + envelope.Payload.Length;
            if (destination.Length < 4 + total)
                throw new ArgumentException("destination too small", nameof(destination));

            BinaryPrimitives.WriteUInt32LittleEndian(destination, (uint)total);
            int o = 4;
            destination[o++] = envelope.Major;
            destination[o++] = envelope.Minor;
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(o), envelope.Opcode);
            o += 2;
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(o), envelope.Flags);
            o += 2;
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(o), envelope.RequestSequence);
            o += 4;
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(o), (uint)envelope.Payload.Length);
            o += 4;
            if (envelope.Payload.Length > 0)
                envelope.Payload.CopyTo(destination.Slice(o));
        }

        public static bool TryDecode(ReadOnlySpan<byte> frameBody, out PacketEnvelope envelope, out string? error)
        {
            envelope = null!;
            ReadOnlySpan<byte> header = frameBody.Length >= PacketEnvelope.HeaderSize
                ? frameBody.Slice(0, PacketEnvelope.HeaderSize)
                : frameBody;
            if (!TryReadHeader(header, frameBody.Length, out byte major, out byte minor, out ushort opcode, out ushort flags, out uint sequence, out int payloadLength, out error))
                return false;

            byte[] payload = payloadLength == 0
                ? Array.Empty<byte>()
                : frameBody.Slice(PacketEnvelope.HeaderSize, payloadLength).ToArray();
            envelope = CreateEnvelope(major, minor, opcode, flags, sequence, payload);
            return true;
        }

        /// <summary>
        /// Pulls one frame from <paramref name="buffer"/>, which may hold a partial frame or several frames.
        /// </summary>
        /// <remarks>
        /// A partial frame returns false with <paramref name="error"/> null and <paramref name="consumed"/> at
        /// <paramref name="buffer"/>'s start. The caller advances with consumed at that start and examined at the
        /// buffer end, so the bytes stay for the next read. A complete frame copies its payload out (the pipe
        /// buffer is reused) and sets <paramref name="consumed"/> just after the frame. A length that can never
        /// form a frame returns false with <paramref name="error"/> set.
        /// </remarks>
        public static bool TryReadFrame(ReadOnlySequence<byte> buffer, out PacketEnvelope? envelope, out string? error, out SequencePosition consumed)
        {
            envelope = null;
            error = null;
            consumed = buffer.Start;
            if (buffer.Length < 4)
                return false;

            Span<byte> lenBytes = stackalloc byte[4];
            buffer.Slice(0, 4).CopyTo(lenBytes);
            uint total = BinaryPrimitives.ReadUInt32LittleEndian(lenBytes);
            if (total < PacketEnvelope.HeaderSize || total > PacketEnvelope.MaxFrameBytes)
            {
                error = "bad_frame_length";
                return false;
            }

            long frameLength = 4L + total;
            if (buffer.Length < frameLength)
                return false;

            if (!TryDecodeBody(buffer.Slice(4, total), out PacketEnvelope decoded, out error))
                return false;

            envelope = decoded;
            consumed = buffer.GetPosition(frameLength);
            return true;
        }

        static bool TryDecodeBody(ReadOnlySequence<byte> body, out PacketEnvelope envelope, out string? error)
        {
            if (body.IsSingleSegment)
                return TryDecode(body.FirstSpan, out envelope, out error);

            envelope = null!;
            if (body.Length < PacketEnvelope.HeaderSize)
            {
                error = "short_header";
                return false;
            }

            Span<byte> header = stackalloc byte[PacketEnvelope.HeaderSize];
            body.Slice(0, PacketEnvelope.HeaderSize).CopyTo(header);
            if (!TryReadHeader(header, body.Length, out byte major, out byte minor, out ushort opcode, out ushort flags, out uint sequence, out int payloadLength, out error))
                return false;

            byte[] payload = payloadLength == 0 ? Array.Empty<byte>() : new byte[payloadLength];
            if (payloadLength > 0)
                body.Slice(PacketEnvelope.HeaderSize, payloadLength).CopyTo(payload);
            envelope = CreateEnvelope(major, minor, opcode, flags, sequence, payload);
            return true;
        }

        static bool TryReadHeader(
            ReadOnlySpan<byte> header,
            long bodyLength,
            out byte major,
            out byte minor,
            out ushort opcode,
            out ushort flags,
            out uint sequence,
            out int payloadLength,
            out string? error)
        {
            major = 0;
            minor = 0;
            opcode = 0;
            flags = 0;
            sequence = 0;
            payloadLength = 0;
            error = null;
            if (header.Length < PacketEnvelope.HeaderSize || bodyLength < PacketEnvelope.HeaderSize)
            {
                error = "short_header";
                return false;
            }

            major = header[0];
            minor = header[1];
            opcode = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(2));
            flags = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(4));
            sequence = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(6));
            uint payloadLen = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(10));
            if (payloadLen > PacketEnvelope.MaxPayloadBytes)
            {
                error = "oversized";
                return false;
            }

            if (bodyLength != PacketEnvelope.HeaderSize + payloadLen)
            {
                error = "length_mismatch";
                return false;
            }

            payloadLength = (int)payloadLen;
            return true;
        }

        static PacketEnvelope CreateEnvelope(byte major, byte minor, ushort opcode, ushort flags, uint sequence, byte[] payload) =>
            new()
            {
                Major = major,
                Minor = minor,
                Opcode = opcode,
                Flags = flags,
                RequestSequence = sequence,
                Payload = payload
            };

        public static async Task<PacketEnvelope?> ReadAsync(Stream stream, CancellationToken ct)
        {
            var lenBuf = new byte[4];
            if (!await ReadExactAsync(stream, lenBuf, ct).ConfigureAwait(false))
                return null;

            uint total = BinaryPrimitives.ReadUInt32LittleEndian(lenBuf);
            if (total < PacketEnvelope.HeaderSize || total > PacketEnvelope.MaxFrameBytes)
                throw new InvalidOperationException("bad_frame_length");

            var body = new byte[total];
            if (!await ReadExactAsync(stream, body, ct).ConfigureAwait(false))
                return null;

            if (!TryDecode(body, out var envelope, out string? error))
                throw new InvalidOperationException(error ?? "decode");
            return envelope;
        }

        public static async Task WriteAsync(Stream stream, PacketEnvelope envelope, CancellationToken ct)
        {
            await WriteManyAsync(stream, [envelope], ct).ConfigureAwait(false);
        }

        public static async Task WriteManyAsync(Stream stream, IReadOnlyList<PacketEnvelope> envelopes, CancellationToken ct)
        {
            if (envelopes.Count == 0)
                return;
            if (envelopes.Count == 1)
            {
                byte[] frame = Encode(envelopes[0]);
                await stream.WriteAsync(frame, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
                return;
            }

            int totalBytes = 0;
            for (int i = 0; i < envelopes.Count; i++)
                totalBytes += EncodedFrameByteCount(envelopes[i]);

            byte[] rented = ArrayPool<byte>.Shared.Rent(totalBytes);
            try
            {
                int offset = 0;
                for (int i = 0; i < envelopes.Count; i++)
                {
                    int n = EncodedFrameByteCount(envelopes[i]);
                    EncodeInto(envelopes[i], rented.AsSpan(offset, n));
                    offset += n;
                }

                await stream.WriteAsync(rented.AsMemory(0, totalBytes), ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int n = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct).ConfigureAwait(false);
                if (n == 0)
                    return false;
                offset += n;
            }

            return true;
        }
    }
}
