namespace Fineage.Agent.Contracts;

/// <summary>
/// A quote for an AI operation in integer credits, isolated from the auditable economy ledger.
/// </summary>
public sealed record BillingQuote : IAgentContractDocument
{
    public BillingQuote(
        int schemaVersion,
        string toolName,
        BillingClass billingClass,
        PriceUnit priceUnit,
        long quantity,
        CreditAmount credits,
        bool isBillable,
        DateTimeOffset expiry)
    {
        ContractValidation.RequireSupportedSchema(schemaVersion);
        if (!Enum.IsDefined(billingClass))
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, "BillingClass is not defined.");
        if (!Enum.IsDefined(priceUnit))
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, "PriceUnit is not defined.");
        if (quantity < 0)
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, "Quantity must be >= 0.");
        if (!isBillable && credits.Value != 0)
        {
            throw new AgentContractException(
                AgentRefusalCodes.InvalidContract,
                "A non-billable quote must have zero credits.");
        }

        SchemaVersion = schemaVersion;
        ToolName = ContractValidation.RequireToolName(toolName);
        BillingClass = billingClass;
        PriceUnit = priceUnit;
        Quantity = quantity;
        Credits = credits;
        IsBillable = isBillable;
        Expiry = ContractValidation.RequireUtcTimestamp(expiry, nameof(Expiry));
    }

    public int SchemaVersion { get; }
    public string ToolName { get; }
    public BillingClass BillingClass { get; }
    public PriceUnit PriceUnit { get; }
    public long Quantity { get; }
    public CreditAmount Credits { get; }
    public bool IsBillable { get; }
    public DateTimeOffset Expiry { get; }

    public static BillingQuote Create(
        string toolName,
        BillingClass billingClass,
        PriceUnit priceUnit,
        long quantity,
        CreditAmount credits,
        bool isBillable,
        DateTimeOffset expiry) =>
        new(
            AgentContractSchema.Current,
            toolName,
            billingClass,
            priceUnit,
            quantity,
            credits,
            isBillable,
            expiry);
}
