using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fineage.Agent.Contracts;

/// <summary>
/// A bounded world projection for an Agent. Entity count and payload bytes are enforced
/// by this type; callers cannot opt out of the limits.
/// </summary>
public sealed record AgentObservation : IAgentContractDocument
{
    public const int MaxEntities = 128;
    public const int MaxPayloadBytes = 32_768;

    [JsonConstructor]
    public AgentObservation(
        int schemaVersion,
        SnapshotVersion snapshotVersion,
        CorrelationId correlationId,
        CharacterId characterId,
        ObservedSelf? self,
        IReadOnlyList<ObservedEntity> entities,
        JsonElement? details,
        AgentCursor cursor)
    {
        ContractValidation.RequireSupportedSchema(schemaVersion);
        SchemaVersion = schemaVersion;
        SnapshotVersion = snapshotVersion;
        CorrelationId = correlationId;
        CharacterId = characterId;
        Self = self;
        Entities = ContractValidation.CopyList(entities, MaxEntities, nameof(Entities));
        Details = ContractValidation.OptionalObject(details, nameof(Details));
        Cursor = cursor ?? throw new AgentContractException(AgentRefusalCodes.InvalidContract, "Cursor is required.");
        ContractValidation.RequirePayloadLimit(this, MaxPayloadBytes, Entities.Count, MaxEntities);
    }

    public int SchemaVersion { get; }
    public SnapshotVersion SnapshotVersion { get; }
    public CorrelationId CorrelationId { get; }
    public CharacterId CharacterId { get; }
    public ObservedSelf? Self { get; }
    public IReadOnlyList<ObservedEntity> Entities { get; }
    public JsonElement? Details { get; }
    public AgentCursor Cursor { get; }

    public static AgentObservation Create(
        SnapshotVersion snapshotVersion,
        CorrelationId correlationId,
        CharacterId characterId,
        ObservedSelf? self,
        IReadOnlyList<ObservedEntity> entities,
        JsonElement? details,
        AgentCursor? cursor = null) =>
        new(
            AgentContractSchema.Current,
            snapshotVersion,
            correlationId,
            characterId,
            self,
            entities,
            details,
            cursor ?? AgentCursor.Create(snapshotVersion));
}
