using System.Text.Json;

namespace Fineage.Agent.Contracts;

/// <summary>
/// Describes one Agent tool. Billing and risk fields are part of the v1 schema so
/// a later billing stage does not have to break compatibility. This type is not a
/// catalog of tools; unimplemented tools are omitted, not stubbed.
/// </summary>
public sealed record ToolDescriptor : IAgentContractDocument
{
    public ToolDescriptor(
        int schemaVersion,
        string name,
        string description,
        JsonElement? inputSchema,
        BillingClass billingClass,
        PriceUnit priceUnit,
        bool isBillable,
        RiskClass riskClass)
    {
        ContractValidation.RequireSupportedSchema(schemaVersion);
        if (!Enum.IsDefined(billingClass))
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, "BillingClass is not defined.");
        if (!Enum.IsDefined(priceUnit))
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, "PriceUnit is not defined.");
        if (!Enum.IsDefined(riskClass))
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, "RiskClass is not defined.");
        SchemaVersion = schemaVersion;
        Name = ContractValidation.RequireToolName(name);
        Description = ContractValidation.RequireText(description, ContractValidation.MaxDescriptionLength, nameof(Description));
        InputSchema = ContractValidation.OptionalObject(inputSchema, nameof(InputSchema));
        BillingClass = billingClass;
        PriceUnit = priceUnit;
        IsBillable = isBillable;
        RiskClass = riskClass;
    }

    public int SchemaVersion { get; }
    public string Name { get; }
    public string Description { get; }
    public JsonElement? InputSchema { get; }
    public BillingClass BillingClass { get; }
    public PriceUnit PriceUnit { get; }
    public bool IsBillable { get; }
    public RiskClass RiskClass { get; }

    public static ToolDescriptor Create(
        string name,
        string description,
        BillingClass billingClass,
        PriceUnit priceUnit,
        bool isBillable,
        RiskClass riskClass,
        JsonElement? inputSchema = null) =>
        new(
            AgentContractSchema.Current,
            name,
            description,
            inputSchema,
            billingClass,
            priceUnit,
            isBillable,
            riskClass);
}
