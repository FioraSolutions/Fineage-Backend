namespace Fineage.Login;

/// <summary>Zone-port gameplay opcodes that must not be sent to the login TCP listener.</summary>
static class LoginWireBoundary
{
    public static bool IsZoneClientOpcode(ushort type) => type switch
    {
        3 or 4 or 5 or 6 or 8 => true,
        _ => false
    };
}
