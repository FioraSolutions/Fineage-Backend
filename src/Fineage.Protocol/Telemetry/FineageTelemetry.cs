using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Fineage.Protocol.Telemetry;

/// <summary>
/// Process-wide counters and traces. Export by subscribing the OpenTelemetry SDK to
/// meter <see cref="MeterName"/> and activity source <see cref="ActivitySourceName"/>.
/// TCP instruments use tag <c>endpoint</c> (<c>login</c> or <c>zone</c> in production CLI).
/// </summary>
public static class FineageTelemetry
{
    public const string MeterName = "Fineage.Server";
    public const string ActivitySourceName = "Fineage.Server";

    public static readonly ActivitySource Activity = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName, "0.4.0");

    public static readonly UpDownCounter<long> Connections =
        Meter.CreateUpDownCounter<long>("fineage.connections", description: "Open TCP sessions");

    public static readonly Counter<long> ConnectionsRejected =
        Meter.CreateCounter<long>("fineage.connections.rejected", description: "Accepts dropped because the process was at its connection cap");

    public static readonly Counter<long> FramesIn =
        Meter.CreateCounter<long>("fineage.frames.in", description: "Decoded inbound packets");

    public static readonly Counter<long> FramesOut =
        Meter.CreateCounter<long>("fineage.frames.out", description: "Outbound packets written to a session");

    public static readonly Counter<long> BytesIn =
        Meter.CreateCounter<long>("fineage.bytes.in", unit: "By", description: "Inbound packet bytes, including the frame header");

    public static readonly Counter<long> BytesOut =
        Meter.CreateCounter<long>("fineage.bytes.out", unit: "By", description: "Outbound packet bytes, including the frame header");

    public static readonly Counter<long> OutboundDropped =
        Meter.CreateCounter<long>("fineage.outbound.dropped", description: "Frames dropped because a session outbound queue was full");

    public static readonly Counter<long> PacketsRejected =
        Meter.CreateCounter<long>("fineage.packets.rejected", description: "Packets refused before normal handling (rate limit, unknown opcode, wrong port)");

    public static readonly Counter<long> PacketsErrored =
        Meter.CreateCounter<long>("fineage.packets.errored", description: "Packets that produced an error response or failed while handling");

    public static readonly Histogram<double> HandlerMilliseconds =
        Meter.CreateHistogram<double>("fineage.handler.ms", unit: "ms", description: "Packet handler duration");

    public static readonly Histogram<double> TickMilliseconds =
        Meter.CreateHistogram<double>("fineage.tick.ms", unit: "ms", description: "Zone simulation tick duration");

    public static readonly Histogram<double> EnterTryMilliseconds =
        Meter.CreateHistogram<double>("fineage.enter.try_ms", unit: "ms", description: "TryEnter world join on the simulation thread");

    public static readonly Histogram<double> EnterTryAoiBindMilliseconds =
        Meter.CreateHistogram<double>("fineage.enter.try_aoi_bind_ms", unit: "ms", description: "TryEnter knownlist/AOI bind on the simulation thread");

    public static readonly Histogram<double> CommandApplyMilliseconds =
        Meter.CreateHistogram<double>("fineage.command.apply_ms", unit: "ms", description: "Single command apply duration (tag command)");

    public static readonly Histogram<double> EnterPlanMilliseconds =
        Meter.CreateHistogram<double>("fineage.enter.plan_ms", unit: "ms", description: "Enter commit planning on the simulation thread");

    public static readonly Histogram<double> EnterSelfProjectionMilliseconds =
        Meter.CreateHistogram<double>("fineage.enter.self_projection_ms", unit: "ms", description: "Enter self packet projection on the simulation thread");

    public static readonly Histogram<double> EnterKnownSnapshotMilliseconds =
        Meter.CreateHistogram<double>("fineage.enter.known_snapshot_ms", unit: "ms", description: "Enter knownlist snapshot on the simulation thread");

    public static readonly Histogram<double> EnterKnownEncodeMilliseconds =
        Meter.CreateHistogram<double>("fineage.enter.known_encode_ms", unit: "ms", description: "Enter knownlist NetCodec work off the simulation thread");

    public static readonly Histogram<double> EnterPeerFanoutMilliseconds =
        Meter.CreateHistogram<double>("fineage.enter.peer_fanout_ms", unit: "ms", description: "Enter peer fan-out encoding off the simulation thread");

    public static readonly Histogram<double> EnterKnownEntities =
        Meter.CreateHistogram<double>("fineage.enter.known_entities", unit: "{entity}", description: "Known entities per enter");

    public static readonly Histogram<double> EnterKnownPlayers =
        Meter.CreateHistogram<double>("fineage.enter.known_players", unit: "{player}", description: "Known players per enter");

    public static readonly Histogram<double> EnterFrames =
        Meter.CreateHistogram<double>("fineage.enter.frames", unit: "{frame}", description: "Outbound frames per enter burst");

    public static readonly Histogram<double> EnterBytes =
        Meter.CreateHistogram<double>("fineage.enter.bytes", unit: "By", description: "Outbound bytes per enter burst");

    public static readonly Histogram<double> StageMilliseconds =
        Meter.CreateHistogram<double>("fineage.tick.stage.ms", unit: "ms", description: "Zone simulation stage duration (tag stage)");

    public static readonly Histogram<int> StageRegions =
        Meter.CreateHistogram<int>("fineage.tick.stage.regions", unit: "{region}", description: "Regions a simulation stage ran in (tag stage)");

    public static readonly Counter<long> StageFailures =
        Meter.CreateCounter<long>("fineage.tick.stage.failures", description: "Simulation stage or region bodies that threw (tag stage)");

    public static readonly Counter<long> TickOverruns =
        Meter.CreateCounter<long>("fineage.tick.overruns", description: "Simulation steps that took longer than the fixed step");

    public static readonly Counter<long> TicksSkipped =
        Meter.CreateCounter<long>("fineage.tick.skipped", description: "Simulation ticks a late step jumped over");

    public static readonly Counter<long> RegionHandoffs =
        Meter.CreateCounter<long>("fineage.region.handoffs", description: "Entities handed to another region at the end of a step");

    public static readonly Counter<long> LedgerAdjustments =
        Meter.CreateCounter<long>("fineage.ledger.adjustments", description: "Alignment adjustments before a ledger move (tag move_kind)");

    public static readonly Counter<long> LedgerFlushFailures =
        Meter.CreateCounter<long>("fineage.ledger.flush_failures", description: "Durable journal flushes that threw (tag pending)");

    /// <summary>Non-economic character snapshots. Tag <c>result</c> is written or coalesced.</summary>
    public static readonly Counter<long> PersistSnapshots =
        Meter.CreateCounter<long>("fineage.persist.snapshots", description: "Non-economic character snapshots (tag result)");

    public static readonly ObservableGauge<int> PersistPending = Meter.CreateObservableGauge(
        "fineage.persist.pending",
        ObservePersistPending,
        unit: "{snapshot}",
        description: "Character and pet snapshots waiting to be written, including the one in flight");

    public static readonly ObservableGauge<double> PersistOldestAge = Meter.CreateObservableGauge(
        "fineage.persist.oldest_age_ms",
        ObservePersistOldestAge,
        unit: "ms",
        description: "Age of the oldest character or pet snapshot that has not been written");

    public static Func<int>? PersistPendingCount { get; set; }
    public static Func<double>? PersistOldestAgeMs { get; set; }

    static IEnumerable<Measurement<int>> ObservePersistPending()
    {
        yield return new Measurement<int>(PersistPendingCount?.Invoke() ?? 0);
    }

    static IEnumerable<Measurement<double>> ObservePersistOldestAge()
    {
        yield return new Measurement<double>(PersistOldestAgeMs?.Invoke() ?? 0);
    }

    public static readonly ObservableGauge<double> PacketsPerSecond = Meter.CreateObservableGauge(
        "fineage.packets.per_second",
        ObservePacketRates,
        unit: "{packet}/s",
        description: "Packet rate over the most recent console summary or /metrics scrape window");

    static readonly object RateGate = new();
    static double _inboundPacketsPerSecond;
    static double _outboundPacketsPerSecond;

    public static void SetPacketRates(double inboundPerSecond, double outboundPerSecond)
    {
        if (double.IsNaN(inboundPerSecond) || inboundPerSecond < 0)
            inboundPerSecond = 0;
        if (double.IsNaN(outboundPerSecond) || outboundPerSecond < 0)
            outboundPerSecond = 0;
        lock (RateGate)
        {
            _inboundPacketsPerSecond = inboundPerSecond;
            _outboundPacketsPerSecond = outboundPerSecond;
        }
    }

    static IEnumerable<Measurement<double>> ObservePacketRates()
    {
        double inbound;
        double outbound;
        lock (RateGate)
        {
            inbound = _inboundPacketsPerSecond;
            outbound = _outboundPacketsPerSecond;
        }

        yield return new Measurement<double>(inbound, new KeyValuePair<string, object?>("direction", "in"));
        yield return new Measurement<double>(outbound, new KeyValuePair<string, object?>("direction", "out"));
    }

    public static readonly ObservableGauge<int> ZoneOnline = Meter.CreateObservableGauge(
        "fineage.zone.online",
        ObserveOnline,
        description: "Players currently in a zone simulation");

    public static readonly ObservableGauge<int> CommandIngressQueued = Meter.CreateObservableGauge(
        "fineage.command.ingress.queued",
        ObserveCommandIngressQueued,
        description: "Player commands waiting for the next simulation drain");

    public static Func<int>? OnlineCount { get; set; }
    public static Func<int>? CommandIngressDepth { get; set; }

    static IEnumerable<Measurement<int>> ObserveOnline()
    {
        yield return new Measurement<int>(OnlineCount?.Invoke() ?? 0);
    }

    static IEnumerable<Measurement<int>> ObserveCommandIngressQueued()
    {
        yield return new Measurement<int>(CommandIngressDepth?.Invoke() ?? 0);
    }
}
