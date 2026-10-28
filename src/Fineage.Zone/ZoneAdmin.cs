namespace Fineage.Zone;

/// <summary>
/// <c>FINEAGE_ZONE_ADMIN</c> enables debug opcodes such as <see cref="Fineage.Protocol.MsgType.GiveItem"/>.
/// Unset, empty, or any other value leaves them off.
/// </summary>
public static class ZoneAdmin
{
    public const string EnvVar = "FINEAGE_ZONE_ADMIN";

    public static bool Enabled => IsEnabled(Environment.GetEnvironmentVariable(EnvVar));

    public static bool IsEnabled(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var token = text.Trim();
        return token == "1"
            || token.Equals("true", StringComparison.OrdinalIgnoreCase)
            || token.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}
