namespace Fineage.Net.Contract
{
    public sealed class PCCafeCouponUseNetMsg
    {
        public string Code { get; set; } = "";
    }

    public sealed class PcCafePointInfoNetMsg
    {
        public int Points { get; set; }
        public int PointsToAdd { get; set; }
        public int PeriodType { get; set; }
        public int RemainTime { get; set; }
        public int PointType { get; set; }
        public int Time { get; set; }
    }
}
