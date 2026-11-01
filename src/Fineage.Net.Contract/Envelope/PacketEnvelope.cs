namespace Fineage.Net.Contract
{
    /// <summary>Wire header for Fineage Net (MyL2 PacketEnvelope). Payload is NetCodec bytes.</summary>
    public sealed class PacketEnvelope
    {
        public const byte ProtocolMajor = 1;
        public const byte ProtocolMinor = 0;
        public const int HeaderSize = 14; // major+minor+opcode+flags+seq+payloadLen
        public const int MaxPayloadBytes = 60_000;
        public const int MaxFrameBytes = HeaderSize + MaxPayloadBytes;

        public byte Major { get; set; } = ProtocolMajor;
        public byte Minor { get; set; } = ProtocolMinor;
        public ushort Opcode { get; set; }
        public ushort Flags { get; set; }
        public uint RequestSequence { get; set; }
        public byte[] Payload { get; set; } = System.Array.Empty<byte>();
    }

    [System.Flags]
    public enum PacketFlags : ushort
    {
        None = 0,
        Reliable = 1 << 0,
        Compressed = 1 << 1,
    }
}
