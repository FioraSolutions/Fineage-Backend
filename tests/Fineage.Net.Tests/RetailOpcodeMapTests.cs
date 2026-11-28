using System.Diagnostics;
using Xunit;

namespace Fineage.Net.Tests;

/// <summary>
/// The markdown coverage tables and docs/data/retail-net-opcode-map.json must agree,
/// and every mapped Fineage name must exist in NetOpcode (see ADR 0001 / #994).
/// </summary>
public sealed class RetailOpcodeMapTests
{
    [Fact]
    public void Wire_coverage_and_the_machine_readable_map_agree()
    {
        string root = RepoRoot();
        string script = Path.Combine(root, "tools", "check_retail_opcode_map.py");
        Assert.True(File.Exists(script), script);
        var start = new ProcessStartInfo
        {
            FileName = "python3",
            Arguments = Quote(script),
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var proc = Process.Start(start) ?? throw new InvalidOperationException("python3 failed to start");
        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        if (!proc.WaitForExit(60_000))
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
            Assert.Fail("check_retail_opcode_map.py timed out");
        }

        Assert.True(proc.ExitCode == 0, stdout + stderr);
        Assert.Contains("retail opcode map ok", stdout);
    }

    [Fact]
    public void Contract_sync_gap_file_is_sorted_and_unique()
    {
        string path = Path.Combine(RepoRoot(), "docs", "data", "net-contract-client-gap.txt");
        Assert.True(File.Exists(path), path);
        var entries = File.ReadAllLines(path)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToList();
        Assert.Equal(entries.OrderBy(s => s, StringComparer.Ordinal).ToList(), entries);
        Assert.Equal(entries.Count, entries.Distinct().Count());
        Assert.All(entries, e => Assert.EndsWith(".cs", e));
    }

    static string Quote(string path) => '"' + path.Replace("\"", "\\\"") + '"';

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "scripts", "run-local-net.sh")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
