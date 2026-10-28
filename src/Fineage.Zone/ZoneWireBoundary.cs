namespace Fineage.Zone;

/// <summary>Login-port opcodes that must not be sent to the zone TCP listener.</summary>
/// <remarks>V2 roster uses opcodes 5 and 6 (same numbers as legacy zone combat ids; zone rejects them here).</remarks>
static class ZoneWireBoundary
{
    public static bool IsLoginClientOpcode(ushort type) => type switch
    {
        5 or 6 or 7 or 10 or 11 => true,
        >= 20 and <= 23 => true,
        _ => false
    };
}
