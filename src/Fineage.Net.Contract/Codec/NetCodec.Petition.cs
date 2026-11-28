namespace Fineage.Net.Contract
{
    public static partial class NetCodec
    {
        public static byte[] EncodePetitionRequest(PetitionRequestNetMsg m) => Write(w =>
        {
            w.WriteString(m.Content ?? "");
            w.WriteInt32(m.Type);
        });

        public static PetitionRequestNetMsg DecodePetitionRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new PetitionRequestNetMsg
            {
                Content = r.ReadString(),
                Type = r.ReadInt32()
            };
        }

        public static byte[] EncodePetitionCancel(PetitionCancelNetMsg _) => Write(_ => { });

        public static PetitionCancelNetMsg DecodePetitionCancel(ReadOnlySpan<byte> _) => new();
    }
}
