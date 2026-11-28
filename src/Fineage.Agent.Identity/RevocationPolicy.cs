namespace Fineage.Agent.Identity;

/// <summary>
/// What happens to work already accepted when a principal, agent, or credential is revoked.
/// New authenticate and authorize calls are always refused immediately.
/// </summary>
public static class RevocationPolicy
{
    /// <summary>
    /// Already-accepted operations may finish. The revocation is recorded in the audit log.
    /// The identity service does not silently cancel in-flight work and does not allow new work.
    /// </summary>
    public const string AcceptedCompleteWithAudit = "accepted_complete_with_audit";
}
