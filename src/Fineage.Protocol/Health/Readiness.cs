using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fineage.Protocol.Health;

/// <summary>One required dependency. A missing/unconfigured probe is a failed check, never a default pass.</summary>
public sealed record ReadinessCheck(string Name, bool Ok, string? Detail = null);

/// <summary>
/// The readiness contract shared by Login and Zone. Readiness means the process can do its job now:
/// every required dependency answered and every required startup stage completed. Liveness is separate
/// and only means the process can answer an HTTP request.
/// </summary>
public sealed record ReadinessReport(bool Ready, IReadOnlyList<ReadinessCheck> Checks)
{
    public static ReadinessReport NotConfigured(string role) =>
        new(false, [new ReadinessCheck("probe", false, $"{role} readiness probe is not configured")]);

    public static ReadinessReport From(IEnumerable<ReadinessCheck> checks)
    {
        var list = checks.ToList();
        if (list.Count == 0)
            return new ReadinessReport(false, [new ReadinessCheck("probe", false, "readiness reported no checks")]);
        return new ReadinessReport(list.All(check => check.Ok), list);
    }

    /// <summary>Every failed check with its concrete reason, suitable for a 503 body and deploy logs.</summary>
    public IReadOnlyList<string> Reasons => Checks
        .Where(check => !check.Ok)
        .Select(check => string.IsNullOrWhiteSpace(check.Detail) ? check.Name : $"{check.Name}: {check.Detail}")
        .ToList();
}

/// <summary>Wire body for <c>/readyz</c>, <c>/livez</c> and Zone's <c>/status</c>.</summary>
public sealed record HealthBody(
    string Status,
    bool Ready,
    string Role,
    IReadOnlyDictionary<string, bool> Checks,
    IReadOnlyList<string> Reasons,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PlayersOnline = null)
{
    public const string LiveStatus = "live";
    public const string ReadyStatus = "ready";
    public const string NotReadyStatus = "not_ready";

    public static HealthBody Live(string role) =>
        new(LiveStatus, true, role, new Dictionary<string, bool> { ["process"] = true }, []);

    public static HealthBody From(string role, ReadinessReport report, int? playersOnline = null) =>
        new(
            report.Ready ? ReadyStatus : NotReadyStatus,
            report.Ready,
            role,
            report.Checks.ToDictionary(check => check.Name, check => check.Ok, StringComparer.Ordinal),
            report.Reasons,
            playersOnline);

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string ToJson() => JsonSerializer.Serialize(this, Json);
}
