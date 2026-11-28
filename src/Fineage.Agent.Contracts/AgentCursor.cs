namespace Fineage.Agent.Contracts;

/// <summary>
/// Cursor for a later event/delta feed. <see cref="Token"/> is opaque and optional;
/// there is no durable Zone outbox in this contract.
/// </summary>
public sealed record AgentCursor : IAgentContractDocument
{
    public AgentCursor(int schemaVersion, SnapshotVersion snapshotVersion, string? token)
    {
        ContractValidation.RequireSupportedSchema(schemaVersion);
        SchemaVersion = schemaVersion;
        SnapshotVersion = snapshotVersion;
        Token = ContractValidation.OptionalText(token, ContractValidation.MaxCursorTokenLength, nameof(Token));
    }

    public int SchemaVersion { get; }
    public SnapshotVersion SnapshotVersion { get; }
    public string? Token { get; }

    public static AgentCursor Create(SnapshotVersion snapshotVersion, string? token = null) =>
        new(AgentContractSchema.Current, snapshotVersion, token);
}
