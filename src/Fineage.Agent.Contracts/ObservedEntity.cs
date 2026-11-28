using System.Text.Json;

namespace Fineage.Agent.Contracts;

public sealed record ObservedSelf
{
    public ObservedSelf(int objectId, int x, int y, int z, int heading)
    {
        if (objectId == 0)
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, "ObservedSelf.ObjectId must be non-zero.");
        ObjectId = objectId;
        X = x;
        Y = y;
        Z = z;
        Heading = heading;
    }

    public int ObjectId { get; }
    public int X { get; }
    public int Y { get; }
    public int Z { get; }
    public int Heading { get; }
}

public sealed record ObservedEntity
{
    public ObservedEntity(
        int objectId,
        string kind,
        int x,
        int y,
        int z,
        string? name,
        JsonElement? attributes)
    {
        if (objectId == 0)
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, "ObservedEntity.ObjectId must be non-zero.");
        ObjectId = objectId;
        Kind = ContractValidation.RequireText(kind, ContractValidation.MaxKindLength, nameof(Kind));
        X = x;
        Y = y;
        Z = z;
        Name = ContractValidation.OptionalText(name, ContractValidation.MaxNameLength, nameof(Name));
        Attributes = ContractValidation.OptionalObject(attributes, nameof(Attributes));
    }

    public int ObjectId { get; }
    public string Kind { get; }
    public int X { get; }
    public int Y { get; }
    public int Z { get; }
    public string? Name { get; }
    public JsonElement? Attributes { get; }
}
