namespace Fineage.Zone;

/// <summary>Login-port opcodes that must not be sent to the zone TCP listener.</summary>
static class ZoneWireBoundary
{
    public static bool IsLoginClientOpcode(ushort type) => type switch
    {
        7 or 10 or 11 => true,
        >= 20 and <= 23 => true,
        _ => false
    };
}
