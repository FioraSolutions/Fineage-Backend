using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fineage.Net.Contract;
using Fineage.Net.Host;
using Xunit;

namespace Fineage.Net.Tests;

public class FramingAndDedupTests
{
    [Fact]
    public void Envelope_roundtrips()
    {
        var payload = NetCodec.EncodeHello(new HelloMsg
        {
            ClientName = "unity",
            FeatureFlags = ProtocolCapabilities.FeatureSession
        });
        var envelope = new PacketEnvelope
        {
            Opcode = NetOpcode.Hello,
            RequestSequence = 7,
            Payload = payload
        };
        byte[] frame = NetFraming.Encode(envelope);
        uint total = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(0, 4));
        Assert.Equal((uint)(frame.Length - 4), total);
        Assert.True(NetFraming.TryDecode(frame.AsSpan(4), out var decoded, out string? error));
        Assert.Null(error);
        Assert.Equal(NetOpcode.Hello, decoded.Opcode);
        Assert.Equal(7u, decoded.RequestSequence);
        var hello = NetCodec.DecodeHello(decoded.Payload);
        Assert.Equal("unity", hello.ClientName);
    }

    [Fact]
    public void Deduper_rejects_duplicate_sequence()
    {
        var d = new CommandDeduper();
        Assert.True(d.TryAccept(1));
        Assert.False(d.TryAccept(1));
        Assert.True(d.TryAccept(2));
    }

    [Fact]
    public void Deduper_rejects_stale_replay_after_advance()
    {
        var d = new CommandDeduper();
        for (uint i = 1; i <= 3000; i++)
            Assert.True(d.TryAccept(i));

        // Old HashSet+prune accepted pruned ids; strict watermark must reject.
        Assert.False(d.TryAccept(100));
        Assert.False(d.TryAccept(3000));
        Assert.True(d.TryAccept(3001));
        Assert.True(d.TryAccept(0));
    }

    [Fact]
    public async Task WriteMany_roundtrips_multiple_frames()
    {
        var envelopes = new[]
        {
            new PacketEnvelope { Opcode = NetOpcode.Hello, RequestSequence = 1, Payload = [1, 2, 3] },
            new PacketEnvelope { Opcode = NetOpcode.HelloAck, RequestSequence = 2, Payload = [] },
            new PacketEnvelope { Opcode = NetOpcode.Reject, RequestSequence = 9, Flags = 3, Payload = [9] },
        };

        using var stream = new MemoryStream();
        await NetFraming.WriteManyAsync(stream, envelopes, CancellationToken.None);

        stream.Position = 0;
        for (int i = 0; i < envelopes.Length; i++)
        {
            var read = await NetFraming.ReadAsync(stream, CancellationToken.None);
            Assert.NotNull(read);
            Assert.Equal(envelopes[i].Opcode, read.Opcode);
            Assert.Equal(envelopes[i].RequestSequence, read.RequestSequence);
            Assert.Equal(envelopes[i].Flags, read.Flags);
            Assert.Equal(envelopes[i].Payload, read.Payload);
        }

        Assert.Null(await NetFraming.ReadAsync(stream, CancellationToken.None));
    }

    [Fact]
    public void Oversized_payload_rejected()
    {
        var envelope = new PacketEnvelope
        {
            Opcode = NetOpcode.Reject,
            Payload = new byte[PacketEnvelope.MaxPayloadBytes + 1]
        };
        Assert.Throws<InvalidOperationException>(() => NetFraming.Encode(envelope));
    }

    [Fact]
    public void Handoff_roundtrip()
    {
        var msg = new HandoffMsg
        {
            Account = "a",
            CharacterId = 42,
            CharacterName = "Hero",
            ClassId = 0,
            Token = "fa1.x.y",
            ZoneHost = "127.0.0.1",
            ZonePort = 9877,
            ExpiresUnixSeconds = 123
        };
        var decoded = NetCodec.DecodeHandoff(NetCodec.EncodeHandoff(msg));
        Assert.Equal(msg.Account, decoded.Account);
        Assert.Equal(msg.CharacterId, decoded.CharacterId);
        Assert.Equal(msg.ZonePort, decoded.ZonePort);
        Assert.Equal(msg.Token, decoded.Token);
    }
}
