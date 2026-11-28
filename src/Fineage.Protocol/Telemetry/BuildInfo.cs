using System.Reflection;

namespace Fineage.Protocol.Telemetry;

/// <summary>
/// Build provenance of the process that renders the metrics. This reads the entry assembly the process actually
/// loaded (not the DLL currently on disk), so a rebuild under a running Zone cannot impersonate a newer binary.
/// </summary>
public static class BuildInfo
{
    static readonly Assembly Entry = Assembly.GetEntryAssembly() ?? typeof(BuildInfo).Assembly;

    public static string InformationalVersion { get; } = ResolveInformationalVersion();

    public static string Commit { get; } = ParseCommit(InformationalVersion);

    public static string Configuration { get; } =
        Entry.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "unknown";

    public static string ModuleId { get; } = Entry.ManifestModule.ModuleVersionId.ToString("D");

    static string ResolveInformationalVersion() =>
        Entry.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Entry.GetName().Version?.ToString()
        ?? "unknown";

    /// <summary>The SDK stamps <c>&lt;version&gt;+&lt;source revision&gt;</c>; the hash is the build commit.</summary>
    public static string ParseCommit(string informationalVersion)
    {
        int plus = informationalVersion.IndexOf('+');
        if (plus < 0)
            return informationalVersion;
        string tail = informationalVersion[(plus + 1)..];
        int dot = tail.IndexOf('.');
        return dot < 0 ? tail : tail[..dot];
    }
}
