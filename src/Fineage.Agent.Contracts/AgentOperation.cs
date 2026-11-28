namespace Fineage.Agent.Contracts;

/// <summary>
/// Registry record of a tool invocation. Terminal failure or refusal is explicit;
/// there is no successful stub for an unimplemented tool.
/// </summary>
public sealed record AgentOperation : IAgentContractDocument
{
    public AgentOperation(
        int schemaVersion,
        OperationId operationId,
        CommandId commandId,
        CorrelationId correlationId,
        IdempotencyKey idempotencyKey,
        string toolName,
        OperationStatus status,
        string? refusalCode,
        string? detail,
        DateTimeOffset createdAt,
        DateTimeOffset? completedAt)
    {
        ContractValidation.RequireSupportedSchema(schemaVersion);
        ContractValidation.RequireRefusalCode(status, refusalCode);
        bool terminal = status is OperationStatus.Succeeded or OperationStatus.Refused or OperationStatus.Failed;
        if (terminal && completedAt is null)
        {
            throw new AgentContractException(
                AgentRefusalCodes.InvalidContract,
                $"{status} requires CompletedAt.");
        }

        if (!terminal && completedAt is not null)
        {
            throw new AgentContractException(
                AgentRefusalCodes.InvalidContract,
                $"{status} must not set CompletedAt.");
        }

        SchemaVersion = schemaVersion;
        OperationId = operationId;
        CommandId = commandId;
        CorrelationId = correlationId;
        IdempotencyKey = idempotencyKey;
        ToolName = ContractValidation.RequireToolName(toolName);
        Status = status;
        RefusalCode = ContractValidation.OptionalText(refusalCode, ContractValidation.MaxRefusalCodeLength, nameof(RefusalCode));
        Detail = ContractValidation.OptionalText(detail, ContractValidation.MaxDetailLength, nameof(Detail));
        CreatedAt = ContractValidation.RequireUtcTimestamp(createdAt, nameof(CreatedAt));
        CompletedAt = completedAt is { } value ? ContractValidation.AsUtc(value) : null;
    }

    public int SchemaVersion { get; }
    public OperationId OperationId { get; }
    public CommandId CommandId { get; }
    public CorrelationId CorrelationId { get; }
    public IdempotencyKey IdempotencyKey { get; }
    public string ToolName { get; }
    public OperationStatus Status { get; }
    public string? RefusalCode { get; }
    public string? Detail { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? CompletedAt { get; }

    public static AgentOperation Create(
        OperationId operationId,
        CommandId commandId,
        CorrelationId correlationId,
        IdempotencyKey idempotencyKey,
        string toolName,
        OperationStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset? completedAt = null,
        string? refusalCode = null,
        string? detail = null) =>
        new(
            AgentContractSchema.Current,
            operationId,
            commandId,
            correlationId,
            idempotencyKey,
            toolName,
            status,
            refusalCode,
            detail,
            createdAt,
            completedAt);
}
