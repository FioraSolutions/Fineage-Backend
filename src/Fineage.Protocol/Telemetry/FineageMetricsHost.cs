using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Fineage.Protocol.Telemetry;

public sealed class FineageMetricsHost : IAsyncDisposable
{
    readonly CancellationTokenSource _cts = new();
    readonly MetricsCollector _collector;
    readonly object _scrapeGate = new();
    readonly Stopwatch _scrapeClock = Stopwatch.StartNew();
    Task? _summary;
    PrometheusEndpoint? _http;
    TrafficTotals _previousScrape;

    FineageMetricsHost(MetricsCollector collector, TrafficTotals baseline)
    {
        _collector = collector;
        _previousScrape = baseline;
    }

    public int? MetricsPort => _http?.Port;

    public static FineageMetricsHost? Start(string role, TextWriter output, TextWriter error)
    {
        int summarySeconds = MetricsEnv.ParseSummarySeconds(Environment.GetEnvironmentVariable(MetricsEnv.SummarySecondsVariable));
        string rolePortVariable = MetricsEnv.RolePortVariable(role);
        string? portText = Environment.GetEnvironmentVariable(rolePortVariable);
        string portVariable = rolePortVariable;
        if (string.IsNullOrWhiteSpace(portText))
        {
            portText = Environment.GetEnvironmentVariable(MetricsEnv.PortVariable);
            portVariable = MetricsEnv.PortVariable;
        }

        if (!MetricsEnv.TryParsePort(portText, out int port, out string? portError))
        {
            error.WriteLine($"[metrics] ignoring {portVariable}: {portError}");
            port = 0;
        }

        if (summarySeconds <= 0 && port == 0)
            return null;

        var collector = new MetricsCollector();
        var host = new FineageMetricsHost(collector, MetricsText.Totals(collector.Snapshot()));
        if (port > 0 && !host.TryListen(port, portVariable, output, error))
            port = 0;

        if (summarySeconds > 0)
        {
            output.WriteLine($"[metrics] summary every {summarySeconds}s");
            host._summary = Task.Run(() => host.SummaryLoopAsync(output, TimeSpan.FromSeconds(summarySeconds), host._cts.Token));
        }

        if (host._http == null && host._summary == null)
        {
            collector.Dispose();
            host._cts.Dispose();
            return null;
        }

        return host;
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        if (_summary != null)
        {
            try
            {
                await _summary.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
            {
            }
        }

        if (_http != null)
            await _http.DisposeAsync().ConfigureAwait(false);
        _collector.Dispose();
        _cts.Dispose();
    }

    bool TryListen(int port, string portVariable, TextWriter output, TextWriter error)
    {
        string? bindText = Environment.GetEnvironmentVariable(MetricsEnv.BindVariable);
        if (string.IsNullOrWhiteSpace(bindText))
            bindText = "127.0.0.1";
        if (!IPAddress.TryParse(bindText, out IPAddress? bind))
        {
            error.WriteLine($"[metrics] ignoring {portVariable}: invalid {MetricsEnv.BindVariable}");
            return false;
        }

        try
        {
            _http = PrometheusEndpoint.Start(bind, port, Scrape);
            output.WriteLine($"[metrics] prometheus http://{bind}:{_http.Port}/metrics");
            return true;
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            error.WriteLine($"[metrics] {portVariable} {port} failed: {ex.Message}");
            return false;
        }
    }

    string Scrape()
    {
        lock (_scrapeGate)
        {
            MetricsSnapshot snapshot = _collector.Snapshot();
            TrafficTotals current = MetricsText.Totals(snapshot);
            double elapsed = _scrapeClock.Elapsed.TotalSeconds;
            _scrapeClock.Restart();
            TrafficTotals previous = _previousScrape;
            _previousScrape = current;
            double inbound = MetricsText.Rate(current.FramesIn, previous.FramesIn, elapsed);
            double outbound = MetricsText.Rate(current.FramesOut, previous.FramesOut, elapsed);
            FineageTelemetry.SetPacketRates(inbound, outbound);
            return MetricsText.Prometheus(snapshot, inbound, outbound);
        }
    }

    async Task SummaryLoopAsync(TextWriter output, TimeSpan interval, CancellationToken ct)
    {
        TrafficTotals previous = MetricsText.Totals(_collector.Snapshot());
        using var timer = new PeriodicTimer(interval);
        var clock = Stopwatch.StartNew();
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                double elapsed = clock.Elapsed.TotalSeconds;
                clock.Restart();
                MetricsSnapshot snapshot = _collector.Snapshot();
                TrafficTotals current = MetricsText.Totals(snapshot);
                double inbound = MetricsText.Rate(current.FramesIn, previous.FramesIn, elapsed);
                double outbound = MetricsText.Rate(current.FramesOut, previous.FramesOut, elapsed);
                FineageTelemetry.SetPacketRates(inbound, outbound);
                output.WriteLine(MetricsText.FormatSummary(previous, current, elapsed));
                previous = current;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
