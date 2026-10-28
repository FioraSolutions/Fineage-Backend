using System.Globalization;

namespace Fineage.Protocol.Telemetry;

public static class MetricsEnv
{
    public const string SummarySecondsVariable = "FINEAGE_METRICS_SUMMARY_SECONDS";
    public const string PortVariable = "FINEAGE_METRICS_PORT";
    public const string BindVariable = "FINEAGE_METRICS_BIND";

    public static string RolePortVariable(string role) =>
        role switch
        {
            "login" => "FINEAGE_LOGIN_METRICS_PORT",
            "zone" => "FINEAGE_ZONE_METRICS_PORT",
            _ => PortVariable
        };

    public static int ParseSummarySeconds(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 60;
        if (IsOff(text))
            return 0;
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds))
            return 60;
        return seconds <= 0 ? 0 : seconds;
    }

    public static bool TryParsePort(string? text, out int port, out string? error)
    {
        port = 0;
        error = null;
        if (string.IsNullOrWhiteSpace(text) || IsOff(text))
            return true;
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            || value is < 0 or > 65535)
        {
            error = "expected a TCP port, 0, or off";
            return false;
        }

        port = value;
        return true;
    }

    static bool IsOff(string text)
    {
        string trimmed = text.Trim();
        return trimmed.Equals("0", StringComparison.Ordinal)
            || trimmed.Equals("off", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("false", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("disable", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("disabled", StringComparison.OrdinalIgnoreCase);
    }
}
