using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
using Fineage.LoadTest.Metrics;

namespace Fineage.LoadTest.Reporting;

public sealed class RunEnvironmentInfo
{
    public string? GitCommit { get; init; }
    public string? GitBranch { get; init; }
    public bool GitDirty { get; init; }
    /// <summary>Dirty only when a compiled source/project file differs; docs and untracked scenarios do not fail a run.</summary>
    public bool SourceDirty { get; init; }
    public string BuildConfiguration { get; init; } = "Release";
    public string OsDescription { get; init; } = "";
    public int ProcessorCount { get; init; }
    public long TotalRamMb { get; init; }
    public string DotNetVersion { get; init; } = "";
    public string GcMode { get; init; } = "";
    public string? DotnetGcDynamicAdaptation { get; init; }
    public string? DotnetTieredPgo { get; init; }
    public string? DotnetReadyToRun { get; init; }
    /// <summary>Commit compiled into the Zone assembly the process actually loaded.</summary>
    public string? ZoneBuildCommit { get; init; }
    /// <summary>Debug/Release compiled into the loaded Zone assembly.</summary>
    public string? ZoneBuildConfiguration { get; init; }
    public string? ZoneBuildModuleId { get; init; }
    public string? ZoneBuildVersion { get; init; }
    /// <summary>Zone binary path when it could be resolved (Linux /proc or an explicit --zone-dll).</summary>
    public string? ZoneBinaryPath { get; init; }
    /// <summary>MVID of the Zone DLL on disk at report time; differs from the loaded module after a rebuild.</summary>
    public string? ZoneBinaryModuleId { get; init; }
}

public static class RunEnvironmentCollector
{
    /// <summary><c>Debug</c> / <c>Release</c> from <c>/proc/&lt;pid&gt;/cmdline</c> (<c>…/bin/Debug/…</c>), or null.</summary>
    public static string? ZoneBuildFromProcess(int? zonePid)
    {
        if (zonePid is not > 0)
            return null;
        // /proc is Linux-only. On macOS the process metadata is simply unavailable, and that is a MISSING field, never
        // a reason to abort the report.
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

    /// <summary>Whether this host exposes <c>/proc</c>, so process introspection can be attempted at all.</summary>
    internal static bool HasProcFileSystem => OperatingSystem.IsLinux() && Directory.Exists("/proc");

    /// <summary>The module MVID of a managed DLL, read without loading it. Null when the path is missing/not managed.</summary>
    public static string? ModuleIdOfFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        try
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata)
                return null;
            MetadataReader md = pe.GetMetadataReader();
            return md.GetGuid(md.GetModuleDefinition().Mvid).ToString();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// A run is only a valid before/after sample when the loaded Zone assembly is the intended build. Missing build
    /// info, a commit different from the intended one, or a DLL whose MVID changed under the running process is loud.
    /// </summary>
    public static IReadOnlyList<string> ProvenanceProblems(
        RunEnvironmentInfo? env,
        string? expectedCommit = null,
        bool requireClean = false)
    {
        var problems = new List<string>();
        if (env == null)
            return ["Zone provenance: RunEnvironmentCollector did not run."];
        string? intended = expectedCommit ?? env.GitCommit;
        if (string.IsNullOrWhiteSpace(env.ZoneBuildCommit))
        {
            problems.Add("Zone provenance: fineage_build_info is missing from the Zone scrape; the loaded binary cannot be tied to a commit.");
        }
        else if (!string.IsNullOrWhiteSpace(intended) &&
                 !env.ZoneBuildCommit.StartsWith(intended, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"Zone provenance: loaded Zone build {env.ZoneBuildCommit} != intended {intended}.");
        }

        if (env.ZoneBuildModuleId is { Length: > 0 } loaded &&
            env.ZoneBinaryModuleId is { Length: > 0 } onDisk &&
            !string.Equals(loaded, onDisk, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"Zone provenance: loaded module {loaded} != {env.ZoneBinaryPath ?? "binary"} module {onDisk}; the DLL was rebuilt under the running Zone.");
        }

        if (requireClean)
        {
            if (string.IsNullOrWhiteSpace(env.ZoneBuildConfiguration) ||
                string.Equals(env.ZoneBuildConfiguration, "unknown", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add("Zone provenance: the loaded Zone configuration is missing from fineage_build_info.");
            }

            if (env.ZoneBuildModuleId is not { Length: > 0 })
                problems.Add("Zone provenance: the loaded Zone module id is missing from fineage_build_info; the loaded binary cannot be tied to the --zone-dll file.");
            if (env.ZoneBinaryModuleId is not { Length: > 0 })
                problems.Add("Zone provenance: --zone-dll is required for a clean benchmark; the loaded binary cannot be tied to the file.");
            if (string.IsNullOrWhiteSpace(intended))
                problems.Add("Zone provenance: no intended commit (git HEAD unavailable and --expect-zone-commit not set).");
            if (env.SourceDirty)
                problems.Add("Zone provenance: compiled source is dirty; the Zone binary is not tied to a clean commit.");
        }

        return problems;
    }

    public static RunEnvironmentInfo Collect(int? zonePid = null, string? zoneDll = null, ZoneBuildInfo? zone = null)
    {
        string? commit = TryGit("rev-parse", "HEAD");
        string? branch = TryGit("branch", "--show-current");
        bool dirty = TryGit("status", "--porcelain") is { Length: > 0 };
        var sourcePaths = new List<string>
        {
            "src/Fineage.Zone", "src/Fineage.Protocol", "src/Fineage.Game", "src/Fineage.Accounts",
            "src/Fineage.Net.Host", "src/Fineage.Net.Contract", "src/Fineage.Login",
            "tools/Fineage.LoadTest", "*.props", "*.targets"
        };
        var sourceArgs = new List<string> { "status", "--porcelain", "--" };
        sourceArgs.AddRange(sourcePaths);
        bool sourceDirty = TryGit([.. sourceArgs]) is { Length: > 0 };
        string? binary = zoneDll ?? ZoneBinaryPath(zonePid ?? 0);
        // The loaded zone assembly is the truth; the process cmdline/env is only a fallback.
        string build = zone?.Configuration
                       ?? ZoneBuildFromProcess(zonePid)
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
            SourceDirty = sourceDirty,
            BuildConfiguration = build,
            OsDescription = RuntimeInformation.OSDescription,
            ProcessorCount = Environment.ProcessorCount,
            TotalRamMb = ramMb,
            DotNetVersion = RuntimeInformation.FrameworkDescription,
            GcMode = DescribeZoneGc(zonePid),
            DotnetGcDynamicAdaptation = Environment.GetEnvironmentVariable("DOTNET_GCDynamicAdaptationMode"),
            DotnetTieredPgo = Environment.GetEnvironmentVariable("DOTNET_TieredPGO"),
            DotnetReadyToRun = Environment.GetEnvironmentVariable("DOTNET_ReadyToRun"),
            ZoneBuildCommit = zone?.Commit,
            ZoneBuildConfiguration = zone?.Configuration,
            ZoneBuildModuleId = zone?.ModuleId,
            ZoneBuildVersion = zone?.Version,
            ZoneBinaryPath = binary,
            ZoneBinaryModuleId = ModuleIdOfFile(binary)
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
            return HasProcFileSystem
                ? $"MISSING: /proc/{zonePid}/cmdline and /proc/{zonePid}/exe do not name Fineage.Zone or Fineage.Zone.dll"
                : $"MISSING: this host has no /proc (not Linux), so the zone binary of pid {zonePid} cannot be resolved";
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
        if (!HasProcFileSystem)
            return null;
        string link = $"/proc/{pid}/exe";
        try
        {
            string? exe = Directory.ResolveLinkTarget(link, returnFinalTarget: true)?.FullName;
            return exe != null && IsZoneBinary(exe) ? exe : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The process vanished between the two reads, or /proc hides it: not knowing the binary is MISSING data.
            return null;
        }
    }

    static string? ReadCmdline(int pid)
    {
        string cmdlinePath = $"/proc/{pid}/cmdline";
        if (!File.Exists(cmdlinePath))
            return null;
        try
        {
            return Encoding.UTF8.GetString(File.ReadAllBytes(cmdlinePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
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
            cwd ??= ProcCwd(pid);
            return cwd == null ? arg : Path.GetFullPath(Path.Combine(cwd, arg));
        }

        return null;
    }

    static string? ProcCwd(int pid)
    {
        string link = $"/proc/{pid}/cwd";
        if (!HasProcFileSystem || !Directory.Exists("/proc"))
            return null;
        try
        {
            return Directory.ResolveLinkTarget(link, returnFinalTarget: true)?.FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
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