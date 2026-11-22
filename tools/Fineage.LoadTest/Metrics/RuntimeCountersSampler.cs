using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Fineage.LoadTest.Metrics;

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
}

/// <summary>Samples zone process via dotnet-counters when available; otherwise /proc CPU RSS fallback.</summary>
public sealed class RuntimeCountersSampler : IAsyncDisposable
{
    Process? _process;
    readonly List<double> _cpuSamples = [];
    readonly List<double> _allocMb = [];
    readonly List<double> _gcHeapMb = [];
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
                        "monitor",
                        "-p", _pid.ToString(CultureInfo.InvariantCulture),
                        "--counters", counters,
                        "--interval", "1"
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

    static readonly Regex CsvRx = new(
        @"^(?<name>[^,]+),\s*(?<value>-?[0-9.]+)",
        RegexOptions.Compiled);

    void ParseLine(string line)
    {
        if (!line.Contains(','))
            return;
        var m = CsvRx.Match(line.Trim());
        if (!m.Success)
            return;
        string name = m.Groups["name"].Value.Trim();
        if (!double.TryParse(m.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            return;

        switch (name)
        {
            case "cpu-usage" or "CPU Usage (%)":
                _cpuSamples.Add(v);
                break;
            case "alloc-rate" or "Allocation Rate (B / sec)":
                _allocMb.Add(v / (1024 * 1024));
                break;
            case "gc-heap-size" or "GC Heap Size (MB)":
                _gcHeapMb.Add(v);
                break;
            case "gen-0-gc-count":
                if (_gen0Start < 0) _gen0Start = (long)v;
                _gen0End = (long)v;
                break;
            case "gen-1-gc-count":
                if (_gen1Start < 0) _gen1Start = (long)v;
                _gen1End = (long)v;
                break;
            case "gen-2-gc-count":
                if (_gen2Start < 0) _gen2Start = (long)v;
                _gen2End = (long)v;
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

    public async Task<RuntimeCounterStats> StopAndSummarizeAsync()
    {
        try
        {
            _cts?.Cancel();
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
        }
        catch
        {
            // ignore
        }

        if (_unavailable != null)
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
            Source = _cpuSamples.Count > 0 ? "dotnet-counters" : "dotnet-counters (no samples)",
            UnavailableReason = _cpuSamples.Count == 0 ? "monitor produced no parseable samples" : null,
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
            LockContentionMax = Max(_lockContention)
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