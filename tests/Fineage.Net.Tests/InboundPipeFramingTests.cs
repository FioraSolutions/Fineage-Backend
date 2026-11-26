using System.Buffers;
using Fineage.Net.Contract;
using Fineage.Net.Host;
using Xunit;
using Xunit.Abstractions;

namespace Fineage.Net.Tests;

public class InboundPipeFramingTests
{
    readonly ITestOutputHelper _output;

    public InboundPipeFramingTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Partial_header_and_body_do_not_consume_the_buffer()
    {
        byte[] frame = Frame(opcode: 9, sequence: 3, payload: [1, 2, 3, 4]);
        var head = new ReadOnlySequence<byte>(frame.AsMemory(0, 3));
        Assert.False(NetFraming.TryReadFrame(head, out _, out string? error, out _));
        Assert.Null(error);

        var shortBody = new ReadOnlySequence<byte>(frame.AsMemory(0, 10));
        Assert.False(NetFraming.TryReadFrame(shortBody, out _, out error, out _));
        Assert.Null(error);
    }

    [Fact]
    public void Split_segments_roundtrip_and_the_payload_outlives_the_buffer()
    {
        byte[] payload = [9, 8, 7, 6, 5];
        byte[] frame = Frame(opcode: NetOpcode.Hello, sequence: 11, payload);
        ReadOnlySequence<byte> split = Split(frame, 2, 9);
        Assert.True(NetFraming.TryReadFrame(split, out PacketEnvelope? envelope, out string? error, out SequencePosition consumed));
        Assert.Null(error);
        Assert.Equal(split.End, consumed);
        Assert.Equal(payload, envelope!.Payload);

        frame[4 + PacketEnvelope.HeaderSize] = 0;
        Assert.Equal(9, envelope.Payload[0]);
    }

    [Fact]
    public void Several_frames_in_one_buffer_are_read_in_order()
    {
        byte[] first = Frame(1, 1, [1]);
        byte[] second = Frame(2, 2, []);
        byte[] third = Frame(3, 3, [3, 3]);
        byte[] all = first.Concat(second).Concat(third).ToArray();
        var buffer = new ReadOnlySequence<byte>(all);

        Assert.True(NetFraming.TryReadFrame(buffer, out PacketEnvelope? a, out _, out SequencePosition end));
        buffer = buffer.Slice(end);
        Assert.True(NetFraming.TryReadFrame(buffer, out PacketEnvelope? b, out _, out end));
        buffer = buffer.Slice(end);
        Assert.True(NetFraming.TryReadFrame(buffer, out PacketEnvelope? c, out _, out end));
        buffer = buffer.Slice(end);

        Assert.Equal(1, a!.Payload[0]);
        Assert.Empty(b!.Payload);
        Assert.Equal(new byte[] { 3, 3 }, c!.Payload);
        Assert.Equal(0, buffer.Length);
    }

    [Fact]
    public void A_length_that_cannot_be_a_frame_is_an_error()
    {
        byte[] bad = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bad, 3);
        Assert.False(NetFraming.TryReadFrame(new ReadOnlySequence<byte>(bad), out _, out string? error, out _));
        Assert.Equal("bad_frame_length", error);
    }

    [Fact]
    public async Task One_byte_reads_still_deliver_every_frame()
    {
        byte[] first = Frame(4, 4, [4, 5, 6]);
        byte[] second = Frame(5, 5, [7]);
        var stream = new MemoryStream(first.Concat(second).ToArray());
        var frames = new List<PacketEnvelope>();

        await NetInboundPipe.ReadFramesAsync(
            stream,
            (envelope, _) =>
            {
                frames.Add(envelope);
                return ValueTask.CompletedTask;
            },
            CancellationToken.None,
            bufferSize: 1,
            minimumReadSize: 1);

        Assert.Equal(2, frames.Count);
        Assert.Equal(new byte[] { 4, 5, 6 }, frames[0].Payload);
        Assert.Equal(7, frames[1].Payload[0]);
        Assert.Equal(5u, frames[1].RequestSequence);
    }

    [Fact]
    public async Task Coalesced_frames_arrive_from_one_read()
    {
        byte[] blob = Frame(1, 1, [1]).Concat(Frame(2, 2, [2, 2])).Concat(Frame(3, 3, [3])).ToArray();
        var frames = new List<PacketEnvelope>();
        await NetInboundPipe.ReadFramesAsync(new MemoryStream(blob), (envelope, _) =>
        {
            frames.Add(envelope);
            return ValueTask.CompletedTask;
        }, CancellationToken.None);

        Assert.Equal(3, frames.Count);
        Assert.Equal(new ushort[] { 1, 2, 3 }, frames.Select(f => f.Opcode).ToArray());
    }

    [Fact]
    public async Task A_bad_length_fails_the_read()
    {
        byte[] bad = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bad, 3);
        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NetInboundPipe.ReadFramesAsync(new MemoryStream(bad), (_, _) => ValueTask.CompletedTask, CancellationToken.None));
        Assert.Equal("bad_frame_length", ex.Message);
    }

    [Fact]
    public async Task A_partial_frame_at_end_of_stream_delivers_nothing()
    {
        byte[] frame = Frame(1, 1, [1, 2, 3, 4]);
        var frames = new List<PacketEnvelope>();
        await NetInboundPipe.ReadFramesAsync(new MemoryStream(frame, 0, 6), (envelope, _) =>
        {
            frames.Add(envelope);
            return ValueTask.CompletedTask;
        }, CancellationToken.None);

        Assert.Empty(frames);
    }

    [Fact]
    public async Task ReadAsync_still_assembles_a_frame_from_one_byte_reads()
    {
        byte[] payload = [1, 2, 3];
        byte[] frame = Frame(NetOpcode.HelloAck, 8, payload);
        var envelope = await NetFraming.ReadAsync(new OneByteStream(frame), CancellationToken.None);
        Assert.NotNull(envelope);
        Assert.Equal(NetOpcode.HelloAck, envelope.Opcode);
        Assert.Equal(payload, envelope.Payload);
    }

    [Fact]
    public async Task Pipe_reader_allocates_fewer_bytes_per_frame_than_ReadAsync()
    {
        const int count = 2000;
        byte[] payload = new byte[64];
        for (int i = 0; i < payload.Length; i++)
            payload[i] = (byte)i;
        byte[] blob = new byte[count * (4 + PacketEnvelope.HeaderSize + payload.Length)];
        int offset = 0;
        for (int i = 0; i < count; i++)
        {
            byte[] frame = Frame((ushort)(i + 1), (uint)(i + 1), payload);
            frame.CopyTo(blob, offset);
            offset += frame.Length;
        }

        await DrainReadAsync(blob);
        await DrainPipe(blob);

        long before = GC.GetAllocatedBytesForCurrentThread();
        int read = await DrainReadAsync(blob);
        long readAsyncBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        before = GC.GetAllocatedBytesForCurrentThread();
        int piped = await DrainPipe(blob);
        long pipeBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(count, read);
        Assert.Equal(count, piped);
        double perRead = readAsyncBytes / (double)count;
        double perPipe = pipeBytes / (double)count;
        _output.WriteLine($"ReadAsync {perRead:0.#} B/frame ({readAsyncBytes} B), pipe {perPipe:0.#} B/frame ({pipeBytes} B), n={count}, payload={payload.Length}");
        Assert.True(perPipe < perRead, $"pipe {perPipe:0.#} B/frame was not below ReadAsync {perRead:0.#} B/frame");
    }

    static async Task<int> DrainReadAsync(byte[] blob)
    {
        var stream = new MemoryStream(blob);
        int n = 0;
        while (await NetFraming.ReadAsync(stream, CancellationToken.None) != null)
            n++;
        return n;
    }

    static async Task<int> DrainPipe(byte[] blob)
    {
        int n = 0;
        await NetInboundPipe.ReadFramesAsync(new MemoryStream(blob), (_, _) =>
        {
            n++;
            return ValueTask.CompletedTask;
        }, CancellationToken.None);
        return n;
    }

    [Fact]
    public async Task A_cancelled_read_does_not_lose_the_bytes_it_already_took()
    {
        // The load-test bot abandons its enter-settle read after 400 ms. NetFraming.ReadAsync reads the 4-byte
        // length and then the body as two separate stream reads, so cancelling in between throws the prefix away
        // and the next read starts inside the frame: it parses payload bytes as a length and dies with
        // "bad_frame_length". That is the disconnect two bots hit in the 50-bot smoke. The reader must keep the
        // prefix and resume at the same frame boundary.
        byte[] first = Frame(NetOpcode.HelloAck, 1, [1, 2, 3, 4]);
        byte[] second = Frame(NetOpcode.EntitySpawn, 2, [5, 6, 7, 8]);
        var stream = new BlockingAfterPrefixStream(first, second);
        var reader = new NetFrameReader(stream);

        using (var cts = new CancellationTokenSource())
        {
            cts.CancelAfter(TimeSpan.FromMilliseconds(100));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadAsync(cts.Token));
        }

        // The prefix is still buffered, so the next read completes the same frame instead of misparsing it.
        Assert.Equal(4, reader.Buffered);
        PacketEnvelope? resumed = await reader.ReadAsync(CancellationToken.None);
        Assert.NotNull(resumed);
        Assert.Equal(NetOpcode.HelloAck, resumed!.Opcode);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, resumed.Payload);

        PacketEnvelope? next = await reader.ReadAsync(CancellationToken.None);
        Assert.NotNull(next);
        Assert.Equal(NetOpcode.EntitySpawn, next!.Opcode);
        Assert.Equal(new byte[] { 5, 6, 7, 8 }, next.Payload);
    }

    [Fact]
    public async Task A_cancelled_read_never_parses_payload_bytes_as_a_length()
    {
        // Same fault, stated as the failure the smoke actually reported: resume must not throw bad_frame_length.
        byte[] frame = Frame(NetOpcode.HelloAck, 1, [0xFF, 0xFF, 0xFF, 0xFF]);
        var reader = new NetFrameReader(new BlockingAfterPrefixStream(frame));

        using (var cts = new CancellationTokenSource())
        {
            cts.CancelAfter(TimeSpan.FromMilliseconds(100));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadAsync(cts.Token));
        }

        PacketEnvelope? envelope = await reader.ReadAsync(CancellationToken.None);
        Assert.Equal(NetOpcode.HelloAck, envelope!.Opcode);
        Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, envelope.Payload);
    }


    static byte[] Frame(ushort opcode, uint sequence, byte[] payload) =>
        NetFraming.Encode(new PacketEnvelope
        {
            Opcode = opcode,
            RequestSequence = sequence,
            Payload = payload
        });

    static ReadOnlySequence<byte> Split(byte[] data, int first, int second)
    {
        var head = new Chunk(data.AsMemory(0, first));
        Chunk mid = head.Append(data.AsMemory(first, second - first));
        Chunk tail = mid.Append(data.AsMemory(second));
        return new ReadOnlySequence<byte>(head, 0, tail, tail.Memory.Length);
    }

    sealed class Chunk : ReadOnlySequenceSegment<byte>
    {
        public Chunk(ReadOnlyMemory<byte> memory) => Memory = memory;

        public Chunk Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Chunk(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }

    /// <summary>
    /// Hands out the 4-byte length prefix, then blocks the body read until cancelled. Reproduces a frame that
    /// straddles the caller's read timeout. The block happens once; later reads return the buffered frame.
    /// </summary>
    sealed class BlockingAfterPrefixStream : Stream
    {
        readonly byte[] _data;
        int _position;
        bool _blocked;

        public BlockingAfterPrefixStream(params byte[][] frames)
        {
            _data = frames.SelectMany(f => f).ToArray();
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position >= 4 && !_blocked)
            {
                _blocked = true;
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }

            int take = Math.Min(Math.Min(4, _data.Length - _position), buffer.Length);
            if (take <= 0)
                return 0;
            _data.AsSpan(_position, take).CopyTo(buffer.Span);
            _position += take;
            return take;
        }
    }

    sealed class OneByteStream : MemoryStream
    {
        public OneByteStream(byte[] data) : base(data) { }

        public override int Read(byte[] buffer, int offset, int count) =>
            base.Read(buffer, offset, count > 1 ? 1 : count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
            base.ReadAsync(buffer.Length > 1 ? buffer.Slice(0, 1) : buffer, cancellationToken);
    }
}
