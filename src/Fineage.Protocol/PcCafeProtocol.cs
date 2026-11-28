namespace Fineage.Protocol;

/// <summary>Mobius PC Cafe points / coupon rules (Interlude).</summary>
public static class PcCafeProtocol
{
    /// <summary>Mobius <c>MaxPcCafePoints</c> default.</summary>
    public const int MaxPoints = 200_000;

    /// <summary>Mobius <c>AcquisitionPointsRetailLikePoints</c> default (per 5 min tick).</summary>
    public const int RetailLikePoints = 10;

    /// <summary>Mobius retail-like acquisition interval (ms).</summary>
    public const int RetailLikeIntervalMs = 300_000;

    /// <summary>ExPCCafePointInfo remainTime — Mobius always sends 42.</summary>
    public const int RemainTimeRetail = 42;
}
