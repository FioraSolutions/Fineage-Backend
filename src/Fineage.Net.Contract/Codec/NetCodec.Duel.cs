namespace Fineage.Net.Contract
{
    public static partial class NetCodec
    {
        public static byte[] EncodeDuelCommand(DuelCommandNetMsg m) => Write(w =>
        {
            w.WriteByte((byte)m.Kind);
            w.WriteString(m.TargetName ?? "");
            w.WriteByte(m.Party ? (byte)1 : (byte)0);
            w.WriteInt32(m.Response);
        });

        public static DuelCommandNetMsg DecodeDuelCommand(System.ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new DuelCommandNetMsg
            {
                Kind = (DuelCommandKind)r.ReadByte(),
                TargetName = r.ReadString(),
                Party = r.ReadByte() != 0,
                Response = r.ReadInt32()
            };
        }

        public static byte[] EncodeDuelEvent(DuelEventNetMsg m) => Write(w =>
        {
            w.WriteByte((byte)m.Kind);
            w.WriteByte(m.Party ? (byte)1 : (byte)0);
            w.WriteString(m.Name ?? "");
            w.WriteByte(m.Team);
        });

        public static DuelEventNetMsg DecodeDuelEvent(System.ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new DuelEventNetMsg
            {
                Kind = (DuelEventKind)r.ReadByte(),
                Party = r.ReadByte() != 0,
                Name = r.ReadString(),
                Team = r.ReadByte()
            };
        }

        public static byte[] EncodeDuelUserInfo(DuelUserInfoNetMsg m) => Write(w =>
        {
            w.WriteString(m.Name ?? "");
            w.WriteInt32(m.ObjectId);
            w.WriteInt32(m.ClassId);
            w.WriteInt32(m.Level);
            w.WriteInt32(m.Hp);
            w.WriteInt32(m.MaxHp);
            w.WriteInt32(m.Mp);
            w.WriteInt32(m.MaxMp);
            w.WriteInt32(m.Cp);
            w.WriteInt32(m.MaxCp);
        });

        public static DuelUserInfoNetMsg DecodeDuelUserInfo(System.ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new DuelUserInfoNetMsg
            {
                Name = r.ReadString(),
                ObjectId = r.ReadInt32(),
                ClassId = r.ReadInt32(),
                Level = r.ReadInt32(),
                Hp = r.ReadInt32(),
                MaxHp = r.ReadInt32(),
                Mp = r.ReadInt32(),
                MaxMp = r.ReadInt32(),
                Cp = r.ReadInt32(),
                MaxCp = r.ReadInt32()
            };
        }
    }
}
