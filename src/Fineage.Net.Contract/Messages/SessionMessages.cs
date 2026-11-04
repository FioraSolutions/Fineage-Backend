namespace Fineage.Net.Contract
{
    public sealed class HelloMsg
    {
        public byte Major { get; set; } = PacketEnvelope.ProtocolMajor;
        public byte Minor { get; set; } = PacketEnvelope.ProtocolMinor;
        public string ClientName { get; set; } = "";
        public uint FeatureFlags { get; set; }
    }

    public sealed class HelloAckMsg
    {
        public byte Major { get; set; } = PacketEnvelope.ProtocolMajor;
        public byte Minor { get; set; } = PacketEnvelope.ProtocolMinor;
        public uint FeatureFlags { get; set; }
        public string ServerName { get; set; } = "fineage-net";
    }

    public sealed class RejectMsg
    {
        public string Code { get; set; } = "";
        public string Message { get; set; } = "";
    }
}
