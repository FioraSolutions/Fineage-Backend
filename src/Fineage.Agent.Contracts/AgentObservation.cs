using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fineage.Agent.Contracts;

/// <summary>
/// A bounded world projection for an Agent. Entity count and payload bytes are enforced
/// by this type; callers cannot opt out of the limits. When the visible set is larger
/// than <see cref="MaxEntities"/> (or a page would exceed <see cref="MaxPayloadBytes"/>),
/// callers must page: <see cref="HasMore"/> is set and <see cref="TotalVisible"/> is the
/// known visible count. Silent truncation is not representable.
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
        AgentCursor cursor,
        int totalVisible = 0,
        int pageOffset = 0,
        bool hasMore = false)
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
        if (pageOffset < 0)
            throw new AgentContractException(AgentRefusalCodes.InvalidContract, "PageOffset must be >= 0.");
        PageOffset = pageOffset;
        HasMore = hasMore;
        TotalVisible = totalVisible > 0 ? totalVisible : PageOffset + Entities.Count + (HasMore ? 1 : 0);
        if (TotalVisible < PageOffset + Entities.Count)
        {
            throw new AgentContractException(
                AgentRefusalCodes.InvalidContract,
                "TotalVisible must cover the current page.");
        }

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

    /// <summary>Total visible entities on the character knownlist (not just this page).</summary>
    public int TotalVisible { get; }

    /// <summary>Index of the first entity on this page in the stable visible order.</summary>
    public int PageOffset { get; }

    /// <summary>True when more visible entities exist after this page.</summary>
    public bool HasMore { get; }

    public static AgentObservation Create(
        SnapshotVersion snapshotVersion,
        CorrelationId correlationId,
        CharacterId characterId,
        ObservedSelf? self,
        IReadOnlyList<ObservedEntity> entities,
        JsonElement? details,
        AgentCursor? cursor = null,
        int totalVisible = 0,
        int pageOffset = 0,
        bool hasMore = false) =>
        new(
            AgentContractSchema.Current,
            snapshotVersion,
            correlationId,
            characterId,
            self,
            entities,
            details,
            cursor ?? AgentCursor.Create(snapshotVersion, hasMore ? (pageOffset + (entities?.Count ?? 0)).ToString() : null),
            totalVisible,
            pageOffset,
            hasMore);
}
