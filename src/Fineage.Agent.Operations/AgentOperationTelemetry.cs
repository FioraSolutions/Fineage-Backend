using System.Diagnostics.Metrics;

namespace Fineage.Agent.Operations;

/// <summary>Runtime operation metrics. Instrument names match the accepted series.</summary>
public static class AgentOperationTelemetry
{
    public const string MeterName = "Fineage.Agent";
    public const string RunningInstrument = "agent_operations_running";
    public const string DurationInstrument = "operation_duration_ms";
    public const string OperationTag = "operation";

    public static readonly Meter Meter = new(MeterName, "1.0.0");

    static long _running;

    public static readonly ObservableGauge<long> Running =
        Meter.CreateObservableGauge(
            RunningInstrument,
            () => Interlocked.Read(ref _running),
            unit: "{operation}",
            description: "Accepted or running Agent operations that have not yet reached a terminal status");

    public static readonly Histogram<double> Duration =
        Meter.CreateHistogram<double>(
            DurationInstrument,
            unit: "ms",
            description: "Wall duration from admission to a terminal operation status");

    public static long RunningCount => Interlocked.Read(ref _running);

    public static void NoteOpened() => Interlocked.Increment(ref _running);

    public static void NoteClosed(string toolName, double durationMilliseconds)
    {
        long current;
        do
        {
            current = Interlocked.Read(ref _running);
            if (current <= 0)
                break;
        }
        while (Interlocked.CompareExchange(ref _running, current - 1, current) != current);

        string operation = string.IsNullOrWhiteSpace(toolName) ? "unknown" : toolName;
        Duration.Record(durationMilliseconds, new KeyValuePair<string, object?>(OperationTag, operation));
    }

    public static void ResetForTests() => Interlocked.Exchange(ref _running, 0);
}
