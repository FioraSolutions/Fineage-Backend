using System.Globalization;
using System.Text;

namespace Fineage.Protocol.Telemetry;

public readonly record struct TrafficTotals(
    long Connections,
    int PlayersOnline,
    long FramesIn,
    long FramesOut,
    long BytesIn,
    long BytesOut,
    long PacketsRejected,
    long PacketsErrored,
    double HandlerMsSum,
    long HandlerCount,
    double TickMsSum,
    long TickCount);

public sealed class HistogramMetrics
{
    public double Sum { get; init; }
    public long Count { get; init; }
    public long[] CumulativeBuckets { get; init; } = [];
}

public sealed class EndpointMetrics
{
    public required string Endpoint { get; init; }
    public long Connections { get; init; }
    public long FramesIn { get; init; }
    public long FramesOut { get; init; }
    public long BytesIn { get; init; }
    public long BytesOut { get; init; }
    public long PacketsRejected { get; init; }
    public long PacketsErrored { get; init; }
    public long ConnectionsRejected { get; init; }
    public long OutboundDropped { get; init; }
    public HistogramMetrics Handler { get; init; } = new();
    public HistogramMetrics SocketWriteMs { get; init; } = new();
    public HistogramMetrics SocketWriteBytes { get; init; } = new();
    public HistogramMetrics SocketWriteFrames { get; init; } = new();
}

public sealed class StageMetrics
{
    public required string Stage { get; init; }
    public HistogramMetrics Duration { get; init; } = new();
    public long Failures { get; init; }
}

public sealed class MetricsSnapshot
{
    public int PlayersOnline { get; init; }
    public int CommandIngressQueued { get; init; }
    public HistogramMetrics Tick { get; init; } = new();
    public HistogramMetrics EnterTry { get; init; } = new();
    public HistogramMetrics EnterTryAoiBind { get; init; } = new();
    public HistogramMetrics EnterPlan { get; init; } = new();
    public HistogramMetrics EnterSelf { get; init; } = new();
    public HistogramMetrics EnterKnownSnapshot { get; init; } = new();
    public HistogramMetrics EnterKnownEncode { get; init; } = new();
    public HistogramMetrics EnterPeerFanout { get; init; } = new();
    public HistogramMetrics EnterKnownEntities { get; init; } = new();
    public HistogramMetrics EnterKnownPlayers { get; init; } = new();
    public HistogramMetrics EnterFrames { get; init; } = new();
    public HistogramMetrics EnterBytes { get; init; } = new();
    public HistogramMetrics TickStepFanoutDrain { get; init; } = new();
    public HistogramMetrics CommandToMailboxes { get; init; } = new();
    public HistogramMetrics FanoutBatchProcess { get; init; } = new();
    public HistogramMetrics FanoutQueueOldestAge { get; init; } = new();
    public HistogramMetrics FanoutQueueDepth { get; init; } = new();
    public HistogramMetrics FanoutEventsPerBatch { get; init; } = new();
    public HistogramMetrics FanoutRecipientsPerEvent { get; init; } = new();
    public HistogramMetrics FanoutQueueBytes { get; init; } = new();
    public HistogramMetrics OutboundQueueFrames { get; init; } = new();
    public HistogramMetrics OutboundQueueBytes { get; init; } = new();
    public HistogramMetrics OutboundQueueOldestAge { get; init; } = new();
    public long FanoutOverflow { get; init; }
    public long FanoutCoalesced { get; init; }
    public long ReliableOverflow { get; init; }
    public long TickOverruns { get; init; }
    public long TicksSkipped { get; init; }
    public long RegionHandoffs { get; init; }
    public IReadOnlyDictionary<string, long> LedgerAdjustments { get; init; } =
        new Dictionary<string, long>(StringComparer.Ordinal);
    public long LedgerFlushFailures { get; init; }
    public int PersistPending { get; init; }
    public double PersistOldestAgeMs { get; init; }
    public IReadOnlyDictionary<string, long> PersistSnapshots { get; init; } =
        new Dictionary<string, long>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, long> FanoutDropped { get; init; } =
        new Dictionary<string, long>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, long> OutboundLaneBlocked { get; init; } =
        new Dictionary<string, long>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, long> OutboundLaneFull { get; init; } =
        new Dictionary<string, long>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, long> OutboundLaneCoalesced { get; init; } =
        new Dictionary<string, long>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, long> OutboundLaneEvicted { get; init; } =
        new Dictionary<string, long>(StringComparer.Ordinal);
    public IReadOnlyList<StageMetrics> Stages { get; init; } = [];
    public IReadOnlyList<StageMetrics> CommandTimelinePhases { get; init; } = [];
    public IReadOnlyList<StageMetrics> CommandApplyByName { get; init; } = [];
    public IReadOnlyList<StageMetrics> CommandQueueWaitByClass { get; init; } = [];
    public IReadOnlyList<EndpointMetrics> Endpoints { get; init; } = [];
}

public static class MetricsText
{
    public static readonly double[] DurationBoundsMs =
    [
        0.5, 1, 2, 5, 10, 20, 25, 40, 50, 60, 80, 100, 120, 150, 180, 200, 220, 250, 300, 400, 500, 650, 750, 800,
        1000, 1500, 2000, 3000, 5000, 10000
    ];

    public static readonly double[] CountBounds =
        [0, 1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192];

    public static readonly double[] SocketWriteFrameBounds =
        [1, 2, 3, 4, 5, 6, 7, 8, 10, 12, 16, 24, 32, 48, 64];

    public static readonly double[] SocketWriteByteBounds =
        [128, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536];

    public static TrafficTotals Totals(MetricsSnapshot snapshot)
    {
        long connections = 0;
        long framesIn = 0;
        long framesOut = 0;
        long bytesIn = 0;
        long bytesOut = 0;
        long rejected = 0;
        long errored = 0;
        double handlerSum = 0;
        long handlerCount = 0;
        foreach (EndpointMetrics endpoint in snapshot.Endpoints)
        {
            connections += endpoint.Connections;
            framesIn += endpoint.FramesIn;
            framesOut += endpoint.FramesOut;
            bytesIn += endpoint.BytesIn;
            bytesOut += endpoint.BytesOut;
            rejected += endpoint.PacketsRejected;
            errored += endpoint.PacketsErrored;
            handlerSum += endpoint.Handler.Sum;
            handlerCount += endpoint.Handler.Count;
        }

        return new TrafficTotals(
            connections,
            snapshot.PlayersOnline,
            framesIn,
            framesOut,
            bytesIn,
            bytesOut,
            rejected,
            errored,
            handlerSum,
            handlerCount,
            snapshot.Tick.Sum,
            snapshot.Tick.Count);
    }

    public static string FormatSummary(TrafficTotals previous, TrafficTotals current, double elapsedSeconds)
    {
        double inPps = Rate(current.FramesIn, previous.FramesIn, elapsedSeconds);
        double outPps = Rate(current.FramesOut, previous.FramesOut, elapsedSeconds);
        double tickAvg = Average(current.TickMsSum - previous.TickMsSum, current.TickCount - previous.TickCount);
        double handlerAvg = Average(current.HandlerMsSum - previous.HandlerMsSum, current.HandlerCount - previous.HandlerCount);
        return FormattableString.Invariant(
            $"fineage conn={current.Connections} online={current.PlayersOnline} in_pps={inPps:0.0} out_pps={outPps:0.0} bytes_in={Delta(current.BytesIn, previous.BytesIn)} bytes_out={Delta(current.BytesOut, previous.BytesOut)} tick_ms_avg={tickAvg:0.00} handler_ms_avg={handlerAvg:0.00} rejected={Delta(current.PacketsRejected, previous.PacketsRejected)} errored={Delta(current.PacketsErrored, previous.PacketsErrored)}");
    }

    public static double Rate(long current, long previous, double elapsedSeconds)
    {
        if (elapsedSeconds <= 0)
            return 0;
        long delta = current - previous;
        if (delta < 0)
            delta = 0;
        return delta / elapsedSeconds;
    }

    public static long Delta(long current, long previous)
    {
        long delta = current - previous;
        return delta < 0 ? 0 : delta;
    }

    public static double Average(double sumDelta, long countDelta)
    {
        if (countDelta <= 0 || sumDelta < 0 || double.IsNaN(sumDelta))
            return 0;
        return sumDelta / countDelta;
    }

    public static string Prometheus(MetricsSnapshot snapshot, double inPacketsPerSecond, double outPacketsPerSecond)
    {
        var sb = new StringBuilder(2048);
        AppendGauge(sb, "fineage_connections", "Open TCP sessions", snapshot.Endpoints, static e => e.Connections);
        AppendCounter(sb, "fineage_connections_rejected_total", "Accepts dropped because the process was at its connection cap", snapshot.Endpoints, static e => e.ConnectionsRejected);
        AppendCounter(sb, "fineage_frames_in_total", "Decoded inbound packets", snapshot.Endpoints, static e => e.FramesIn);
        AppendCounter(sb, "fineage_frames_out_total", "Outbound packets written to a session", snapshot.Endpoints, static e => e.FramesOut);
        AppendCounter(sb, "fineage_bytes_in_total", "Inbound packet bytes, including the frame header", snapshot.Endpoints, static e => e.BytesIn);
        AppendCounter(sb, "fineage_bytes_out_total", "Outbound packet bytes, including the frame header", snapshot.Endpoints, static e => e.BytesOut);
        AppendCounter(sb, "fineage_outbound_dropped_total", "Frames dropped because a session outbound queue was full", snapshot.Endpoints, static e => e.OutboundDropped);
        if (snapshot.FanoutDropped.Count > 0)
        {
            AppendHelp(sb, "fineage_fanout_dropped_total", "Fanout frames dropped under stripe/outbound backpressure");
            sb.Append("# TYPE fineage_fanout_dropped_total counter\n");
            foreach (KeyValuePair<string, long> pair in snapshot.FanoutDropped)
            {
                sb.Append("fineage_fanout_dropped_total{opcode_group=\"")
                    .Append(Escape(pair.Key))
                    .Append("\"} ")
                    .Append(pair.Value.ToString(CultureInfo.InvariantCulture))
                    .Append('\n');
            }
        }
        else
        {
            AppendCounterLine(sb, "fineage_fanout_dropped_total",
                "Fanout frames dropped under stripe/outbound backpressure", 0);
        }

        AppendCounter(sb, "fineage_packets_rejected_total", "Packets refused before normal handling", snapshot.Endpoints, static e => e.PacketsRejected);
        AppendCounter(sb, "fineage_packets_errored_total", "Packets that produced an error response or failed while handling", snapshot.Endpoints, static e => e.PacketsErrored);
        AppendHelp(sb, "fineage_packets_per_second", "Packet rate over the most recent summary or scrape window");
        sb.Append("# TYPE fineage_packets_per_second gauge\n");
        sb.Append("fineage_packets_per_second{direction=\"in\"} ").Append(Num(inPacketsPerSecond)).Append('\n');
        sb.Append("fineage_packets_per_second{direction=\"out\"} ").Append(Num(outPacketsPerSecond)).Append('\n');
        AppendHistogram(sb, "fineage_handler_ms", "Packet handler duration", snapshot.Endpoints, static e => e.Handler, DurationBoundsMs);
        AppendHistogram(sb, "fineage_socket_write_ms", "TCP socket write duration for one outbound frame batch",
            snapshot.Endpoints, static e => e.SocketWriteMs, DurationBoundsMs);
        AppendHistogram(sb, "fineage_socket_write_bytes", "Bytes written in one socket write",
            snapshot.Endpoints, static e => e.SocketWriteBytes, SocketWriteByteBounds);
        AppendHistogram(sb, "fineage_socket_write_frames", "Frames combined in one socket write",
            snapshot.Endpoints, static e => e.SocketWriteFrames, SocketWriteFrameBounds);
        AppendHelp(sb, "fineage_tick_ms", "Zone simulation tick duration");
        sb.Append("# TYPE fineage_tick_ms histogram\n");
        AppendHistogramSamples(sb, "fineage_tick_ms", "", snapshot.Tick, DurationBoundsMs);
        AppendSingletonHistogram(sb, "fineage_tick_step_fanout_drain_ms",
            "Fan-out batch execution duration on the dispatcher worker", snapshot.TickStepFanoutDrain);
        AppendSingletonHistogram(sb, "fineage_command_to_mailboxes_ms",
            "Command commit to first outbound mailbox enqueue in a fan-out batch", snapshot.CommandToMailboxes);
        AppendSingletonHistogram(sb, "fineage_fanout_batch_process_ms",
            "Wall time to execute one sealed fan-out batch on the dispatcher worker", snapshot.FanoutBatchProcess);
        AppendSingletonHistogram(sb, "fineage_fanout_queue_oldest_age_ms",
            "Age of the oldest batch waiting in the fan-out dispatcher queue", snapshot.FanoutQueueOldestAge);
        AppendSingletonHistogram(sb, "fineage_fanout_queue_depth",
            "Depth of the fan-out dispatcher batch queue at sample time", snapshot.FanoutQueueDepth, CountBounds);
        AppendSingletonHistogram(sb, "fineage_fanout_events_per_batch",
            "Send actions executed per sealed batch", snapshot.FanoutEventsPerBatch, CountBounds);
        AppendSingletonHistogram(sb, "fineage_fanout_recipients_per_event",
            "Mailbox enqueues performed by one fan-out send", snapshot.FanoutRecipientsPerEvent, CountBounds);
        AppendSingletonHistogram(sb, "fineage_fanout_queue_bytes",
            "Encoded payload bytes in a sealed batch when it leaves the dispatcher queue", snapshot.FanoutQueueBytes, SocketWriteByteBounds);
        AppendSingletonHistogram(sb, "fineage_outbound_queue_frames",
            "Frames waiting on a connection when another frame is queued", snapshot.OutboundQueueFrames, SocketWriteFrameBounds);
        AppendSingletonHistogram(sb, "fineage_outbound_queue_bytes",
            "Bytes waiting on a connection when another frame is queued", snapshot.OutboundQueueBytes, SocketWriteByteBounds);
        AppendSingletonHistogram(sb, "fineage_outbound_queue_oldest_age_ms",
            "Age of the oldest frame included in one socket write", snapshot.OutboundQueueOldestAge);
        AppendCounterLine(sb, "fineage_fanout_overflow_total",
            "Sealed fan-out batches rejected because the dispatcher is closed", snapshot.FanoutOverflow);
        AppendCounterLine(sb, "fineage_coalesced_total",
            "Replaceable fan-out writes superseded before send", snapshot.FanoutCoalesced);
        AppendCounterLine(sb, "fineage_reliable_overflow_total",
            "Reliable frames refused because a session outbound queue was full", snapshot.ReliableOverflow);
        AppendLane(sb, "fineage_outbound_lane_blocked_total",
            "Producer found a full fan-out stripe and did not wait", snapshot.OutboundLaneBlocked);
        AppendLane(sb, "fineage_outbound_lane_full_total",
            "Mailbox budget refused a frame or snapshot", snapshot.OutboundLaneFull);
        AppendLane(sb, "fineage_outbound_lane_coalesced_total",
            "Movement frame replaced a pending one for the same entity", snapshot.OutboundLaneCoalesced);
        AppendLane(sb, "fineage_outbound_lane_evicted_total",
            "Stale mailbox frame discarded", snapshot.OutboundLaneEvicted);
        AppendHelp(sb, "fineage_tick_overruns_total", "Simulation steps that took longer than the fixed step");
        sb.Append("# TYPE fineage_tick_overruns_total counter\n");
        sb.Append("fineage_tick_overruns_total ").Append(snapshot.TickOverruns.ToString(CultureInfo.InvariantCulture)).Append('\n');
        AppendHelp(sb, "fineage_tick_skipped_total", "Simulation ticks a late step jumped over");
        sb.Append("# TYPE fineage_tick_skipped_total counter\n");
        sb.Append("fineage_tick_skipped_total ").Append(snapshot.TicksSkipped.ToString(CultureInfo.InvariantCulture)).Append('\n');
        AppendHelp(sb, "fineage_region_handoffs_total", "Entities handed to another region at the end of a step");
        sb.Append("# TYPE fineage_region_handoffs_total counter\n");
        sb.Append("fineage_region_handoffs_total ").Append(snapshot.RegionHandoffs.ToString(CultureInfo.InvariantCulture)).Append('\n');
        if (snapshot.LedgerAdjustments.Count > 0)
        {
            AppendHelp(sb, "fineage_ledger_adjustments_total", "Alignment adjustments before a ledger move");
            sb.Append("# TYPE fineage_ledger_adjustments_total counter\n");
            foreach (var pair in snapshot.LedgerAdjustments)
            {
                sb.Append("fineage_ledger_adjustments_total{move_kind=\"")
                    .Append(Escape(pair.Key))
                    .Append("\"} ")
                    .Append(pair.Value.ToString(CultureInfo.InvariantCulture))
                    .Append('\n');
            }
        }

        if (snapshot.PersistSnapshots.Count > 0)
        {
            AppendHelp(sb, "fineage_persist_snapshots_total", "Non-economic character snapshots");
            sb.Append("# TYPE fineage_persist_snapshots_total counter\n");
            foreach (var pair in snapshot.PersistSnapshots)
            {
                sb.Append("fineage_persist_snapshots_total{result=\"")
                    .Append(Escape(pair.Key))
                    .Append("\"} ")
                    .Append(pair.Value.ToString(CultureInfo.InvariantCulture))
                    .Append('\n');
            }
        }

        AppendHelp(sb, "fineage_ledger_flush_failures_total", "Auditable economy ledger flush failures");
        sb.Append("# TYPE fineage_ledger_flush_failures_total counter\n");
        sb.Append("fineage_ledger_flush_failures_total ")
            .Append(snapshot.LedgerFlushFailures.ToString(CultureInfo.InvariantCulture))
            .Append('\n');

        AppendLabeledStageHistograms(sb, "fineage_command_timeline_ms", "phase",
            "Elapsed time between command lifecycle phases", snapshot.CommandTimelinePhases);
        AppendLabeledStageHistograms(sb, "fineage_command_apply_ms", "command",
            "Single command apply duration", snapshot.CommandApplyByName);
        AppendLabeledStageHistograms(sb, "fineage_command_queue_wait_ms", "class",
            "Ingress queue wait before dequeue", snapshot.CommandQueueWaitByClass);
        AppendStages(sb, snapshot.Stages);
        AppendEnterHistograms(sb, snapshot);
        AppendHelp(sb, "fineage_zone_online", "Players currently in a zone simulation");
        sb.Append("# TYPE fineage_zone_online gauge\n");
        sb.Append("fineage_zone_online ").Append(snapshot.PlayersOnline.ToString(CultureInfo.InvariantCulture)).Append('\n');
        AppendHelp(sb, "fineage_command_ingress_queued", "Player commands waiting for the next simulation drain");
        sb.Append("# TYPE fineage_command_ingress_queued gauge\n");
        sb.Append("fineage_command_ingress_queued ").Append(snapshot.CommandIngressQueued.ToString(CultureInfo.InvariantCulture)).Append('\n');
        AppendHelp(sb, "fineage_persist_pending", "Character and pet snapshots waiting to be written, including the one in flight");
        sb.Append("# TYPE fineage_persist_pending gauge\n");
        sb.Append("fineage_persist_pending ").Append(snapshot.PersistPending.ToString(CultureInfo.InvariantCulture)).Append('\n');
        AppendHelp(sb, "fineage_persist_oldest_age_ms", "Age of the oldest character or pet snapshot that has not been written");
        sb.Append("# TYPE fineage_persist_oldest_age_ms gauge\n");
        sb.Append("fineage_persist_oldest_age_ms ").Append(Num(snapshot.PersistOldestAgeMs)).Append('\n');
        return sb.ToString();
    }

    static void AppendGauge(StringBuilder sb, string name, string help, IReadOnlyList<EndpointMetrics> endpoints, Func<EndpointMetrics, long> value)
    {
        if (!HasEndpoint(endpoints))
            return;
        AppendHelp(sb, name, help);
        sb.Append("# TYPE ").Append(name).Append(" gauge\n");
        foreach (EndpointMetrics endpoint in endpoints)
            sb.Append(name).Append("{endpoint=\"").Append(Escape(endpoint.Endpoint)).Append("\"} ")
                .Append(value(endpoint).ToString(CultureInfo.InvariantCulture)).Append('\n');
    }

    static void AppendCounter(StringBuilder sb, string name, string help, IReadOnlyList<EndpointMetrics> endpoints, Func<EndpointMetrics, long> value)
    {
        if (!HasEndpoint(endpoints))
            return;
        AppendHelp(sb, name, help);
        sb.Append("# TYPE ").Append(name).Append(" counter\n");
        foreach (EndpointMetrics endpoint in endpoints)
            sb.Append(name).Append("{endpoint=\"").Append(Escape(endpoint.Endpoint)).Append("\"} ")
                .Append(value(endpoint).ToString(CultureInfo.InvariantCulture)).Append('\n');
    }

    static void AppendHistogram(
        StringBuilder sb,
        string name,
        string help,
        IReadOnlyList<EndpointMetrics> endpoints,
        Func<EndpointMetrics, HistogramMetrics> histogram,
        double[] bounds)
    {
        if (!HasEndpoint(endpoints))
            return;
        bool any = false;
        foreach (EndpointMetrics endpoint in endpoints)
        {
            if (histogram(endpoint).Count > 0)
            {
                any = true;
                break;
            }
        }

        if (!any)
            return;
        AppendHelp(sb, name, help);
        sb.Append("# TYPE ").Append(name).Append(" histogram\n");
        foreach (EndpointMetrics endpoint in endpoints)
        {
            HistogramMetrics h = histogram(endpoint);
            if (h.Count == 0)
                continue;
            string labels = "endpoint=\"" + Escape(endpoint.Endpoint) + "\"";
            AppendHistogramSamples(sb, name, labels, h, bounds);
        }
    }

    static void AppendEnterHistograms(StringBuilder sb, MetricsSnapshot snapshot)
    {
        AppendSingletonHistogram(sb, "fineage_enter_try_ms", "TryEnter world join on the simulation thread", snapshot.EnterTry);
        AppendSingletonHistogram(sb, "fineage_enter_try_aoi_bind_ms", "TryEnter knownlist AOI bind on the simulation thread", snapshot.EnterTryAoiBind);
        AppendSingletonHistogram(sb, "fineage_enter_plan_ms", "Enter commit planning on the simulation thread", snapshot.EnterPlan);
        AppendSingletonHistogram(sb, "fineage_enter_self_projection_ms", "Enter self packet projection on the simulation thread", snapshot.EnterSelf);
        AppendSingletonHistogram(sb, "fineage_enter_known_snapshot_ms", "Enter knownlist snapshot on the simulation thread", snapshot.EnterKnownSnapshot);
        AppendSingletonHistogram(sb, "fineage_enter_known_encode_ms", "Enter knownlist NetCodec work off the simulation thread", snapshot.EnterKnownEncode);
        AppendSingletonHistogram(sb, "fineage_enter_peer_fanout_ms", "Enter peer fan-out encoding off the simulation thread", snapshot.EnterPeerFanout);
        AppendSingletonHistogram(sb, "fineage_enter_known_entities", "Known entities per enter", snapshot.EnterKnownEntities);
        AppendSingletonHistogram(sb, "fineage_enter_known_players", "Known players per enter", snapshot.EnterKnownPlayers);
        AppendSingletonHistogram(sb, "fineage_enter_frames", "Outbound frames per enter burst", snapshot.EnterFrames);
        AppendSingletonHistogram(sb, "fineage_enter_bytes", "Outbound bytes per enter burst", snapshot.EnterBytes);
    }

    static void AppendSingletonHistogram(StringBuilder sb, string name, string help, HistogramMetrics histogram) =>
        AppendSingletonHistogram(sb, name, help, histogram, DurationBoundsMs);

    static void AppendSingletonHistogram(StringBuilder sb, string name, string help, HistogramMetrics histogram, double[] bounds)
    {
        if (histogram.Count == 0)
            return;
        AppendHelp(sb, name, help);
        sb.Append("# TYPE ").Append(name).Append(" histogram\n");
        AppendHistogramSamples(sb, name, "", histogram, bounds);
    }

    static void AppendCounterLine(StringBuilder sb, string name, string help, long value)
    {
        AppendHelp(sb, name, help);
        sb.Append("# TYPE ").Append(name).Append(" counter\n");
        sb.Append(name).Append(' ').Append(value.ToString(CultureInfo.InvariantCulture)).Append('\n');
    }

    static void AppendLabeledStageHistograms(
        StringBuilder sb,
        string metric,
        string labelName,
        string help,
        IReadOnlyList<StageMetrics> rows)
    {
        if (rows.Count == 0)
            return;
        AppendHelp(sb, metric, help);
        sb.Append("# TYPE ").Append(metric).Append(" histogram\n");
        foreach (StageMetrics row in rows)
            AppendHistogramSamples(sb, metric, $"{labelName}=\"{Escape(row.Stage)}\"", row.Duration, DurationBoundsMs);
    }

    static void AppendStages(StringBuilder sb, IReadOnlyList<StageMetrics> stages)
    {
        if (stages.Count == 0)
            return;
        AppendHelp(sb, "fineage_tick_stage_ms", "Zone simulation stage duration");
        sb.Append("# TYPE fineage_tick_stage_ms histogram\n");
        foreach (StageMetrics stage in stages)
            AppendHistogramSamples(sb, "fineage_tick_stage_ms", "stage=\"" + Escape(stage.Stage) + "\"", stage.Duration, DurationBoundsMs);
        AppendHelp(sb, "fineage_tick_stage_failures_total", "Simulation stage or region bodies that threw");
        sb.Append("# TYPE fineage_tick_stage_failures_total counter\n");
        foreach (StageMetrics stage in stages)
            sb.Append("fineage_tick_stage_failures_total{stage=\"").Append(Escape(stage.Stage)).Append("\"} ")
                .Append(stage.Failures.ToString(CultureInfo.InvariantCulture)).Append('\n');
    }

    static void AppendHistogramSamples(StringBuilder sb, string name, string labels, HistogramMetrics histogram, double[] bounds)
    {
        long[] buckets = histogram.CumulativeBuckets;
        for (int i = 0; i < bounds.Length; i++)
        {
            long cumulative = i < buckets.Length ? buckets[i] : 0;
            sb.Append(name).Append("_bucket{");
            if (labels.Length > 0)
                sb.Append(labels).Append(',');
            sb.Append("le=\"").Append(bounds[i].ToString("0.###", CultureInfo.InvariantCulture)).Append("\"} ")
                .Append(cumulative.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        long inf = buckets.Length > bounds.Length ? buckets[bounds.Length] : (buckets.Length > 0 ? buckets[^1] : 0);
        sb.Append(name).Append("_bucket{");
        if (labels.Length > 0)
            sb.Append(labels).Append(',');
        sb.Append("le=\"+Inf\"} ").Append(inf.ToString(CultureInfo.InvariantCulture)).Append('\n');
        sb.Append(name).Append("_sum");
        AppendBareLabels(sb, labels);
        sb.Append(' ').Append(Num(histogram.Sum)).Append('\n');
        sb.Append(name).Append("_count");
        AppendBareLabels(sb, labels);
        sb.Append(' ').Append(histogram.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
    }

    static void AppendBareLabels(StringBuilder sb, string labels)
    {
        if (labels.Length == 0)
            return;
        sb.Append('{').Append(labels).Append('}');
    }

    static void AppendLane(StringBuilder sb, string name, string help, IReadOnlyDictionary<string, long> counts)
    {
        if (counts.Count == 0)
            return;
        AppendHelp(sb, name, help);
        sb.Append("# TYPE ").Append(name).Append(" counter\n");
        foreach (KeyValuePair<string, long> pair in counts)
        {
            sb.Append(name).Append("{lane=\"").Append(Escape(pair.Key)).Append("\"} ")
                .Append(pair.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }
    }

    static void AppendHelp(StringBuilder sb, string name, string help)
    {
        sb.Append("# HELP ").Append(name).Append(' ').Append(help).Append('\n');
    }

    static bool HasEndpoint(IReadOnlyList<EndpointMetrics> endpoints) => endpoints.Count > 0;

    static string Num(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
            return "0";
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
