namespace Fineage.Net.Contract
{
    /// <summary>
    /// Mobius Interlude extended packet ids folded into the Fineage <see cref="NetOpcode"/> ushort namespace.
    /// S→C: primary <c>0xFE</c> + sub; C→S: primary <c>0xD0</c> + sub (see <c>ExClientPackets</c>).
    /// </summary>
    public static class NetOpcodeMobius
    {
        public const ushort ExServerBase = 0xFE00;
        public const ushort ExClientBase = 0xD000;

        public static ushort ExServer(byte sub) => unchecked((ushort)(ExServerBase | sub));

        public static ushort ExClient(byte sub) => unchecked((ushort)(ExClientBase | sub));
    }
}