namespace Fineage.LoadTest.Reporting;

/// <summary>Prometheus names we expect after #867 PR1 / PR #862; report lists any that were absent during the run.</summary>
public static class ExpectedMetricsCatalog
{
    public static readonly string[] TickAndStages =
    [
        "fineage_tick_ms",
        "fineage_tick_stage_ms",
        "fineage_tick_overruns_total",
        "fineage_command_ingress_queued"
    ];

    public static readonly string[] CommandPipeline =
    [
        "fineage_command_apply_ms",
        "fineage_command_timeline_ms",
        "fineage_command_to_mailboxes_ms",
        "fineage_command_queue_depth",
        "fineage_command_queue_age_ms",
        "fineage_command_coalesced_total",
        "fineage_command_rejected_total",
        "fineage_tick_max_ms",
        "fineage_step_watchdog_stalls"
    ];

    public static readonly string[] FanoutAndOutbound =
    [
        "fineage_fanout_queue_depth",
        "fineage_fanout_queue_bytes",
        "fineage_fanout_queue_oldest_age_ms",
        "fineage_fanout_batch_process_ms",
        "fineage_fanout_events_per_batch",
        "fineage_fanout_recipients_per_event",
        "fineage_fanout_overflow_total",
        "fineage_outbound_queue_frames",
        "fineage_outbound_queue_bytes",
        "fineage_outbound_queue_oldest_age_ms",
        "fineage_coalesced_total",
        "fineage_reliable_overflow_total",
        "fineage_socket_write_ms",
        "fineage_socket_write_bytes",
        "fineage_socket_write_frames",
        "fineage_fanout_dropped_total",
        "fineage_outbound_dropped_total"
    ];

    public static readonly string[] EnterAndRequest =
    [
        "fineage_enter_try_ms",
        "fineage_enter_plan_ms",
        "fineage_enter_peer_fanout_ms"
    ];

    public static readonly string[] Persistence =
    [
        "fineage_ledger_adjustments_total",
        "fineage_ledger_flush_failures_total"
    ];

    public static IReadOnlyList<string> All =>
    [
        ..TickAndStages,
        ..CommandPipeline,
        ..FanoutAndOutbound,
        ..EnterAndRequest,
        ..Persistence
    ];

    public static List<string> MissingFrom(string? metricsText)
    {
        if (string.IsNullOrEmpty(metricsText))
            return All.ToList();
        var missing = new List<string>();
        foreach (string name in All)
        {
            if (!metricsText.Contains(name, StringComparison.Ordinal))
                missing.Add(name);
        }

        return missing;
    }
}