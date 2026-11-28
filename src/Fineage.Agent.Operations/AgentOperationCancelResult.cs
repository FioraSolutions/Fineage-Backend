namespace Fineage.Agent.Operations;

/// <summary>Outcome of a cancel request. By default the accepted operation is left running.</summary>
public sealed class AgentOperationCancelResult
{
    AgentOperationCancelResult(bool cancelled, AgentOperation operation, string? refusalCode, string? detail)
    {
        Cancelled = cancelled;
        Operation = operation;
        RefusalCode = refusalCode;
        Detail = detail;
    }

    public bool Cancelled { get; }
    public AgentOperation Operation { get; }
    public string? RefusalCode { get; }
    public string? Detail { get; }

    public static AgentOperationCancelResult Allowed(AgentOperation operation) =>
        new(true, operation, null, null);

    public static AgentOperationCancelResult Denied(AgentOperation operation, string refusalCode, string detail) =>
        new(false, operation, refusalCode, detail);
}
