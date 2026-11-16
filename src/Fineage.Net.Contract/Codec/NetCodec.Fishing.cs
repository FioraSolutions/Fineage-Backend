namespace Fineage.Net.Contract
{
    public static partial class NetCodec
    {
        public static byte[] EncodeFishingEvent(FishingEventNetMsg m) => Write(w =>
        {
            w.WriteByte((byte)m.Kind);
            w.WriteInt32(m.FisherObjectId);
            w.WriteInt32(m.FishGroup);
            w.WriteInt32(m.X);
            w.WriteInt32(m.Y);
            w.WriteInt32(m.Z);
            w.WriteByte(m.NightLure ? (byte)1 : (byte)0);
            w.WriteInt32(m.Time);
            w.WriteInt32(m.Hp);
            w.WriteByte((byte)m.Mode);
            w.WriteByte((byte)m.LureType);
            w.WriteByte((byte)m.Deceptive);
            w.WriteByte((byte)m.GoodUse);
            w.WriteByte((byte)m.Anim);
            w.WriteInt32(m.Penalty);
            w.WriteByte(m.Win ? (byte)1 : (byte)0);
        });

        public static FishingEventNetMsg DecodeFishingEvent(System.ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new FishingEventNetMsg
            {
                Kind = r.ReadByte(),
                FisherObjectId = r.ReadInt32(),
                FishGroup = r.ReadInt32(),
                X = r.ReadInt32(),
                Y = r.ReadInt32(),
                Z = r.ReadInt32(),
                NightLure = r.ReadByte() != 0,
                Time = r.ReadInt32(),
                Hp = r.ReadInt32(),
                Mode = r.ReadByte(),
                LureType = r.ReadByte(),
                Deceptive = r.ReadByte(),
                GoodUse = r.ReadByte(),
                Anim = r.ReadByte(),
                Penalty = r.ReadInt32(),
                Win = r.ReadByte() != 0
            };
        }
    }
}
