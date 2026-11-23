using System.Text.RegularExpressions;
using Fineage.LoadTest.Metrics;

namespace Fineage.LoadTest.Reporting;

public sealed class MetricLatencyRow
{
    public required string Name { get; init; }
    public string Source { get; init; } = "prometheus";
    public double P50Ms { get; init; }
    public double P95Ms { get; init; }
    public double P99Ms { get; init; }
    public double P999Ms { get; init; }
    public double MaxMs { get; init; }
    public long Count { get; init; }
}

public static class LatencyTableBuilder
{
    static readonly string[] PreferredStageOrder =
    [
        "commands",
        "movement.player",
        "movement.npc",
        "regen.players",
        "regen.npcs",
        "abnormals",
        "aggression",
        "random-walk"
    ];

    public static List<MetricLatencyRow> FromPeakMetrics(string? peakMetrics, string? zoneLogPath)
    {
        var rows = new List<MetricLatencyRow>();
        if (peakMetrics != null)
        {
            rows.Add(RowFromHistogram(peakMetrics, "fineage_tick_ms", "tick (histogram)"));
            if (PrometheusParser.Parse(peakMetrics).TickTrueMaxMs > 0)
            {
                rows.Add(new MetricLatencyRow
                {
                    Name = "tick_true_max",
                    Source = "gauge fineage_tick_max_ms",
                    MaxMs = PrometheusParser.Parse(peakMetrics).TickTrueMaxMs
                });
            }

            foreach (string label in new[]
                     {
                         "submit_to_dequeue",
                         "dequeue_to_apply",
                         "apply_to_seal",
                         "seal_to_fanout_enqueue",
                         "fanout_enqueue_to_mailbox",
                         "submit_to_mailbox",
                         "mailbox_to_socket_write"
                     })
            {
                if (PrometheusParser.ParseLabeledHistogram(peakMetrics, "fineage_command_timeline_ms", "phase", label) is { } tl)
                    rows.Add(FromStats($"command.timeline/{label}", tl));
            }

            foreach (string metric in new[]
                     {
                         "fineage_command_to_mailboxes_ms",
                         "fineage_fanout_dispatcher_enqueue_wait_ms",
                         "fineage_fanout_stripe_enqueue_wait_ms",
                         "fineage_fanout_batch_process_ms"
                     })
            {
                if (PrometheusParser.ParseSingletonHistogram(peakMetrics, metric) is { } hs)
                    rows.Add(FromStats(metric.Replace("fineage_", "", StringComparison.Ordinal), hs));
            }

            if (PrometheusParser.ParseSingletonHistogram(peakMetrics, "fineage_command_queue_depth") is { } depth)
                rows.Add(FromStats("command_queue_depth", depth));
            if (PrometheusParser.ParseSingletonHistogram(peakMetrics, "fineage_command_queue_age_ms") is { } age)
                rows.Add(FromStats("command_queue_age", age));
            if (PrometheusParser.ParseSingletonHistogram(peakMetrics, "fineage_fanout_queue_oldest_age_ms") is { } fanoutAge)
                rows.Add(FromStats("fanout_queue_oldest_age", fanoutAge));

            foreach (string stage in PreferredStageOrder)
            {
                if (PrometheusParser.ParseLabeledHistogram(peakMetrics, "fineage_tick_stage_ms", "stage", stage) is { } hs)
                    rows.Add(FromStats($"tick_stage/{stage}", hs));
            }

            foreach (Match match in Regex.Matches(
                         peakMetrics,
                         @"fineage_tick_stage_ms_count\{stage=""([^""]+)""\}\s+(\d+)",
                         RegexOptions.Multiline))
            {
                string stage = match.Groups[1].Value;
                if (PreferredStageOrder.Contains(stage, StringComparer.Ordinal))
                    continue;
                if (PrometheusParser.ParseLabeledHistogram(peakMetrics, "fineage_tick_stage_ms", "stage", stage) is { } extra)
                    rows.Add(FromStats($"tick_stage/{stage}", extra));
            }

            string[] singletons =
            [
                "fineage_enter_try_ms",
                "fineage_enter_plan_ms",
                "fineage_enter_self_projection_ms",
                "fineage_enter_known_encode_ms",
                "fineage_enter_peer_fanout_ms",
                "fineage_outbound_queue_oldest_age_ms",
                "fineage_socket_write_ms"
            ];
            foreach (string metric in singletons)
            {
                if (PrometheusParser.ParseSingletonHistogram(peakMetrics, metric) is { } hs)
                    rows.Add(FromStats(metric.Replace("fineage_", "", StringComparison.Ordinal), hs));
            }

            if (PrometheusParser.ParseLabeledHistogram(peakMetrics, "fineage_command_apply_ms", "command", "request:enter") is { } enter)
                rows.Add(FromStats("command_apply/request:enter", enter));
        }

        foreach (var (command, maxMs) in ZoneSlowCommandLogParser.WorstCommandMs(zoneLogPath))
        {
            rows.Add(new MetricLatencyRow
            {
                Name = $"zone_log/{command}",
                Source = "zone_log_slow_commands",
                MaxMs = maxMs,
                P99Ms = maxMs,
                P95Ms = maxMs,
                P50Ms = maxMs,
                Count = ZoneSlowCommandLogParser.OccurrenceCount(zoneLogPath, command)
            });
        }

        return rows;
    }

    static MetricLatencyRow RowFromHistogram(string text, string metric, string display)
    {
        var hs = PrometheusParser.ParseSingletonHistogram(text, metric);
        return hs == null
            ? new MetricLatencyRow { Name = display, Source = "prometheus" }
            : FromStats(display, hs);
    }

    static MetricLatencyRow FromStats(string name, HistogramStats hs) => new()
    {
        Name = name,
        Source = "prometheus",
        P50Ms = hs.P50,
        P95Ms = hs.P95,
        P99Ms = hs.P99,
        P999Ms = hs.P999,
        MaxMs = hs.Max,
        Count = hs.Count
    };
}