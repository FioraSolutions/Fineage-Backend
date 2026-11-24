using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

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
    /// <summary><c>Debug</c> / <c>Release</c> from <c>/proc/&lt;pid&gt;/cmdline</c> (<c>…/bin/Debug/…</c>), or null.</summary>
    internal static string? ZoneBuildFromProcess(int? zonePid)
    {
        if (zonePid is not > 0)
            return null;
        string path = $"/proc/{zonePid}/cmdline";
        if (!File.Exists(path))
            return null;
        string cmd = File.ReadAllText(path).Replace('\0', ' ');
        if (cmd.Contains("/bin/Debug/", StringComparison.Ordinal))
            return "Debug";
        if (cmd.Contains("/bin/Release/", StringComparison.Ordinal))
            return "Release";
        return null;
    }

    public static RunEnvironmentInfo Collect(int? zonePid = null)
    {
        string? commit = TryGit("rev-parse", "HEAD");
        string? branch = TryGit("branch", "--show-current");
        bool dirty = TryGit("status", "--porcelain") is { Length: > 0 };
        // The zone binary path is the truth (run-local-net.sh defaults to Debug); the env is only a fallback.
        string build = ZoneBuildFromProcess(zonePid)
                       ?? Environment.GetEnvironmentVariable("FINEAGE_BUILD_CONFIGURATION")
                       ?? Environment.GetEnvironmentVariable("DOTNET_BUILD_CONFIGURATION")
                       ?? "unknown";

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
            GcMode = DescribeZoneGc(zonePid),
            DotnetGcDynamicAdaptation = Environment.GetEnvironmentVariable("DOTNET_GCDynamicAdaptationMode"),
            DotnetTieredPgo = Environment.GetEnvironmentVariable("DOTNET_TieredPGO"),
            DotnetReadyToRun = Environment.GetEnvironmentVariable("DOTNET_ReadyToRun")
        };
    }

    /// <summary>
    /// GC mode of the zone process from its runtimeconfig. The load-test process is Workstation even when
    /// the zone project sets <c>ServerGarbageCollection</c>.
    /// </summary>
    public static string DescribeZoneGc(int? zonePid)
    {
        if (zonePid is not > 0)
            return "MISSING: zone pid was not passed, so the zone runtimeconfig could not be read";
        string? binary = ZoneBinaryPath(zonePid.Value);
        if (binary == null)
            return $"MISSING: /proc/{zonePid}/cmdline and /proc/{zonePid}/exe do not name Fineage.Zone or Fineage.Zone.dll";
        string? dir = Path.GetDirectoryName(binary);
        string config = Path.Combine(dir ?? ".", "Fineage.Zone.runtimeconfig.json");
        if (!File.Exists(config))
            return $"MISSING: zone runtimeconfig {config}";
        string json;
        try
        {
            json = File.ReadAllText(config);
        }
        catch (Exception ex)
        {
            return $"MISSING: could not read {config}: {ex.Message}";
        }

        return GcModeFromRuntimeConfig(json, config);
    }

    public static string GcModeFromRuntimeConfig(string json, string path)
    {
        if (json.Contains("\"System.GC.Server\": true", StringComparison.OrdinalIgnoreCase)
            || json.Contains("\"System.GC.Server\":true", StringComparison.OrdinalIgnoreCase))
            return "Server";
        if (json.Contains("\"System.GC.Server\": false", StringComparison.OrdinalIgnoreCase)
            || json.Contains("\"System.GC.Server\":false", StringComparison.OrdinalIgnoreCase))
            return "Workstation";
        return $"MISSING: {path} does not set System.GC.Server";
    }

    static string? ZoneBinaryPath(int pid)
    {
        string? fromCmd = MatchZoneBinary(ReadCmdline(pid), pid);
        if (fromCmd != null)
            return fromCmd;
        string? exe = Directory.ResolveLinkTarget($"/proc/{pid}/exe", returnFinalTarget: true)?.FullName;
        return exe != null && IsZoneBinary(exe) ? exe : null;
    }

    static string? ReadCmdline(int pid)
    {
        string cmdlinePath = $"/proc/{pid}/cmdline";
        if (!File.Exists(cmdlinePath))
            return null;
        return Encoding.UTF8.GetString(File.ReadAllBytes(cmdlinePath));
    }

    static string? MatchZoneBinary(string? cmdline, int pid)
    {
        if (string.IsNullOrEmpty(cmdline))
            return null;
        string? cwd = null;
        foreach (string arg in cmdline.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!IsZoneBinary(arg))
                continue;
            if (Path.IsPathRooted(arg))
                return arg;
            cwd ??= Directory.ResolveLinkTarget($"/proc/{pid}/cwd", returnFinalTarget: true)?.FullName;
            return cwd == null ? arg : Path.GetFullPath(Path.Combine(cwd, arg));
        }

        return null;
    }

    static bool IsZoneBinary(string path)
    {
        string name = Path.GetFileName(path);
        return name.Equals("Fineage.Zone.dll", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Fineage.Zone", StringComparison.OrdinalIgnoreCase);
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