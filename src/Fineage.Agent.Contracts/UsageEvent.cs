namespace Fineage.Agent.Contracts;

/// <summary>
/// An AI usage record in integer credits. Isolated from the auditable economy ledger:
/// this type must not be written to the game journal or character bags.
/// </summary>
public sealed record UsageEvent : IAgentContractDocument
{
    public UsageEvent(
        int schemaVersion,
        CorrelationId correlationId,
        OperationId? operationId,
        string toolName,
        BillingClass billingClass,
        PriceUnit priceUnit,
        long quantity,
        CreditAmount credits,
        DateTimeOffset occurredAt)
    {
        ContractValidation.RequireSupportedSchema(schemaVersion);
        if (!Enum.IsDefined(billingClass))
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, "BillingClass is not defined.");
        if (!Enum.IsDefined(priceUnit))
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, "PriceUnit is not defined.");
        if (quantity < 0)
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, "Quantity must be >= 0.");
        SchemaVersion = schemaVersion;
        CorrelationId = correlationId;
        OperationId = operationId;
        ToolName = ContractValidation.RequireToolName(toolName);
        BillingClass = billingClass;
        PriceUnit = priceUnit;
        Quantity = quantity;
        Credits = credits;
        OccurredAt = ContractValidation.RequireUtcTimestamp(occurredAt, nameof(OccurredAt));
    }

    public int SchemaVersion { get; }
    public CorrelationId CorrelationId { get; }
    public OperationId? OperationId { get; }
    public string ToolName { get; }
    public BillingClass BillingClass { get; }
    public PriceUnit PriceUnit { get; }
    public long Quantity { get; }
    public CreditAmount Credits { get; }
    public DateTimeOffset OccurredAt { get; }

    public static UsageEvent Create(
        CorrelationId correlationId,
        string toolName,
        BillingClass billingClass,
        PriceUnit priceUnit,
        long quantity,
        CreditAmount credits,
        DateTimeOffset occurredAt,
        OperationId? operationId = null) =>
        new(
            AgentContractSchema.Current,
            correlationId,
            operationId,
            toolName,
            billingClass,
            priceUnit,
            quantity,
            credits,
            occurredAt);
}
