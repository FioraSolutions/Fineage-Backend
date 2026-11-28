namespace Fineage.Agent.Contracts;

/// <summary>Stable refusal codes for contract validation and Agent receipts.</summary>
public static class AgentRefusalCodes
{
    public const string UnsupportedSchema = "unsupported_schema";
    public const string InvalidContract = "invalid_contract";
    public const string ObservationLimit = "observation_limit";
    public const string UnknownTool = "unknown_tool";
    public const string Expired = "expired";
    public const string NotBound = "not_bound";
    public const string IdempotencyConflict = "idempotency_conflict";
    public const string InvalidCredential = "invalid_credential";
    public const string Revoked = "revoked";
    public const string CrossTenant = "cross_tenant";
    public const string CharacterNotBound = "character_not_bound";
    public const string ScopeDenied = "scope_denied";
    public const string AgentMismatch = "agent_mismatch";
    public const string StaleSnapshot = "stale_snapshot";
    public const string CancelNotAllowed = "cancel_not_allowed";
    public const string Cancelled = "cancelled";
}
