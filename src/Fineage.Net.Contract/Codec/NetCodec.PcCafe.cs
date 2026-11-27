namespace Fineage.Net.Contract
{
    public static partial class NetCodec
    {
        public static byte[] EncodePCCafeCouponUse(PCCafeCouponUseNetMsg m) => Write(w => w.WriteString(m.Code));

        public static PCCafeCouponUseNetMsg DecodePCCafeCouponUse(ReadOnlySpan<byte> p) =>
            new() { Code = new Reader(p).ReadString() };

        public static byte[] EncodePcCafePointInfo(PcCafePointInfoNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.Points);
            w.WriteInt32(m.PointsToAdd);
            w.WriteByte((byte)m.PeriodType);
            w.WriteInt32(m.RemainTime);
            w.WriteByte((byte)m.PointType);
            w.WriteInt32(m.Time * 3);
        });

        public static PcCafePointInfoNetMsg DecodePcCafePointInfo(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new PcCafePointInfoNetMsg
            {
                Points = r.ReadInt32(),
                PointsToAdd = r.ReadInt32(),
                PeriodType = r.ReadByte(),
                RemainTime = r.ReadInt32(),
                PointType = r.ReadByte(),
                Time = r.ReadInt32() / 3
            };
        }
    }
}
