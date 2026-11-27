using System.Diagnostics;
using System.Globalization;

namespace Fineage.LoadTest.Metrics;

public sealed record RssSample(DateTimeOffset At, double RssMb);

/// <summary>
/// Periodic zone-process RSS sampler for soak/endurance trend gates (#990). Linux reads
/// <c>/proc/&lt;pid&gt;/status</c>; other hosts fall back to <c>ps -o rss=</c>, which also works on macOS.
/// A missing process or unreadable counter is surfaced by the trend gate, never replaced with 0.
/// </summary>
public sealed class ProcessRssSampler : IAsyncDisposable
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(5);

    readonly int _pid;
    readonly TimeSpan _interval;
    readonly List<RssSample> _samples = [];
    CancellationTokenSource? _cts;
    Task? _loop;

    public ProcessRssSampler(int pid, TimeSpan? interval = null)
    {
        _pid = pid;
        _interval = interval ?? DefaultInterval;
    }

    public IReadOnlyList<RssSample> Samples
    {
        get
        {
            lock (_samples)
                return _samples.ToArray();
        }
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (_pid <= 0)
            return;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loop = Task.Run(() => SampleLoopAsync(_cts.Token), CancellationToken.None);
        await Task.Yield();
    }

    async Task SampleLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (TrySampleRssMb(_pid) is { } mb)
            {
                lock (_samples)
                    _samples.Add(new RssSample(DateTimeOffset.UtcNow, mb));
            }

            try
            {
                await Task.Delay(_interval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task<IReadOnlyList<RssSample>> StopAsync()
    {
        if (_cts != null)
        {
            await _cts.CancelAsync();
            if (_loop != null)
            {
                try
                {
                    await _loop.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
                {
                    // The sampler is advisory; the report records how many samples were collected.
                }
            }
        }

        return Samples;
    }

    /// <summary>One RSS reading in MiB, or null when the host cannot read this pid.</summary>
    public static double? TrySampleRssMb(int pid)
    {
        if (pid <= 0)
            return null;

        string statusPath = $"/proc/{pid}/status";
        if (File.Exists(statusPath))
        {
            try
            {
                foreach (string line in File.ReadLines(statusPath))
                {
                    if (!line.StartsWith("VmRSS:", StringComparison.Ordinal))
                        continue;
                    string[] bits = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (bits.Length >= 2 && long.TryParse(bits[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long kb))
                        return kb / 1024.0;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // fall through to ps
            }
        }

        return TrySampleWithPs(pid);
    }

    static double? TrySampleWithPs(int pid)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ps",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add("rss=");
            psi.ArgumentList.Add("-p");
            psi.ArgumentList.Add(pid.ToString(CultureInfo.InvariantCulture));
            using var proc = Process.Start(psi);
            if (proc == null)
                return null;
            string output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(2000);
            if (proc.ExitCode != 0 || !long.TryParse(output, NumberStyles.Integer, CultureInfo.InvariantCulture, out long kb))
                return null;
            return kb / 1024.0;
        }
        catch
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts?.Dispose();
    }
}
