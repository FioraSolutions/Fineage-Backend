using System.Text.Json;

namespace Fineage.Agent.Contracts;

public sealed record AgentEvent : IAgentContractDocument
{
    public AgentEvent(
        int schemaVersion,
        string eventType,
        CorrelationId correlationId,
        SnapshotVersion snapshotVersion,
        AgentCursor cursor,
        DateTimeOffset occurredAt,
        JsonElement payload)
    {
        ContractValidation.RequireSupportedSchema(schemaVersion);
        if (cursor is null)
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, "Cursor is required.");
        SchemaVersion = schemaVersion;
        EventType = ContractValidation.RequireText(eventType, ContractValidation.MaxEventTypeLength, nameof(EventType));
        CorrelationId = correlationId;
        SnapshotVersion = snapshotVersion;
        Cursor = cursor;
        OccurredAt = ContractValidation.RequireUtcTimestamp(occurredAt, nameof(OccurredAt));
        Payload = ContractValidation.RequireObject(payload, nameof(Payload));
    }

    public int SchemaVersion { get; }
    public string EventType { get; }
    public CorrelationId CorrelationId { get; }
    public SnapshotVersion SnapshotVersion { get; }
    public AgentCursor Cursor { get; }
    public DateTimeOffset OccurredAt { get; }
    public JsonElement Payload { get; }

    public static AgentEvent Create(
        string eventType,
        CorrelationId correlationId,
        SnapshotVersion snapshotVersion,
        DateTimeOffset occurredAt,
        JsonElement payload,
        AgentCursor? cursor = null) =>
        new(
            AgentContractSchema.Current,
            eventType,
            correlationId,
            snapshotVersion,
            cursor ?? AgentCursor.Create(snapshotVersion),
            occurredAt,
            payload);
}
