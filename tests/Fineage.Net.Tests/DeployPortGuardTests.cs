using Xunit;

namespace Fineage.Net.Tests;

/// <summary>
/// Guard for the deploy scripts. The client only speaks Net (9106 login / 9777 zone). The shell
/// scripts used to health-check the retired V2 ports, so <c>deploy-pi</c> reported failure on a
/// perfectly healthy server: both processes were listening on the Net ports while the script probed
/// 2106/7777. Keep the constants and the probe in one place.
/// </summary>
public sealed class DeployPortGuardTests
{
    [Fact]
    public void Deploy_health_check_probes_the_Net_ports()
    {
        string root = RepoRoot();
        string deploy = File.ReadAllText(Path.Combine(root, "deploy", "deploy-to-pi.sh"));

        Assert.Contains("FINEAGE_NET_LOGIN_PORT:-9106", deploy, StringComparison.Ordinal);
        Assert.Contains("FINEAGE_NET_ZONE_PORT:-9777", deploy, StringComparison.Ordinal);
        Assert.DoesNotContain("for port in 2106 7777 5080", deploy, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_scripts_keep_the_legacy_V2_port_off_the_Net_port()
    {
        string root = RepoRoot();

        string login = File.ReadAllText(Path.Combine(root, "deploy", "run-login.sh"));
        Assert.Contains("FINEAGE_LEGACY_V2_ENABLE", login, StringComparison.Ordinal);
        Assert.Contains("NET_LOGIN_PORT=\"${FINEAGE_NET_LOGIN_PORT:-9106}\"", login, StringComparison.Ordinal);
        Assert.Contains("if [[ \"$LOGIN_PORT\" == \"$NET_LOGIN_PORT\" ]]", login, StringComparison.Ordinal);

        string zone = File.ReadAllText(Path.Combine(root, "deploy", "run-zone.sh"));
        Assert.Contains("FINEAGE_LEGACY_V2_ENABLE", zone, StringComparison.Ordinal);
        Assert.Contains("NET_ZONE_PORT=\"${FINEAGE_NET_ZONE_PORT:-9777}\"", zone, StringComparison.Ordinal);
        Assert.Contains("if [[ \"$ZONE_PORT\" == \"$NET_ZONE_PORT\" ]]", zone, StringComparison.Ordinal);
    }

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "scripts", "run-local-net.sh")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
