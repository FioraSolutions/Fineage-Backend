using System.Diagnostics;
using Fineage.Accounts;
using Npgsql;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// #993: systemd and <c>systemctl stop</c> send SIGTERM, not SIGINT (Ctrl+C). The Zone and Login
/// process entrypoints cancel their shutdown token on SIGTERM; this test starts the real processes
/// and checks that the zone actually saves online characters, flushes the auditable economy ledger
/// and commits the nine <c>world_state</c> documents, then exits 0.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SigtermShutdownTests
{
    [Fact]
    public void Sigterm_to_the_zone_process_saves_online_characters_flushes_the_ledger_and_commits_world_state()
    {
        if (!PostgresReachable())
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "PostgreSQL is not reachable. Start postgres or set FINEAGE_DATABASE_URL.");
        }

        string repo = RepoRoot();
        string script = Path.Combine(repo, "tests", "deploy", "sigterm-shutdown.test.sh");
        Assert.True(File.Exists(script), script);

        var start = new ProcessStartInfo("bash", script)
        {
            WorkingDirectory = repo,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.Environment["PATH"] = Environment.GetEnvironmentVariable("PATH");
        start.Environment["HOME"] = Environment.GetEnvironmentVariable("HOME") ?? "";
        string? url = Environment.GetEnvironmentVariable(AccountStoreFactory.DatabaseUrlEnv);
        if (!string.IsNullOrWhiteSpace(url))
            start.Environment[AccountStoreFactory.DatabaseUrlEnv] = url;
        start.Environment["FINEAGE_BUILD_CONFIGURATION"] = "Debug";
        start.Environment["FINEAGE_DRILL_KEEP"] = "0";

        using var process = Process.Start(start) ?? throw new InvalidOperationException("failed to start " + script);
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(300_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw new TimeoutException("sigterm-shutdown.test.sh did not finish in 5 minutes.\n" + stdout + stderr);
        }

        Assert.True(process.ExitCode == 0, $"SIGTERM shutdown drill exited {process.ExitCode}\n{stdout}\n{stderr}");
        Assert.Contains("PASS: SIGTERM saved the online character", stdout, StringComparison.Ordinal);
        Assert.Contains("Zone SIGTERM exit=0", stdout, StringComparison.Ordinal);
        Assert.Contains("Login SIGTERM exit=0", stdout, StringComparison.Ordinal);
    }

    static bool PostgresReachable()
    {
        try
        {
            string url = AccountStoreFactory.ResolveConnectionString();
            using var conn = new NpgsqlConnection(url);
            conn.Open();
            return true;
        }
        catch
        {
            return false;
        }
    }

    static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "Fineage.Backend.slnx")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("could not locate Fineage.Backend.slnx from " + AppContext.BaseDirectory);
    }
}
