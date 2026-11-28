using System.Buffers;
using System.Buffers.Binary;
using Fineage.Net.Contract;
using Fineage.Net.Host;
using Xunit;

namespace Fineage.Net.Tests;

/// <summary>
/// Enforces the PacketEnvelope framing limits in docs/adr/0001-custom-net-wire.md.
/// </summary>
public sealed class EnvelopeFramingRejectionTests
{
    [Fact]
    public void Encode_rejects_a_payload_past_MaxPayloadBytes()
    {
        var envelope = new PacketEnvelope { Opcode = NetOpcode.Hello, Payload = new byte[PacketEnvelope.MaxPayloadBytes + 1] };
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => NetFraming.Encode(envelope));
        Assert.Equal("payload too large", ex.Message);
    }

    [Fact]
    public void Encode_accepts_a_payload_at_the_cap()
    {
        byte[] payload = new byte[PacketEnvelope.MaxPayloadBytes];
        payload[^1] = 7;
        byte[] frame = NetFraming.Encode(new PacketEnvelope { Opcode = NetOpcode.Hello, Payload = payload });
        Assert.True(NetFraming.TryReadFrame(new ReadOnlySequence<byte>(frame), out PacketEnvelope? decoded, out string? error, out _));
        Assert.Null(error);
        Assert.Equal(payload, decoded!.Payload);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(13)]
    public void Total_length_below_the_header_is_bad_frame_length(uint total)
    {
        byte[] buf = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, total);
        Assert.False(NetFraming.TryReadFrame(new ReadOnlySequence<byte>(buf), out _, out string? error, out _));
        Assert.Equal("bad_frame_length", error);
    }

    [Fact]
    public void Total_length_above_MaxFrameBytes_is_bad_frame_length()
    {
        byte[] buf = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, (uint)PacketEnvelope.MaxFrameBytes + 1);
        Assert.False(NetFraming.TryReadFrame(new ReadOnlySequence<byte>(buf), out _, out string? error, out _));
        Assert.Equal("bad_frame_length", error);
    }

    [Fact]
    public async Task ReadAsync_rejects_a_bad_frame_length()
    {
        byte[] buf = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, (uint)PacketEnvelope.MaxFrameBytes + 1);
        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NetFraming.ReadAsync(new MemoryStream(buf), CancellationToken.None));
        Assert.Equal("bad_frame_length", ex.Message);
    }

    [Fact]
    public void Header_payloadLen_past_the_cap_is_oversized()
    {
        // totalLength stays inside MaxFrameBytes so framing reaches the header check.
        int total = PacketEnvelope.MaxFrameBytes;
        byte[] frame = new byte[4 + total];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)total);
        frame[4] = PacketEnvelope.ProtocolMajor;
        frame[5] = PacketEnvelope.ProtocolMinor;
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4 + 10), (uint)(PacketEnvelope.MaxPayloadBytes + 1));
        Assert.False(NetFraming.TryReadFrame(new ReadOnlySequence<byte>(frame), out _, out string? error, out _));
        Assert.Equal("oversized", error);
    }

    [Fact]
    public void Header_payloadLen_that_does_not_match_the_prefix_is_length_mismatch()
    {
        byte[] payload = [1, 2, 3];
        byte[] frame = NetFraming.Encode(new PacketEnvelope { Opcode = NetOpcode.Hello, Payload = payload });
        // Prefix still says 14+3, but the header payloadLen claims 2.
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4 + 10), 2);
        Assert.False(NetFraming.TryReadFrame(new ReadOnlySequence<byte>(frame), out _, out string? error, out _));
        Assert.Equal("length_mismatch", error);
    }

    [Fact]
    public void TryDecode_rejects_a_short_header()
    {
        Assert.False(NetFraming.TryDecode(new byte[3], out _, out string? error));
        Assert.Equal("short_header", error);
    }

    [Fact]
    public async Task Inbound_pipe_rejects_an_oversized_frame()
    {
        int total = PacketEnvelope.MaxFrameBytes;
        byte[] frame = new byte[4 + total];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)total);
        frame[4] = PacketEnvelope.ProtocolMajor;
        frame[5] = PacketEnvelope.ProtocolMinor;
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4 + 10), (uint)(PacketEnvelope.MaxPayloadBytes + 1));

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NetInboundPipe.ReadFramesAsync(new MemoryStream(frame), (_, _) => ValueTask.CompletedTask, CancellationToken.None));
        Assert.Equal("oversized", ex.Message);
    }

    [Fact]
    public void Fuzzed_prefixes_do_not_hang_and_only_surface_defined_errors()
    {
        string?[] allowed = [null, "bad_frame_length", "oversized", "length_mismatch", "short_header"];
        var rng = new Random(994);
        for (int i = 0; i < 4000; i++)
        {
            int len = rng.Next(0, 64);
            byte[] blob = new byte[len];
            rng.NextBytes(blob);
            bool ok = NetFraming.TryReadFrame(new ReadOnlySequence<byte>(blob), out PacketEnvelope? envelope, out string? error, out _);
            if (ok)
            {
                Assert.NotNull(envelope);
                Assert.Null(error);
            }
            else if (error is not null)
            {
                Assert.Contains(error, allowed);
            }
            else
            {
                Assert.True(len < 4 || IncompletePrefix(blob), "a complete illegal frame must set an error");
            }
        }
    }

    [Fact]
    public void Fuzzed_complete_frames_are_accepted_or_rejected_with_a_defined_error()
    {
        string?[] allowed = ["bad_frame_length", "oversized", "length_mismatch", "short_header"];
        var rng = new Random(9941);
        for (int i = 0; i < 2000; i++)
        {
            int payloadHint = rng.Next(0, 48);
            byte[] frame = new byte[4 + PacketEnvelope.HeaderSize + payloadHint];
            rng.NextBytes(frame);
            // Force a totalLength that makes the buffer look complete, including illegal sizes.
            uint total = (uint)rng.Next(0, PacketEnvelope.HeaderSize + payloadHint + 8);
            if (rng.Next(4) == 0)
                total = (uint)(PacketEnvelope.MaxFrameBytes + 1 + rng.Next(4));
            BinaryPrimitives.WriteUInt32LittleEndian(frame, total);
            if (total <= PacketEnvelope.MaxFrameBytes && 4L + total <= frame.Length)
            {
                // trim to one frame
                frame = frame.AsSpan(0, (int)(4 + total)).ToArray();
            }

            bool ok = NetFraming.TryReadFrame(new ReadOnlySequence<byte>(frame), out PacketEnvelope? envelope, out string? error, out _);
            if (ok)
            {
                Assert.NotNull(envelope);
                Assert.Null(error);
                Assert.True(envelope!.Payload.Length <= PacketEnvelope.MaxPayloadBytes);
            }
            else if (error is not null)
            {
                Assert.Contains(error, allowed);
            }
        }
    }

    static bool IncompletePrefix(byte[] blob)
    {
        if (blob.Length < 4)
            return true;
        uint total = BinaryPrimitives.ReadUInt32LittleEndian(blob);
        return total >= PacketEnvelope.HeaderSize
            && total <= PacketEnvelope.MaxFrameBytes
            && blob.Length < 4L + total;
    }
}
