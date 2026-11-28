using Fineage.Agent.Contracts;

namespace Fineage.Agent.Identity;

/// <summary>
/// Identity fields that may appear on a tool payload. They are never used as the source of
/// tenant, principal, or agent: a mismatch with the verified credential is an explicit refusal.
/// </summary>
public sealed record UntrustedIdentityClaims(TenantId? TenantId, AgentId? AgentId, CharacterId? CharacterId);
