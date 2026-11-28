namespace Fineage.Agent.Contracts;

/// <summary>Immediate admission result of an action. Refused operations carry an explicit code.</summary>
public sealed record AgentActionReceipt : IAgentContractDocument
{
    public AgentActionReceipt(
        int schemaVersion,
        CommandId commandId,
        CorrelationId correlationId,
        OperationId operationId,
        OperationStatus status,
        string? refusalCode,
        string? detail)
    {
        ContractValidation.RequireSupportedSchema(schemaVersion);
        if (status is not (OperationStatus.Accepted or OperationStatus.Refused))
        {
            throw new AgentContractException(
                AgentRefusalCodes.InvalidContract,
                "A receipt status must be Accepted or Refused.");
        }

        ContractValidation.RequireRefusalCode(status, refusalCode);
        SchemaVersion = schemaVersion;
        CommandId = commandId;
        CorrelationId = correlationId;
        OperationId = operationId;
        Status = status;
        RefusalCode = ContractValidation.OptionalText(refusalCode, ContractValidation.MaxRefusalCodeLength, nameof(RefusalCode));
        Detail = ContractValidation.OptionalText(detail, ContractValidation.MaxDetailLength, nameof(Detail));
    }

    public int SchemaVersion { get; }
    public CommandId CommandId { get; }
    public CorrelationId CorrelationId { get; }
    public OperationId OperationId { get; }
    public OperationStatus Status { get; }
    public string? RefusalCode { get; }
    public string? Detail { get; }

    public static AgentActionReceipt Accepted(
        CommandId commandId,
        CorrelationId correlationId,
        OperationId operationId) =>
        new(AgentContractSchema.Current, commandId, correlationId, operationId, OperationStatus.Accepted, null, null);

    public static AgentActionReceipt Refused(
        CommandId commandId,
        CorrelationId correlationId,
        OperationId operationId,
        string refusalCode,
        string? detail = null) =>
        new(
            AgentContractSchema.Current,
            commandId,
            correlationId,
            operationId,
            OperationStatus.Refused,
            refusalCode,
            detail);
}
