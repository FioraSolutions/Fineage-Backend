using Xunit;

namespace Fineage.Net.Tests;

/// <summary>
/// Guard for the deploy scripts. The client only speaks Net (9106 login / 9777 zone). Deploy now
/// verifies real readiness instead of probing open ports, and switches immutable releases through
/// current/previous symlinks; these assertions keep that contract in one place.
/// </summary>
public sealed class DeployPortGuardTests
{
    [Fact]
    public void Deploy_checks_readiness_and_keeps_the_Net_ports_for_the_smoke()
    {
        string root = RepoRoot();
        string deploy = File.ReadAllText(Path.Combine(root, "deploy", "deploy-to-host.sh"));
        string manager = File.ReadAllText(Path.Combine(root, "deploy", "release-manager.sh"));

        Assert.Contains("release-manager.sh", deploy, StringComparison.Ordinal);
        Assert.Contains("/readyz", manager, StringComparison.Ordinal);
        Assert.Contains("FINEAGE_NET_LOGIN_PORT:-9106", manager, StringComparison.Ordinal);
        Assert.Contains("FINEAGE_NET_ZONE_PORT:-9777", manager, StringComparison.Ordinal);
        Assert.DoesNotContain("for port in 2106 7777 5080", deploy, StringComparison.Ordinal);
        Assert.DoesNotContain("for port in 2106 7777 5080", manager, StringComparison.Ordinal);
    }

    [Fact]
    public void Deploy_runs_migration_preflight_and_switches_release_pointer()
    {
        string root = RepoRoot();
        string manager = File.ReadAllText(Path.Combine(root, "deploy", "release-manager.sh"));

        Assert.Contains("migrate --plan", manager, StringComparison.Ordinal);
        Assert.Contains("releases/", manager, StringComparison.Ordinal);
        Assert.Contains("current", manager, StringComparison.Ordinal);
        Assert.Contains("previous", manager, StringComparison.Ordinal);
        Assert.Contains("ROLLBACK OK", manager, StringComparison.Ordinal);
        Assert.Contains("ROLLBACK FAILED", manager, StringComparison.Ordinal);
    }

    [Fact]
    public void Deploy_restarts_systemd_units_on_the_live_host()
    {
        string root = RepoRoot();
        string manager = File.ReadAllText(Path.Combine(root, "deploy", "release-manager.sh"));

        // The live host runs the servers under systemd; run-*.sh would race those units for the ports.
        Assert.Contains("FINEAGE_RESTART:-systemd", manager, StringComparison.Ordinal);
        Assert.Contains("sudo systemctl restart", manager, StringComparison.Ordinal);
        Assert.DoesNotContain("raspberrypi", manager, StringComparison.Ordinal);
        Assert.DoesNotContain("/home/pi/", manager, StringComparison.Ordinal);
    }

    [Fact]
    public void Deploy_workflow_publishes_the_host_architecture_and_smoke()
    {
        string root = RepoRoot();
        string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "deploy.yml"));

        // homeserver is x86-64 Debian; an arm64 publish cannot execute there.
        Assert.Contains("-r linux-x64", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("linux-arm64", workflow, StringComparison.Ordinal);
        Assert.Contains("publish/smoke", workflow, StringComparison.Ordinal);
        Assert.Contains("publish/login publish/zone publish/smoke", workflow, StringComparison.Ordinal);
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
