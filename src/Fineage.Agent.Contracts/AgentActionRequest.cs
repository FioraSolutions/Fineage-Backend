using System.Text.Json;

namespace Fineage.Agent.Contracts;

/// <summary>
/// A named tool call. This is not a <c>ZoneCommand</c>: the Agent Gate maps an accepted
/// tool name onto the existing game ingress.
/// </summary>
public sealed record AgentActionRequest : IAgentContractDocument
{
    public AgentActionRequest(
        int schemaVersion,
        CommandId commandId,
        CorrelationId correlationId,
        IdempotencyKey idempotencyKey,
        AgentPrincipal principal,
        string toolName,
        JsonElement arguments,
        DateTimeOffset deadline)
    {
        ContractValidation.RequireSupportedSchema(schemaVersion);
        if (principal is null)
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, "Principal is required.");
        SchemaVersion = schemaVersion;
        CommandId = commandId;
        CorrelationId = correlationId;
        IdempotencyKey = idempotencyKey;
        Principal = principal;
        ToolName = ContractValidation.RequireToolName(toolName);
        Arguments = ContractValidation.RequireObject(arguments, nameof(Arguments));
        Deadline = ContractValidation.RequireUtcTimestamp(deadline, nameof(Deadline));
    }

    public int SchemaVersion { get; }
    public CommandId CommandId { get; }
    public CorrelationId CorrelationId { get; }
    public IdempotencyKey IdempotencyKey { get; }
    public AgentPrincipal Principal { get; }
    public string ToolName { get; }
    public JsonElement Arguments { get; }
    public DateTimeOffset Deadline { get; }

    public static AgentActionRequest Create(
        CommandId commandId,
        CorrelationId correlationId,
        IdempotencyKey idempotencyKey,
        AgentPrincipal principal,
        string toolName,
        JsonElement arguments,
        DateTimeOffset deadline) =>
        new(
            AgentContractSchema.Current,
            commandId,
            correlationId,
            idempotencyKey,
            principal,
            toolName,
            arguments,
            deadline);
}
