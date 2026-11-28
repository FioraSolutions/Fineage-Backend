namespace Fineage.Agent.Observation;

/// <summary>Paging and optional kind filter applied off the simulation thread.</summary>
public sealed record AgentObservationQuery(int PageOffset = 0, string? Kind = null)
{
    public static AgentObservationQuery Default { get; } = new();
}
