using System.Diagnostics;
using Xunit;

namespace Fineage.Login.Tests;

/// <summary>
/// deploy/run-login.sh and run-zone.sh load configuration with <c>source fineage.env</c>. An unquoted
/// Npgsql connection string contains <c>;</c>, which bash treats as a command separator, so only
/// <c>Host=127.0.0.1</c> survives. That is exactly how the first Postgres deploy to the Pi failed:
/// every later key was dropped and Npgsql reported "No password has been provided".
/// </summary>
public class PiEnvFileTests
{
    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "deploy")) &&
                File.Exists(Path.Combine(dir.FullName, "Fineage.Backend.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("could not locate the repository root");
    }

    [Fact]
    public void Example_env_survives_bash_source_with_the_connection_string_intact()
    {
        string example = Path.Combine(RepoRoot(), "deploy", "fineage.env.example");
        Assert.True(File.Exists(example), $"missing {example}");

        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-c");
        // Same shape as the deploy scripts: set -a; source; set +a.
        psi.ArgumentList.Add($"set -a; source '{example}'; set +a; printf '%s' \"$FINEAGE_DATABASE_URL\"");

        using var process = Process.Start(psi);
        Assert.NotNull(process);
        string stdout = process!.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        // bash is unavailable on some CI images; skip rather than fail for the wrong reason.
        if (process.ExitCode != 0 && stderr.Contains("No such file", StringComparison.OrdinalIgnoreCase))
            return;

        Assert.Contains("Database=fineage", stdout, StringComparison.Ordinal);
        Assert.Contains("Username=fineage", stdout, StringComparison.Ordinal);
        Assert.Contains("Password=fineage", stdout, StringComparison.Ordinal);
    }
}
