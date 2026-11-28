using Fineage.Agent.Contracts;

namespace Fineage.Agent.Observation;

/// <summary>Either a bounded observation or an explicit refusal. Never a silent truncation.</summary>
public sealed class AgentObservationResult
{
    AgentObservationResult(AgentObservation? observation, string? refusalCode, string? detail)
    {
        Observation = observation;
        RefusalCode = refusalCode;
        Detail = detail;
    }

    public bool Ok => Observation is not null;
    public AgentObservation? Observation { get; }
    public string? RefusalCode { get; }
    public string? Detail { get; }

    public static AgentObservationResult Success(AgentObservation observation) =>
        new(observation, null, null);

    public static AgentObservationResult Refuse(string refusalCode, string detail) =>
        new(null, refusalCode, detail);
}
