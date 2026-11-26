namespace Fineage.LoadTest.Metrics;

public sealed class ServerMetricsSampler
{
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };
    readonly int _loginMetricsPort;
    readonly int _zoneMetricsPort;

    public const int DefaultLoginMetricsPort = 9464;
    public const int DefaultZoneMetricsPort = 9465;
    const string LoginMetricsPortEnv = "FINEAGE_LOGIN_METRICS_PORT";
    const string ZoneMetricsPortEnv = "FINEAGE_ZONE_METRICS_PORT";

    /// <summary>
    /// Ports come from the same environment knobs the servers read, so a stack started on non-default
    /// ports (an isolated soak beside a running playtest stack) is scraped on the port it actually listens on.
    /// </summary>
    public ServerMetricsSampler(int? loginMetricsPort = null, int? zoneMetricsPort = null)
    {
        _loginMetricsPort = loginMetricsPort ?? ResolvePort(LoginMetricsPortEnv, DefaultLoginMetricsPort);
        _zoneMetricsPort = zoneMetricsPort ?? ResolvePort(ZoneMetricsPortEnv, DefaultZoneMetricsPort);
    }

    static int ResolvePort(string variable, int fallback)
    {
        string? text = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(text))
            return fallback;
        return int.TryParse(text.Trim(), out int port) && port is > 0 and <= 65535 ? port : fallback;
    }

    public async Task<List<PrometheusSample>> SampleZoneDuringAsync(TimeSpan interval, TimeSpan duration, CancellationToken ct)
    {
        var samples = new List<PrometheusSample>();
        RawScrapes.Clear();
        var end = DateTimeOffset.UtcNow + duration;
        while (DateTimeOffset.UtcNow < end && !ct.IsCancellationRequested)
        {
            string? text = await FetchAsync($"http://127.0.0.1:{_zoneMetricsPort}/metrics", ct);
            if (text != null)
            {
                RawScrapes.Add(text);
                samples.Add(PrometheusParser.Parse(text));
            }
            try
            {
                await Task.Delay(interval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return samples;
    }

    public List<string> RawScrapes { get; } = [];

    public async Task<PrometheusSample?> SampleZoneOnceAsync(CancellationToken ct) =>
        await FetchAsync($"http://127.0.0.1:{_zoneMetricsPort}/metrics", ct) is { } t ? PrometheusParser.Parse(t) : null;

    async Task<string?> FetchAsync(string url, CancellationToken ct)
    {
        try
        {
            return await _http.GetStringAsync(url, ct);
        }
        catch
        {
            return null;
        }
    }
}

public sealed class ProcessMetricsSampler
{
    public ProcessSample? SamplePid(int pid)
    {
        string statPath = $"/proc/{pid}/stat";
        string statusPath = $"/proc/{pid}/status";
        if (!File.Exists(statPath) || !File.Exists(statusPath))
            return null;

        string stat = File.ReadAllText(statPath);
        string[] parts = stat.Split(' ');
        if (parts.Length < 17)
            return null;

        long rssPages = long.Parse(parts[23]);
        long pageSize = Environment.SystemPageSize;
        long rssBytes = rssPages * pageSize;

        long vmPeak = 0;
        foreach (string line in File.ReadLines(statusPath))
        {
            if (line.StartsWith("VmHWM:", StringComparison.Ordinal))
            {
                vmPeak = ParseKb(line);
                break;
            }
        }

        return new ProcessSample { Pid = pid, RssBytes = rssBytes, VmHwmKb = vmPeak };
    }

    static long ParseKb(string line)
    {
        string[] bits = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return bits.Length >= 2 ? long.Parse(bits[1]) : 0;
    }
}

public sealed class ProcessSample
{
    public int Pid { get; init; }
    public long RssBytes { get; init; }
    public long VmHwmKb { get; init; }
}