using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fineage.Agent.Contracts;

/// <summary>Bounded change set between two snapshot versions.</summary>
public sealed record ObservationDelta : IAgentContractDocument
{
    public const int MaxEntities = AgentObservation.MaxEntities;
    public const int MaxPayloadBytes = AgentObservation.MaxPayloadBytes;

    [JsonConstructor]
    public ObservationDelta(
        int schemaVersion,
        SnapshotVersion fromVersion,
        SnapshotVersion toVersion,
        CorrelationId correlationId,
        IReadOnlyList<ObservedEntity> upserts,
        IReadOnlyList<int> removedObjectIds,
        AgentCursor cursor)
    {
        ContractValidation.RequireSupportedSchema(schemaVersion);
        if (toVersion.Value < fromVersion.Value)
        {
            throw new AgentContractException(
                AgentRefusalCodes.InvalidContract,
                "ObservationDelta.ToVersion must be >= FromVersion.");
        }

        SchemaVersion = schemaVersion;
        FromVersion = fromVersion;
        ToVersion = toVersion;
        CorrelationId = correlationId;
        Upserts = ContractValidation.CopyList(upserts, MaxEntities, nameof(Upserts));
        RemovedObjectIds = ContractValidation.CopyList(removedObjectIds, MaxEntities, nameof(RemovedObjectIds));
        foreach (int id in RemovedObjectIds)
        {
            if (id == 0)
            {
                throw new AgentContractException(
                    AgentRefusalCodes.InvalidContract,
                    "RemovedObjectIds must not contain 0.");
            }
        }

        Cursor = cursor ?? throw new AgentContractException(AgentRefusalCodes.InvalidContract, "Cursor is required.");
        ContractValidation.RequirePayloadLimit(this, MaxPayloadBytes, Upserts.Count, MaxEntities);
    }

    public int SchemaVersion { get; }
    public SnapshotVersion FromVersion { get; }
    public SnapshotVersion ToVersion { get; }
    public CorrelationId CorrelationId { get; }
    public IReadOnlyList<ObservedEntity> Upserts { get; }
    public IReadOnlyList<int> RemovedObjectIds { get; }
    public AgentCursor Cursor { get; }

    public static ObservationDelta Create(
        SnapshotVersion fromVersion,
        SnapshotVersion toVersion,
        CorrelationId correlationId,
        IReadOnlyList<ObservedEntity> upserts,
        IReadOnlyList<int> removedObjectIds,
        AgentCursor? cursor = null) =>
        new(
            AgentContractSchema.Current,
            fromVersion,
            toVersion,
            correlationId,
            upserts,
            removedObjectIds,
            cursor ?? AgentCursor.Create(toVersion));
}
