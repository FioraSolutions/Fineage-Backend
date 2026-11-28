namespace Fineage.Protocol;

/// <summary>Opcode <see cref="MsgType.RequestPCCafeCouponUse"/> — Mobius <c>RequestPCCafeCouponUse</c>.</summary>
public sealed class PCCafeCouponUseMsg
{
    public string Code { get; init; } = "";
}

/// <summary>Opcode <see cref="MsgType.ExPCCafePointInfo"/> — Mobius <c>ExPCCafePointInfo</c>.</summary>
public sealed class PcCafePointInfoMsg
{
    public int Points { get; init; }
    public int PointsToAdd { get; init; }
    public int PeriodType { get; init; }
    public int RemainTime { get; init; } = PcCafeProtocol.RemainTimeRetail;
    public int PointType { get; init; }
    public int Time { get; init; }
}
