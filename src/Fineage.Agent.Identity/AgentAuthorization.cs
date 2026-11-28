using Fineage.Agent.Contracts;

namespace Fineage.Agent.Identity;

/// <summary>Result of an authorize call. Failures are explicit: <see cref="Allowed"/> is never a silent default.</summary>
public sealed class AgentAuthorization
{
    AgentAuthorization(
        bool allowed,
        string? refusalCode,
        string? detail,
        VerifiedAgentIdentity? identity,
        CharacterId? characterId,
        AgentScope? scope)
    {
        Allowed = allowed;
        RefusalCode = refusalCode;
        Detail = detail;
        Identity = identity;
        CharacterId = characterId;
        Scope = scope;
    }

    public bool Allowed { get; }
    public string? RefusalCode { get; }
    public string? Detail { get; }
    public VerifiedAgentIdentity? Identity { get; }
    public CharacterId? CharacterId { get; }
    public AgentScope? Scope { get; }

    public static AgentAuthorization Permit(
        VerifiedAgentIdentity identity,
        CharacterId characterId,
        AgentScope scope) =>
        new(true, null, null, identity, characterId, scope);

    public static AgentAuthorization Deny(string refusalCode, string detail) =>
        new(false, refusalCode, detail, null, null, null);
}
