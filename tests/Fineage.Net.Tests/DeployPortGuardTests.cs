using Xunit;

namespace Fineage.Net.Tests;

/// <summary>
/// Guard for the deploy scripts. The client only speaks Net (9106 login / 9777 zone). The shell
/// scripts used to health-check the retired V2 ports, so <c>deploy</c> reported failure on a
/// perfectly healthy server: both processes were listening on the Net ports while the script probed
/// 2106/7777. Keep the constants and the probe in one place.
/// </summary>
public sealed class DeployPortGuardTests
{
    [Fact]
    public void Deploy_health_check_probes_the_Net_ports()
    {
        string root = RepoRoot();
        // #989: the host-side gate lives in release-switch.sh; deploy-to-host.sh only uploads and calls it.
        string gate = File.ReadAllText(Path.Combine(root, "deploy", "release-switch.sh"));

        Assert.Contains("FINEAGE_NET_LOGIN_PORT:-9106", gate, StringComparison.Ordinal);
        Assert.Contains("FINEAGE_NET_ZONE_PORT:-9777", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("2106", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("7777", gate, StringComparison.Ordinal);
    }

    [Fact]
    public void Deploy_waits_for_readiness_and_switches_releases_atomically()
    {
        string root = RepoRoot();
        string gate = File.ReadAllText(Path.Combine(root, "deploy", "release-switch.sh"));
        string deploy = File.ReadAllText(Path.Combine(root, "deploy", "deploy-to-host.sh"));

        Assert.Contains("/health/ready", gate, StringComparison.Ordinal);
        Assert.Contains("mv -T current.next current", gate, StringComparison.Ordinal);
        Assert.Contains("rolling back to", gate, StringComparison.Ordinal);
        Assert.Contains("release-switch.sh", deploy, StringComparison.Ordinal);
        Assert.DoesNotContain("rsync -a --delete \"${STAGING}", deploy, StringComparison.Ordinal);
    }

    [Fact]
    public void Deploy_restarts_systemd_units_on_the_live_host()
    {
        string root = RepoRoot();
        string deploy = File.ReadAllText(Path.Combine(root, "deploy", "deploy-to-host.sh"));
        string gate = File.ReadAllText(Path.Combine(root, "deploy", "release-switch.sh"));

        // The live host runs the servers under systemd; run-*.sh would race those units for the ports.
        Assert.Contains("FINEAGE_RESTART:-systemd", deploy, StringComparison.Ordinal);
        Assert.Contains("FINEAGE_RESTART:-systemd", gate, StringComparison.Ordinal);
        Assert.Contains("sudo systemctl restart", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("raspberrypi", deploy, StringComparison.Ordinal);
        Assert.DoesNotContain("/home/pi/", deploy, StringComparison.Ordinal);
        Assert.DoesNotContain("/home/pi/", gate, StringComparison.Ordinal);
    }

    [Fact]
    public void Deploy_workflow_publishes_the_host_architecture()
    {
        string root = RepoRoot();
        string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "deploy.yml"));

        // homeserver is x86-64 Debian; an arm64 publish cannot execute there.
        Assert.Contains("-r linux-x64", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("linux-arm64", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_scripts_start_clean_without_any_V2_port()
    {
        string root = RepoRoot();

        string login = File.ReadAllText(Path.Combine(root, "deploy", "run-login.sh"));
        Assert.Contains("NET_LOGIN_PORT=\"${FINEAGE_NET_LOGIN_PORT:-9106}\"", login, StringComparison.Ordinal);
        Assert.DoesNotContain("--port", login, StringComparison.Ordinal);
        Assert.DoesNotContain("FINEAGE_LEGACY", login, StringComparison.Ordinal);
        Assert.DoesNotContain("9206", login, StringComparison.Ordinal);

        string zone = File.ReadAllText(Path.Combine(root, "deploy", "run-zone.sh"));
        Assert.Contains("NET_ZONE_PORT=\"${FINEAGE_NET_ZONE_PORT:-9777}\"", zone, StringComparison.Ordinal);
        Assert.DoesNotContain("--port", zone, StringComparison.Ordinal);
        Assert.DoesNotContain("FINEAGE_LEGACY", zone, StringComparison.Ordinal);
        Assert.DoesNotContain("9877", zone, StringComparison.Ordinal);
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
