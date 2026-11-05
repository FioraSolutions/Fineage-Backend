namespace Fineage.Net.Contract
{
    /// <summary>Negotiated in Hello / HelloAck.</summary>
    public sealed class ProtocolCapabilities
    {
        public byte Major { get; set; } = PacketEnvelope.ProtocolMajor;
        public byte Minor { get; set; } = PacketEnvelope.ProtocolMinor;
        public string ClientName { get; set; } = "";
        public uint FeatureFlags { get; set; }

        public const uint FeatureSession = 1 << 0;
        public const uint FeatureLobby = 1 << 1;
        public const uint FeatureWorldPresence = 1 << 2;
    }
}
