using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fineage.Protocol.Health;

/// <summary>One named check: ok or not, and why.</summary>
public sealed record HealthCheck(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("detail")] string Detail);

/// <summary>Liveness or readiness: ok only when every check is ok. Served as JSON with 200 or 503 (#989).</summary>
public sealed record HealthReport(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("checks")] IReadOnlyList<HealthCheck> Checks)
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public int StatusCode => Ok ? 200 : 503;

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>The failing checks, one line, for deploy logs.</summary>
    public string Failures => string.Join("; ", Checks.Where(check => !check.Ok).Select(check => $"{check.Name}: {check.Detail}"));
}

/// <summary>
/// The process's health contract (#989).
/// <list type="bullet">
/// <item><b>Liveness</b> — the process answers and its live probes pass (for the zone: the simulation loop is not
/// stuck). A failing liveness means "restart me".</item>
/// <item><b>Readiness</b> — every boot stage registered with <see cref="Require"/> has completed and every ready
/// probe passes (database reachable and migrated, simulation ticking within threshold). A failing readiness means
/// "do not send players here", and a deploy must not call the release good.</item>
/// </list>
/// A probe that throws is a failed check naming the exception, never a pass.
/// </summary>
public sealed class HealthBoard
{
    readonly Lock _sync = new();
    readonly List<string> _stageOrder = [];
    readonly Dictionary<string, HealthCheck> _stages = new(StringComparer.Ordinal);
    readonly List<(string Name, Func<HealthCheck> Probe)> _ready = [];
    readonly List<(string Name, Func<HealthCheck> Probe)> _live = [];

    /// <summary>Registers a boot stage that keeps readiness at 503 until <see cref="Complete"/>.</summary>
    public void Require(string stage, string pendingDetail = "not started")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        lock (_sync)
        {
            if (_stages.ContainsKey(stage))
                throw new InvalidOperationException($"health stage '{stage}' is registered twice");
            _stageOrder.Add(stage);
            _stages[stage] = new HealthCheck(stage, false, pendingDetail);
        }
    }

    public void Complete(string stage, string detail = "done") => Set(stage, true, detail);

    /// <summary>A stage that failed for good: readiness stays 503 with <paramref name="detail"/>.</summary>
    public void Fail(string stage, string detail) => Set(stage, false, detail);

    /// <summary>A check evaluated on every readiness request.</summary>
    public void AddReadyProbe(string name, Func<HealthCheck> probe) => Add(_ready, name, probe);

    /// <summary>A check evaluated on every liveness (and readiness) request.</summary>
    public void AddLiveProbe(string name, Func<HealthCheck> probe) => Add(_live, name, probe);

    public HealthReport Liveness()
    {
        var checks = new List<HealthCheck> { new("process", true, $"pid {Environment.ProcessId}") };
        checks.AddRange(Run(_live));
        return new HealthReport(checks.All(check => check.Ok), checks);
    }

    public HealthReport Readiness()
    {
        var checks = new List<HealthCheck>();
        lock (_sync)
            checks.AddRange(_stageOrder.Select(stage => _stages[stage]));
        checks.AddRange(Run(_live));
        checks.AddRange(Run(_ready));
        return new HealthReport(checks.All(check => check.Ok), checks);
    }

    void Set(string stage, bool ok, string detail)
    {
        lock (_sync)
        {
            if (!_stages.ContainsKey(stage))
                throw new InvalidOperationException($"health stage '{stage}' was never registered with Require");
            _stages[stage] = new HealthCheck(stage, ok, detail);
        }
    }

    void Add(List<(string, Func<HealthCheck>)> list, string name, Func<HealthCheck> probe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(probe);
        lock (_sync)
            list.Add((name, probe));
    }

    List<HealthCheck> Run(List<(string Name, Func<HealthCheck> Probe)> probes)
    {
        (string Name, Func<HealthCheck> Probe)[] copy;
        lock (_sync)
            copy = probes.ToArray();
        var results = new List<HealthCheck>(copy.Length);
        foreach (var (name, probe) in copy)
        {
            try
            {
                results.Add(probe() with { Name = name });
            }
            catch (Exception ex)
            {
                results.Add(new HealthCheck(name, false, $"{ex.GetType().Name}: {ex.Message}"));
            }
        }

        return results;
    }
}

/// <summary>Runs an expensive probe (a database round trip) at most once per <paramref name="ttl"/>.</summary>
public sealed class CachedProbe(Func<HealthCheck> probe, TimeSpan ttl)
{
    readonly Lock _sync = new();
    HealthCheck? _last;
    long _at;

    public HealthCheck Read()
    {
        lock (_sync)
        {
            if (_last != null && Stopwatch.GetElapsedTime(_at) < ttl)
                return _last;
        }

        HealthCheck result;
        try
        {
            result = probe();
        }
        catch (Exception ex)
        {
            result = new HealthCheck("probe", false, $"{ex.GetType().Name}: {ex.Message}");
        }

        lock (_sync)
        {
            _last = result;
            _at = Stopwatch.GetTimestamp();
        }

        return result;
    }
}

/// <summary>Env knobs for the health thresholds.</summary>
public static class HealthSettings
{
    public const string ReadyTickLagVariable = "FINEAGE_READY_MAX_TICK_LAG_MS";
    public const string LiveTickLagVariable = "FINEAGE_LIVE_MAX_TICK_LAG_MS";
    public const int DefaultReadyTickLagMs = 5_000;
    public const int DefaultLiveTickLagMs = 30_000;

    public static TimeSpan ReadyTickLag(Func<string, string?>? env = null) =>
        Milliseconds(env, ReadyTickLagVariable, DefaultReadyTickLagMs);

    public static TimeSpan LiveTickLag(Func<string, string?>? env = null) =>
        Milliseconds(env, LiveTickLagVariable, DefaultLiveTickLagMs);

    static TimeSpan Milliseconds(Func<string, string?>? env, string name, int fallback)
    {
        string? raw = (env ?? Environment.GetEnvironmentVariable)(name)?.Trim();
        if (string.IsNullOrEmpty(raw))
            return TimeSpan.FromMilliseconds(fallback);
        if (!int.TryParse(raw, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int ms) || ms < 1)
            throw new InvalidOperationException($"{name}={raw} is not a positive number of milliseconds");
        return TimeSpan.FromMilliseconds(ms);
    }
}
