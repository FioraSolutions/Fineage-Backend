using System.Diagnostics.Metrics;

namespace Fineage.Agent.Identity;

/// <summary>Policy-denial metrics for Agent identity. The instrument name matches the accepted Prometheus series.</summary>
public static class AgentIdentityTelemetry
{
    public const string MeterName = "Fineage.Agent";
    public const string PolicyDenialsInstrument = "agent_policy_denials_total";
    public const string ReasonTag = "reason";

    public static readonly Meter Meter = new(MeterName, "1.0.0");

    public static readonly Counter<long> PolicyDenials =
        Meter.CreateCounter<long>(PolicyDenialsInstrument, description: "Agent identity and scope policy refusals");

    public static void RecordDenial(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            reason = "unknown";
        PolicyDenials.Add(1, new KeyValuePair<string, object?>(ReasonTag, reason));
    }
}
