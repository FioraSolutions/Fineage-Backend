namespace Fineage.Net.Contract
{
    /// <summary>
    /// Version agreement for PacketEnvelope 1.0 (see docs/adr/0001-custom-net-wire.md).
    /// Framing decodes any well-formed header; this type is what Login, Zone and the host
    /// use to accept or reject a peer.
    /// </summary>
    public static class NetProtocol
    {
        public const string MismatchCode = "protocol_mismatch";
        public const string MismatchMessage = "Protocol major does not match this server.";

        /// <summary>
        /// Frame-header agreement. Only <see cref="PacketEnvelope.ProtocolMajor"/> is a hard
        /// check; minor is advertised in Hello/HelloAck and is not a rejection criterion today.
        /// </summary>
        public static bool AcceptsFrame(PacketEnvelope envelope)
        {
            if (envelope is null)
                throw new System.ArgumentNullException(nameof(envelope));
            return envelope.Major == PacketEnvelope.ProtocolMajor;
        }

        /// <summary>
        /// Hello payload agreement and feature-flag intersection used by Login and Zone.
        /// On failure <paramref name="reject"/> is a <c>protocol_mismatch</c> and
        /// <paramref name="featureFlags"/> is 0; the caller must not set HelloCompleted.
        /// </summary>
        public static bool TryAcceptHello(HelloMsg hello, uint granted, out uint featureFlags, out RejectMsg? reject)
        {
            if (hello is null)
                throw new System.ArgumentNullException(nameof(hello));

            if (hello.Major != PacketEnvelope.ProtocolMajor)
            {
                featureFlags = 0;
                reject = new RejectMsg { Code = MismatchCode, Message = MismatchMessage };
                return false;
            }

            featureFlags = granted & (hello.FeatureFlags == 0 ? granted : hello.FeatureFlags);
            if (featureFlags == 0)
                featureFlags = granted;

            reject = null;
            return true;
        }
    }
}
