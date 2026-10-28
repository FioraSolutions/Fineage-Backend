using System.Net;

namespace Fineage.Login;

/// <summary>TCP/HTTP listen address. Default is every interface so a Pi playtest is reachable on the LAN.</summary>
public static class BindAddress
{
    public const string Default = "0.0.0.0";

    public static bool TryResolve(string? text, out IPAddress address, out string display)
    {
        string value = string.IsNullOrWhiteSpace(text) ? Default : text.Trim();
        if (value is "*" or "+" or "any")
            value = Default;
        if (!IPAddress.TryParse(value, out address!))
        {
            display = "";
            return false;
        }

        display = address.ToString();
        return true;
    }
}
