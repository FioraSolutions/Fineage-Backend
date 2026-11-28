using Fineage.Agent.Contracts;
using Fineage.Zone.World;

namespace Fineage.Agent.Gate;

/// <summary>
/// Outcome of an Agent write. Scope or binding failures never reach ingress.
/// Ingress and apply refusals use the same strings a player would have received.
/// </summary>
public sealed class AgentCommandResult
{
    AgentCommandResult(
        bool ok,
        string? refusalCode,
        string? detail,
        string? ingressOutcome,
        OperationId operationId,
        CommandSource source)
    {
        Ok = ok;
        RefusalCode = refusalCode;
        Detail = detail;
        IngressOutcome = ingressOutcome;
        OperationId = operationId;
        Source = source;
    }

    public bool Ok { get; }
    public string? RefusalCode { get; }
    public string? Detail { get; }

    /// <summary>The <c>ExecuteAsync</c> rejection string, or null on success — identical to the player path.</summary>
    public string? IngressOutcome { get; }

    public OperationId OperationId { get; }
    public CommandSource Source { get; }

    public static AgentCommandResult Succeeded(OperationId operationId) =>
        new(true, null, null, null, operationId, CommandSource.Ai);

    public static AgentCommandResult IngressRefused(OperationId operationId, string outcome) =>
        new(false, AgentRefusalCodes.InvalidContract, outcome, outcome, operationId, CommandSource.Ai);

    public static AgentCommandResult Denied(OperationId operationId, string refusalCode, string detail) =>
        new(false, refusalCode, detail, null, operationId, CommandSource.Ai);
}
