using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Fineage.Protocol.Net;
using Fineage.Protocol.Telemetry;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class MetricsSummaryTests
{
    [Fact]
    public void Summary_line_uses_interval_deltas_and_current_gauges()
    {
        var previous = new TrafficTotals(0, 0, 60, 0, 1000, 0, 4, 0, 10, 10, 10, 10);
        var current = new TrafficTotals(4, 2, 180, 360, 7000, 13000, 7, 1, 70, 40, 30, 20);
        CultureInfo saved = CultureInfo.CurrentCulture;
        CultureInfo culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberDecimalSeparator = ",";
        CultureInfo.CurrentCulture = culture;
        try
        {
            string line = MetricsText.FormatSummary(previous, current, 60);
            Assert.Equal(
                "fineage conn=4 online=2 in_pps=2.0 out_pps=6.0 bytes_in=6000 bytes_out=13000 tick_ms_avg=2.00 handler_ms_avg=2.00 rejected=3 errored=1",
                line);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Summary_line_clamps_a_zero_window_and_negative_deltas()
    {
        var previous = new TrafficTotals(2, 1, 50, 40, 500, 800, 3, 2, 20, 4, 9, 3);
        var current = new TrafficTotals(1, 0, 10, 5, 100, 50, 1, 0, 1, 1, 1, 1);
        string line = MetricsText.FormatSummary(previous, current, 0);
        Assert.Equal(
            "fineage conn=1 online=0 in_pps=0.0 out_pps=0.0 bytes_in=0 bytes_out=0 tick_ms_avg=0.00 handler_ms_avg=0.00 rejected=0 errored=0",
            line);
    }

    [Fact]
    public void Prometheus_text_includes_counters_histogram_and_rates()
    {
        var snapshot = new MetricsSnapshot
        {
            PlayersOnline = 2,
            Tick = new HistogramMetrics { Sum = 4.5, Count = 2, CumulativeBuckets = [0, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2] },
            Endpoints =
            [
                new EndpointMetrics
                {
                    Endpoint = "zone",
                    Connections = 4,
                    FramesIn = 10,
                    FramesOut = 12,
                    BytesIn = 100,
                    BytesOut = 200,
                    PacketsRejected = 3,
                    PacketsErrored = 1,
                    ConnectionsRejected = 2,
                    OutboundDropped = 1,
                    Handler = new HistogramMetrics
                    {
                        Sum = 13.5,
                        Count = 2,
                        CumulativeBuckets = [0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2]
                    }
                }
            ]
        };

        string text = MetricsText.Prometheus(snapshot, 1.5, 2.5);
        Assert.Contains("fineage_connections{endpoint=\"zone\"} 4\n", text);
        Assert.Contains("fineage_frames_in_total{endpoint=\"zone\"} 10\n", text);
        Assert.Contains("fineage_frames_out_total{endpoint=\"zone\"} 12\n", text);
        Assert.Contains("fineage_bytes_in_total{endpoint=\"zone\"} 100\n", text);
        Assert.Contains("fineage_bytes_out_total{endpoint=\"zone\"} 200\n", text);
        Assert.Contains("fineage_packets_rejected_total{endpoint=\"zone\"} 3\n", text);
        Assert.Contains("fineage_packets_errored_total{endpoint=\"zone\"} 1\n", text);
        Assert.Contains("fineage_connections_rejected_total{endpoint=\"zone\"} 2\n", text);
        Assert.Contains("fineage_outbound_dropped_total{endpoint=\"zone\"} 1\n", text);
        Assert.Contains("fineage_packets_per_second{direction=\"in\"} 1.5\n", text);
        Assert.Contains("fineage_packets_per_second{direction=\"out\"} 2.5\n", text);
        Assert.Contains("fineage_handler_ms_bucket{endpoint=\"zone\",le=\"1\"} 0\n", text);
        Assert.Contains("fineage_handler_ms_bucket{endpoint=\"zone\",le=\"2\"} 1\n", text);
        Assert.Contains("fineage_handler_ms_bucket{endpoint=\"zone\",le=\"+Inf\"} 2\n", text);
        Assert.Contains("fineage_handler_ms_sum{endpoint=\"zone\"} 13.5\n", text);
        Assert.Contains("fineage_handler_ms_count{endpoint=\"zone\"} 2\n", text);
        Assert.Contains("fineage_tick_ms_sum 4.5\n", text);
        Assert.Contains("fineage_tick_ms_count 2\n", text);
        Assert.Contains("fineage_zone_online 2\n", text);
        Assert.Contains("# TYPE fineage_frames_in_total counter\n", text);
        Assert.Contains("# TYPE fineage_handler_ms histogram\n", text);
    }

    [Theory]
    [InlineData(null, 60)]
    [InlineData("", 60)]
    [InlineData("15", 15)]
    [InlineData("0", 0)]
    [InlineData("off", 0)]
    [InlineData("disabled", 0)]
    [InlineData("abc", 60)]
    public void Summary_interval_parses_env_text(string? text, int expected) =>
        Assert.Equal(expected, MetricsEnv.ParseSummarySeconds(text));

    [Fact]
    public void Metrics_port_disabled_by_default_and_rejects_garbage()
    {
        Assert.True(MetricsEnv.TryParsePort(null, out int unset, out string? unsetError));
        Assert.Equal(0, unset);
        Assert.Null(unsetError);

        Assert.True(MetricsEnv.TryParsePort("0", out int zero, out _));
        Assert.Equal(0, zero);
        Assert.True(MetricsEnv.TryParsePort("off", out int off, out _));
        Assert.Equal(0, off);

        Assert.True(MetricsEnv.TryParsePort("9465", out int port, out string? portError));
        Assert.Equal(9465, port);
        Assert.Null(portError);

        Assert.False(MetricsEnv.TryParsePort("nope", out _, out string? error));
        Assert.False(string.IsNullOrEmpty(error));
        Assert.Equal("FINEAGE_ZONE_METRICS_PORT", MetricsEnv.RolePortVariable("zone"));
        Assert.Equal("FINEAGE_LOGIN_METRICS_PORT", MetricsEnv.RolePortVariable("login"));
    }

    [Theory]
    [InlineData("GET /metrics HTTP/1.1", true)]
    [InlineData("GET /metrics/ HTTP/1.1", true)]
    [InlineData("GET /metrics?x=1 HTTP/1.1", true)]
    [InlineData("GET /health HTTP/1.1", false)]
    [InlineData("POST /metrics HTTP/1.1", false)]
    public void Prometheus_request_line_matches_metrics_path(string request, bool expected) =>
        Assert.Equal(expected, PrometheusEndpoint.IsMetricsGet(request));
}

public sealed class MetricsCollectorTests
{
    [Fact]
    public void Collector_reads_packet_byte_and_handler_counters()
    {
        string endpoint = "metrics" + Guid.NewGuid().ToString("N");
        using var collector = new MetricsCollector();
        PipelineTelemetry.FrameIn(endpoint, 48);
        PipelineTelemetry.FrameOut(endpoint, 16);
        PipelineTelemetry.PacketRejected(endpoint);
        PipelineTelemetry.PacketErrored(endpoint);
        PipelineTelemetry.Handler(endpoint, 1.5);
        FineageTelemetry.Connections.Add(1, new KeyValuePair<string, object?>("endpoint", endpoint));

        MetricsSnapshot snapshot = collector.Snapshot();
        EndpointMetrics row = Assert.Single(snapshot.Endpoints, item => item.Endpoint == endpoint);
        Assert.Equal(1, row.FramesIn);
        Assert.Equal(48, row.BytesIn);
        Assert.Equal(1, row.FramesOut);
        Assert.Equal(16, row.BytesOut);
        Assert.Equal(1, row.PacketsRejected);
        Assert.Equal(1, row.PacketsErrored);
        Assert.Equal(1, row.Connections);
        Assert.Equal(1, row.Handler.Count);
        Assert.Equal(1.5, row.Handler.Sum, 3);
        Assert.Equal(0, row.Handler.CumulativeBuckets[1]);
        Assert.Equal(1, row.Handler.CumulativeBuckets[2]);
        Assert.Equal(1, row.Handler.CumulativeBuckets[^1]);

        FineageTelemetry.Connections.Add(-1, new KeyValuePair<string, object?>("endpoint", endpoint));
    }

    [Fact]
    public void Tick_histogram_records_duration()
    {
        using var collector = new MetricsCollector();
        FineageTelemetry.TickMilliseconds.Record(7.25);
        MetricsSnapshot snapshot = collector.Snapshot();
        Assert.True(snapshot.Tick.Count >= 1);
        Assert.True(snapshot.Tick.Sum >= 7.25);
    }
}

public sealed class PrometheusEndpointTests
{
    [Fact]
    public async Task Metrics_port_serves_text_and_rejects_other_paths()
    {
        await using PrometheusEndpoint endpoint = PrometheusEndpoint.Start(
            IPAddress.Loopback,
            0,
            () => "fineage_connections{endpoint=\"zone\"} 1\n");

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using HttpResponseMessage ok = await client.GetAsync($"http://127.0.0.1:{endpoint.Port}/metrics");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("text/plain", ok.Content.Headers.ContentType?.MediaType);
        Assert.Contains("0.0.4", ok.Content.Headers.ContentType?.ToString());
        Assert.Contains("fineage_connections{endpoint=\"zone\"} 1", await ok.Content.ReadAsStringAsync());

        using HttpResponseMessage missing = await client.GetAsync($"http://127.0.0.1:{endpoint.Port}/health");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
