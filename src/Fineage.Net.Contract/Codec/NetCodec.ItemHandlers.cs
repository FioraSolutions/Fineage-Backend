namespace Fineage.Net.Contract
{
    public static partial class NetCodec
    {
        public static byte[] EncodeDice(DiceNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.ObjectId);
            w.WriteInt32(m.ItemId);
            w.WriteInt32(m.Number);
            w.WriteInt32(m.X);
            w.WriteInt32(m.Y);
            w.WriteInt32(m.Z);
        });

        public static DiceNetMsg DecodeDice(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new DiceNetMsg
            {
                ObjectId = r.ReadInt32(),
                ItemId = r.ReadInt32(),
                Number = r.ReadInt32(),
                X = r.ReadInt32(),
                Y = r.ReadInt32(),
                Z = r.ReadInt32()
            };
        }

        public static byte[] EncodeShowMiniMap(ShowMiniMapNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.MapId);
            w.WriteInt32(m.SevenSignsPeriod);
        });

        public static ShowMiniMapNetMsg DecodeShowMiniMap(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new ShowMiniMapNetMsg { MapId = r.ReadInt32(), SevenSignsPeriod = r.ReadInt32() };
        }

        public static byte[] EncodeShowCalculator(ShowCalculatorNetMsg m) => Write(w => w.WriteInt32(m.ItemId));

        public static ShowCalculatorNetMsg DecodeShowCalculator(ReadOnlySpan<byte> p) =>
            new() { ItemId = new Reader(p).ReadInt32() };

        public static byte[] EncodeShowXMasSeal(ShowXMasSealNetMsg m) => Write(w => w.WriteInt32(m.ItemId));

        public static ShowXMasSealNetMsg DecodeShowXMasSeal(ReadOnlySpan<byte> p) =>
            new() { ItemId = new Reader(p).ReadInt32() };
    }
}
