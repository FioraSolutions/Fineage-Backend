using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;

namespace Fineage.LoadTest.Reporting;

public sealed class RunEnvironmentInfo
{
    public string? GitCommit { get; init; }
    public string? GitBranch { get; init; }
    public bool GitDirty { get; init; }
    public string BuildConfiguration { get; init; } = "Release";
    public string OsDescription { get; init; } = "";
    public int ProcessorCount { get; init; }
    public long TotalRamMb { get; init; }
    public string DotNetVersion { get; init; } = "";
    public string GcMode { get; init; } = "";
    public string? DotnetGcDynamicAdaptation { get; init; }
    public string? DotnetTieredPgo { get; init; }
    public string? DotnetReadyToRun { get; init; }
}

public static class RunEnvironmentCollector
{
    public static RunEnvironmentInfo Collect()
    {
        string? commit = TryGit("rev-parse", "HEAD");
        string? branch = TryGit("branch", "--show-current");
        bool dirty = TryGit("status", "--porcelain") is { Length: > 0 };
        string build = Environment.GetEnvironmentVariable("FINEAGE_BUILD_CONFIGURATION")
                         ?? Environment.GetEnvironmentVariable("DOTNET_BUILD_CONFIGURATION")
                         ?? "Release";

        long ramMb = 0;
        if (File.Exists("/proc/meminfo"))
        {
            foreach (string line in File.ReadLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                {
                    string[] bits = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (bits.Length >= 2 && long.TryParse(bits[1], out long kb))
                        ramMb = kb / 1024;
                    break;
                }
            }
        }

        return new RunEnvironmentInfo
        {
            GitCommit = commit,
            GitBranch = branch,
            GitDirty = dirty,
            BuildConfiguration = build,
            OsDescription = RuntimeInformation.OSDescription,
            ProcessorCount = Environment.ProcessorCount,
            TotalRamMb = ramMb,
            DotNetVersion = RuntimeInformation.FrameworkDescription,
            GcMode = GCSettings.IsServerGC ? "Server" : "Workstation",
            DotnetGcDynamicAdaptation = Environment.GetEnvironmentVariable("DOTNET_GCDynamicAdaptationMode"),
            DotnetTieredPgo = Environment.GetEnvironmentVariable("DOTNET_TieredPGO"),
            DotnetReadyToRun = Environment.GetEnvironmentVariable("DOTNET_ReadyToRun")
        };
    }

    static string? TryGit(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = FindRepoRoot() ?? Directory.GetCurrentDirectory()
            };
            foreach (string arg in args)
                psi.ArgumentList.Add(arg);
            using var proc = Process.Start(psi);
            if (proc == null)
                return null;
            string output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(5000);
            return proc.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }

    static string? FindRepoRoot()
    {
        string? dir = Directory.GetCurrentDirectory();
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        return null;
    }
}