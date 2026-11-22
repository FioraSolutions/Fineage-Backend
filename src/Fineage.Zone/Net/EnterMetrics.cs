using System.Diagnostics;
using System.Diagnostics.Metrics;
using Fineage.Protocol.Telemetry;

namespace Fineage.Zone.Net;

static class EnterMetrics
{
    public static IDisposable MeasurePlan() => new PhaseTimer(FineageTelemetry.EnterPlanMilliseconds);

    public static IDisposable MeasureSelfProjection() => new PhaseTimer(FineageTelemetry.EnterSelfProjectionMilliseconds);

    public static IDisposable MeasureKnownSnapshot() => new PhaseTimer(FineageTelemetry.EnterKnownSnapshotMilliseconds);

    public static IDisposable MeasureKnownEncode() => new PhaseTimer(FineageTelemetry.EnterKnownEncodeMilliseconds);

    public static IDisposable MeasurePeerFanout() => new PhaseTimer(FineageTelemetry.EnterPeerFanoutMilliseconds);

    public static void RecordCounts(int knownEntities, int knownPlayers, int frames, int bytes)
    {
        FineageTelemetry.EnterKnownEntities.Record(knownEntities);
        FineageTelemetry.EnterKnownPlayers.Record(knownPlayers);
        FineageTelemetry.EnterFrames.Record(frames);
        FineageTelemetry.EnterBytes.Record(bytes);
    }

    sealed class PhaseTimer : IDisposable
    {
        readonly Histogram<double> _histogram;
        readonly long _start;

        public PhaseTimer(Histogram<double> histogram)
        {
            _histogram = histogram;
            _start = Stopwatch.GetTimestamp();
        }

        public void Dispose() =>
            _histogram.Record(Stopwatch.GetElapsedTime(_start).TotalMilliseconds);
    }
}