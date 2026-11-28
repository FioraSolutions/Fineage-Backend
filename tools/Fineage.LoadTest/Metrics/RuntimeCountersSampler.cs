using System.Diagnostics;
using System.Globalization;

namespace Fineage.LoadTest.Metrics;

public sealed record RuntimeMetricPoint(DateTimeOffset At, double Value);

public sealed class RuntimeCounterStats
{
    public string Source { get; init; } = "";
    public string? UnavailableReason { get; init; }
    public double CpuPercentMin { get; init; }
    public double CpuPercentAvg { get; init; }
    public double CpuPercentMax { get; init; }
    public double AllocRateMbPerSecAvg { get; init; }
    public double GcHeapMbMax { get; init; }
    public double TimeInGcPercentMax { get; init; }
    public long Gen0CountDelta { get; init; }
    public long Gen1CountDelta { get; init; }
    public long Gen2CountDelta { get; init; }
    public double ThreadPoolQueueMax { get; init; }
    public double ThreadPoolThreadsMax { get; init; }
    public double LockContentionMax { get; init; }

    /// <summary>Timestamped GC heap and allocation samples; the trend gates filter these to the steady-state window.</summary>
    public IReadOnlyList<RuntimeMetricPoint> GcHeapMbSeries { get; init; } = [];

    public IReadOnlyList<RuntimeMetricPoint> AllocRateMbPerSecSeries { get; init; } = [];
}

/// <summary>Samples zone process via dotnet-counters when available; otherwise /proc CPU RSS fallback.</summary>
public sealed class RuntimeCountersSampler : IAsyncDisposable
{
    Process? _process;
    readonly List<double> _cpuSamples = [];
    readonly List<double> _allocMb = [];
    readonly List<double> _gcHeapMb = [];
    readonly List<RuntimeMetricPoint> _allocSeries = [];
    readonly List<RuntimeMetricPoint> _gcHeapSeries = [];
    readonly List<double> _timeInGc = [];
    readonly List<double> _tpQueue = [];
    readonly List<double> _tpThreads = [];
    readonly List<double> _lockContention = [];
    long _gen0Start = -1;
    long _gen0End;
    long _gen1Start = -1;
    long _gen1End;
    long _gen2Start = -1;
    long _gen2End;
    string? _unavailable;
    readonly int _pid;
    Task? _reader;
    CancellationTokenSource? _cts;
    string? _csvPath;

    public RuntimeCountersSampler(int pid) => _pid = pid;

    public async Task StartAsync(CancellationToken ct)
    {
        if (_pid <= 0)
        {
            _unavailable = "zone PID not provided";
            return;
        }

        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            _unavailable = "runtime counter collection only implemented for Linux/macOS";
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _csvPath = Path.Combine(Path.GetTempPath(), $"fineage-gc-{_pid}-{Guid.NewGuid():N}.csv");
        string counters =
            "System.Runtime[cpu-usage,alloc-rate,gc-heap-size,gen-0-gc-count,gen-1-gc-count,gen-2-gc-count," +
            "threadpool-queue-length,threadpool-thread-count,monitor-lock-contention-count,time-in-gc]";
        try
        {
            _process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet-counters",
                    ArgumentList =
                    {
                        "collect",
                        "-p", _pid.ToString(CultureInfo.InvariantCulture),
                        "--format", "csv",
                        "-o", _csvPath,
                        "--counters", counters
                    },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                },
                EnableRaisingEvents = true
            };
            _process.Start();
            _reader = Task.Run(() => ReadOutput(_process, _cts.Token), _cts.Token);
        }
        catch (Exception ex)
        {
            _unavailable = $"dotnet-counters failed to start: {ex.Message}";
            _process?.Dispose();
            _process = null;
        }

        await Task.CompletedTask;
    }

    void ReadOutput(Process proc, CancellationToken ct)
    {
        try
        {
            while (!proc.StandardOutput.EndOfStream && !ct.IsCancellationRequested)
            {
                string? line = proc.StandardOutput.ReadLine();
                if (line == null)
                    break;
                ParseLine(line);
            }
        }
        catch
        {
            // monitor exited
        }
    }

    public void ApplyCsv(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            _unavailable ??= $"MISSING: GC CSV at {_csvPath} is empty";
            return;
        }

        foreach (string raw in csv.Split('\n'))
            ParseLine(raw.Trim());
        if (_cpuSamples.Count == 0 && _gcHeapMb.Count == 0 && _allocMb.Count == 0)
            _unavailable ??= $"MISSING: GC CSV at {_csvPath} has no System.Runtime rows";
    }

    void ParseLine(string line)
    {
        if (line.Length == 0 || line.StartsWith("Timestamp", StringComparison.OrdinalIgnoreCase))
            return;
        string? name = CanonicalCounter(line);
        if (name == null || LastNumber(line) is not { } v)
            return;
        DateTimeOffset at = ParseTimestamp(line) ?? DateTimeOffset.UtcNow;

        switch (name)
        {
            case "cpu-usage" or "CPU Usage (%)":
                _cpuSamples.Add(v);
                break;
            case "alloc-rate" or "Allocation Rate (B / sec)":
                double allocMb = v / (1024 * 1024);
                _allocMb.Add(allocMb);
                _allocSeries.Add(new RuntimeMetricPoint(at, allocMb));
                break;
            case "gc-heap-size" or "GC Heap Size (MB)":
                _gcHeapMb.Add(v);
                _gcHeapSeries.Add(new RuntimeMetricPoint(at, v));
                break;
            case "gen-0-gc-count":
                NoteGen(rate: IsIntervalIncrement(line), ref _gen0Start, ref _gen0End, v);
                break;
            case "gen-1-gc-count":
                NoteGen(rate: IsIntervalIncrement(line), ref _gen1Start, ref _gen1End, v);
                break;
            case "gen-2-gc-count":
                NoteGen(rate: IsIntervalIncrement(line), ref _gen2Start, ref _gen2End, v);
                break;
            case "threadpool-queue-length":
                _tpQueue.Add(v);
                break;
            case "threadpool-thread-count":
                _tpThreads.Add(v);
                break;
            case "monitor-lock-contention-count":
                _lockContention.Add(v);
                break;
            case "time-in-gc":
                _timeInGc.Add(v);
                break;
        }
    }

    static bool IsIntervalIncrement(string line) =>
        line.Contains("Count /", StringComparison.Ordinal) || line.Contains("/ 1 sec", StringComparison.Ordinal);

    static void NoteGen(bool rate, ref long start, ref long end, double value)
    {
        long n = (long)Math.Round(value);
        if (rate)
        {
            if (start < 0)
                start = 0;
            end += n;
            return;
        }

        if (start < 0)
            start = n;
        end = n;
    }

    static string? CanonicalCounter(string line)
    {
        if (line.Contains("CPU Usage", StringComparison.OrdinalIgnoreCase) || line.Contains("cpu-usage", StringComparison.Ordinal))
            return "cpu-usage";
        if (line.Contains("Allocation Rate", StringComparison.OrdinalIgnoreCase) || line.Contains("alloc-rate", StringComparison.Ordinal))
            return "alloc-rate";
        if (line.Contains("GC Heap Size", StringComparison.OrdinalIgnoreCase) || line.Contains("gc-heap-size", StringComparison.Ordinal))
            return "gc-heap-size";
        if (line.Contains("gen-0-gc-count", StringComparison.Ordinal) || line.Contains("Gen 0 GC Count", StringComparison.OrdinalIgnoreCase))
            return "gen-0-gc-count";
        if (line.Contains("gen-1-gc-count", StringComparison.Ordinal) || line.Contains("Gen 1 GC Count", StringComparison.OrdinalIgnoreCase))
            return "gen-1-gc-count";
        if (line.Contains("gen-2-gc-count", StringComparison.Ordinal) || line.Contains("Gen 2 GC Count", StringComparison.OrdinalIgnoreCase))
            return "gen-2-gc-count";
        if (line.Contains("threadpool-queue-length", StringComparison.Ordinal) || line.Contains("ThreadPool Queue Length", StringComparison.OrdinalIgnoreCase))
            return "threadpool-queue-length";
        if (line.Contains("threadpool-thread-count", StringComparison.Ordinal) || line.Contains("ThreadPool Thread Count", StringComparison.OrdinalIgnoreCase))
            return "threadpool-thread-count";
        if (line.Contains("monitor-lock-contention-count", StringComparison.Ordinal) || line.Contains("Monitor Lock Contention", StringComparison.OrdinalIgnoreCase))
            return "monitor-lock-contention-count";
        if (line.Contains("time-in-gc", StringComparison.Ordinal) || line.Contains("% Time in GC", StringComparison.OrdinalIgnoreCase))
            return "time-in-gc";
        return null;
    }

    static DateTimeOffset? ParseTimestamp(string line)
    {
        int comma = line.IndexOf(',');
        if (comma <= 0)
            return null;
        string token = line[..comma].Trim().Trim('"');
        // dotnet-counters writes a naive local timestamp (no offset). Treating that as UTC
        // on a UTC+3 host pulled boot/ramp heap samples into the "steady-state" window.
        DateTimeStyles style = HasExplicitOffset(token)
            ? DateTimeStyles.RoundtripKind | DateTimeStyles.AdjustToUniversal
            : DateTimeStyles.AssumeLocal | DateTimeStyles.AdjustToUniversal;
        return DateTimeOffset.TryParse(token, CultureInfo.InvariantCulture, style, out var at)
            ? at.ToUniversalTime()
            : null;
    }

    static bool HasExplicitOffset(string token)
    {
        if (token.EndsWith('Z') || token.EndsWith('z'))
            return true;
        // "...+03:00" / "...-05:00" — a date-only 'T' must not count as a sign.
        int t = token.IndexOf('T');
        int start = t >= 0 ? t + 1 : 0;
        return token.IndexOf('+', start) >= 0 || token.LastIndexOf('-') > start;
    }

    static double? LastNumber(string line)
    {
        string[] parts = line.Split(',');
        for (int i = parts.Length - 1; i >= 0; i--)
        {
            string token = parts[i].Trim().Trim('"');
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                return value;
        }

        return null;
    }

    public async Task<RuntimeCounterStats> StopAndSummarizeAsync()
    {
        try
        {
            _cts?.Cancel();
            if (_process is { HasExited: false })
            {
                try
                {
                    Process.Start("kill", $"-INT {_process.Id}")?.WaitForExit(1000);
                }
                catch
                {
                    // kill(1) missing: fall through to Kill
                }

                if (!_process.HasExited)
                    _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
        }
        catch
        {
            // ignore
        }

        if (_csvPath != null && File.Exists(_csvPath))
        {
            string csv = await File.ReadAllTextAsync(_csvPath);
            ApplyCsv(csv);
        }
        else if (_unavailable == null)
        {
            _unavailable = $"MISSING: dotnet-counters CSV was not written ({_csvPath ?? "no path"})";
        }

        return BuildStats(includeProcFallback: true);
    }

    /// <summary>Parse a <c>dotnet-counters collect --format csv</c> file. Used by the report and by tests.</summary>
    public static RuntimeCounterStats ParseCsv(string csv)
    {
        var sampler = new RuntimeCountersSampler(0) { _csvPath = "fixture.csv" };
        sampler.ApplyCsv(csv);
        return sampler.BuildStats(includeProcFallback: false);
    }

    RuntimeCounterStats BuildStats(bool includeProcFallback)
    {
        if (includeProcFallback && _unavailable != null && _cpuSamples.Count == 0)
        {
            var procFallback = ProcCpuSampler.Sample(_pid);
            return new RuntimeCounterStats
            {
                Source = "unavailable",
                UnavailableReason = _unavailable,
                CpuPercentMin = procFallback.Min,
                CpuPercentAvg = procFallback.Avg,
                CpuPercentMax = procFallback.Max
            };
        }

        return new RuntimeCounterStats
        {
            Source = _cpuSamples.Count > 0 ? "dotnet-counters csv" : "dotnet-counters csv (no samples)",
            UnavailableReason = _cpuSamples.Count == 0 ? _unavailable ?? $"MISSING: no parseable rows in {_csvPath}" : null,
            CpuPercentMin = Min(_cpuSamples),
            CpuPercentAvg = Avg(_cpuSamples),
            CpuPercentMax = Max(_cpuSamples),
            AllocRateMbPerSecAvg = Avg(_allocMb),
            GcHeapMbMax = Max(_gcHeapMb),
            TimeInGcPercentMax = Max(_timeInGc),
            Gen0CountDelta = Delta(_gen0Start, _gen0End),
            Gen1CountDelta = Delta(_gen1Start, _gen1End),
            Gen2CountDelta = Delta(_gen2Start, _gen2End),
            ThreadPoolQueueMax = Max(_tpQueue),
            ThreadPoolThreadsMax = Max(_tpThreads),
            LockContentionMax = Max(_lockContention),
            GcHeapMbSeries = _gcHeapSeries.ToArray(),
            AllocRateMbPerSecSeries = _allocSeries.ToArray()
        };
    }

    static double Min(List<double> v) => v.Count == 0 ? 0 : v.Min();
    static double Max(List<double> v) => v.Count == 0 ? 0 : v.Max();
    static double Avg(List<double> v) => v.Count == 0 ? 0 : v.Average();
    static long Delta(long start, long end) => start < 0 ? 0 : Math.Max(0, end - start);

    public async ValueTask DisposeAsync()
    {
        await StopAndSummarizeAsync();
        _process?.Dispose();
        _cts?.Dispose();
    }
}

internal static class ProcCpuSampler
{
    public static (double Min, double Avg, double Max) Sample(int pid)
    {
        // single-point jiffies snapshot; load test report still records proc fallback explicitly
        try
        {
            string stat = File.ReadAllText($"/proc/{pid}/stat");
            string[] parts = stat.Split(' ');
            if (parts.Length < 17)
                return (0, 0, 0);
            long utime = long.Parse(parts[13], CultureInfo.InvariantCulture);
            long stime = long.Parse(parts[14], CultureInfo.InvariantCulture);
            double pct = (utime + stime) / (double)Environment.ProcessorCount;
            return (pct, pct, pct);
        }
        catch
        {
            return (0, 0, 0);
        }
    }
}