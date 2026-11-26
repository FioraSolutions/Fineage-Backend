using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Fineage.Net.Contract
{
    /// <summary>
    /// Cancellation-safe inbound frame reader for one stream.
    /// </summary>
    /// <remarks>
    /// <see cref="NetFraming.ReadAsync"/> reads the 4-byte length prefix and then the body in two separate reads
    /// straight off the stream. Cancelling that second read throws away the prefix and any body bytes already
    /// consumed, so the next read starts inside the previous frame, parses payload bytes as a length and fails
    /// with <c>bad_frame_length</c>. Callers that abandon a read on a timer do exactly that: the load-test bot
    /// cancels its enter-settle read after 400 ms, and any frame that straddles the boundary is lost.
    ///
    /// This reader keeps every byte it has taken off the stream until a complete frame has been delivered, so a
    /// cancelled read resumes at the same frame boundary. One instance per stream; it is not thread-safe, which
    /// matches the single-reader-per-connection rule the zone and login sockets already follow.
    /// </remarks>
    public sealed class NetFrameReader
    {
        const int InitialBufferBytes = 8192;

        readonly Stream _stream;
        byte[] _buffer = new byte[InitialBufferBytes];
        int _start;
        int _end;

        public NetFrameReader(Stream stream) =>
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));

        /// <summary>Bytes buffered from the stream that no frame has consumed yet.</summary>
        public int Buffered => _end - _start;

        /// <summary>
        /// Returns the next complete frame, or <c>null</c> at end of stream. A cancelled read leaves the buffer
        /// intact, so calling again resumes exactly where the last frame ended.
        /// </summary>
        public async Task<PacketEnvelope?> ReadAsync(CancellationToken ct)
        {
            while (true)
            {
                if (TryTakeFrame(out PacketEnvelope envelope))
                    return envelope;

                EnsureRoom();
                int read = await _stream
                    .ReadAsync(_buffer.AsMemory(_end, _buffer.Length - _end), ct)
                    .ConfigureAwait(false);
                if (read == 0)
                    return null;
                _end += read;
            }
        }

        bool TryTakeFrame(out PacketEnvelope envelope)
        {
            envelope = null!;
            int available = _end - _start;
            if (available < 4)
                return false;

            uint total = BinaryPrimitives.ReadUInt32LittleEndian(_buffer.AsSpan(_start, 4));
            if (total < PacketEnvelope.HeaderSize || total > PacketEnvelope.MaxFrameBytes)
                throw new InvalidOperationException("bad_frame_length");

            int frameLength = 4 + (int)total;
            if (available < frameLength)
                return false;

            if (!NetFraming.TryDecode(_buffer.AsSpan(_start + 4, (int)total), out envelope, out string? error))
                throw new InvalidOperationException(error ?? "decode");

            _start += frameLength;
            if (_start == _end)
            {
                _start = 0;
                _end = 0;
            }

            return true;
        }

        /// <summary>Makes room for at least one more byte, compacting the consumed prefix before growing.</summary>
        void EnsureRoom()
        {
            if (_end < _buffer.Length)
                return;

            int buffered = _end - _start;
            if (_start > 0)
            {
                Buffer.BlockCopy(_buffer, _start, _buffer, 0, buffered);
                _start = 0;
                _end = buffered;
                if (_end < _buffer.Length)
                    return;
            }

            // A single frame is capped at 4 + MaxFrameBytes, so the buffer never grows past that.
            Array.Resize(ref _buffer, checked(_buffer.Length * 2));
        }
    }
}
