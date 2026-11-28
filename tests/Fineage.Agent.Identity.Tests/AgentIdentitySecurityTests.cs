using Fineage.Agent.Contracts;
using Fineage.Agent.Identity;
using Xunit;

namespace Fineage.Agent.Identity.Tests;

public sealed class AgentIdentitySecurityTests
{
    [Fact]
    public void Cross_tenant_access_is_denied_for_every_published_tool()
    {
        var harness = new IdentityHarness();
        SealedAgent alice = harness.Provision(
            "tenant-a", "principal-a", "agent-a", IdentityHarness.AliceCharacter, IdentityHarness.AliceAccount,
            AgentScope.WorldRead, AgentScope.NavigationExecute, AgentScope.CombatExecute,
            AgentScope.QuestRead, AgentScope.InventoryMutate, AgentScope.BillingRead);
        harness.Provision(
            "tenant-b", "principal-b", "agent-b", IdentityHarness.BobCharacter, IdentityHarness.BobAccount,
            AgentScope.WorldRead, AgentScope.NavigationExecute, AgentScope.CombatExecute,
            AgentScope.QuestRead, AgentScope.InventoryMutate, AgentScope.BillingRead);

        VerifiedAgentIdentity identity = harness.Service.Authenticate(alice.Issued.CredentialId, alice.Issued.RevealSecret());
        using var denials = new DenialCounter();
        string[] tools = ["look", "inspect", "move", "navigate", "attack", "quest_log", "use_item", "drop_item", "usage", "quote"];
        foreach (string tool in tools)
        {
            AgentAuthorization result = harness.Service.AuthorizeTool(identity, IdentityHarness.BobCharacter, tool);
            Assert.False(result.Allowed);
            Assert.Equal(AgentRefusalCodes.CrossTenant, result.RefusalCode);
        }

        Assert.Equal(tools.Length, denials.Total);
        Assert.Equal(tools.Length, denials[AgentRefusalCodes.CrossTenant]);
    }

    [Fact]
    public void Foreign_tenant_agent_and_character_ids_are_refused_with_specific_reasons()
    {
        var harness = new IdentityHarness();
        SealedAgent alice = harness.Provision(
            "tenant-a", "principal-a", "agent-a", IdentityHarness.AliceCharacter, IdentityHarness.AliceAccount,
            AgentScope.WorldRead);
        SealedAgent bob = harness.Provision(
            "tenant-b", "principal-b", "agent-b", IdentityHarness.BobCharacter, IdentityHarness.BobAccount,
            AgentScope.WorldRead);
        VerifiedAgentIdentity identity = harness.Service.Authenticate(alice.Issued.CredentialId, alice.Issued.RevealSecret());

        AgentAuthorization tenant = harness.Service.Authorize(
            identity,
            alice.CharacterId,
            AgentScope.WorldRead,
            new UntrustedIdentityClaims(bob.TenantId, null, null));
        Assert.False(tenant.Allowed);
        Assert.Equal(AgentRefusalCodes.CrossTenant, tenant.RefusalCode);

        AgentAuthorization agent = harness.Service.Authorize(
            identity,
            alice.CharacterId,
            AgentScope.WorldRead,
            new UntrustedIdentityClaims(null, bob.AgentId, null));
        Assert.False(agent.Allowed);
        Assert.Equal(AgentRefusalCodes.AgentMismatch, agent.RefusalCode);

        AgentAuthorization character = harness.Service.Authorize(identity, bob.CharacterId, AgentScope.WorldRead);
        Assert.False(character.Allowed);
        Assert.Equal(AgentRefusalCodes.CrossTenant, character.RefusalCode);
    }

    [Fact]
    public void Payload_tenant_id_is_never_used_as_identity()
    {
        var harness = new IdentityHarness();
        SealedAgent alice = harness.Provision(
            "tenant-a", "principal-a", "agent-a", IdentityHarness.AliceCharacter, IdentityHarness.AliceAccount,
            AgentScope.WorldRead);
        VerifiedAgentIdentity identity = harness.Service.Authenticate(alice.Issued.CredentialId, alice.Issued.RevealSecret());

        AgentAuthorization mismatch = harness.Service.Authorize(
            identity,
            alice.CharacterId,
            AgentScope.WorldRead,
            new UntrustedIdentityClaims(new TenantId("tenant-b"), null, null));
        Assert.False(mismatch.Allowed);
        Assert.Equal(AgentRefusalCodes.CrossTenant, mismatch.RefusalCode);
        Assert.Equal(alice.TenantId, identity.TenantId);
    }

    [Fact]
    public void Character_outside_the_allowlist_is_refused_even_when_the_object_id_is_known()
    {
        var harness = new IdentityHarness();
        harness.Characters.Add(new CharacterId("9999"), IdentityHarness.AliceAccount);
        SealedAgent alice = harness.Provision(
            "tenant-a", "principal-a", "agent-a", IdentityHarness.AliceCharacter, IdentityHarness.AliceAccount,
            AgentScope.WorldRead);
        VerifiedAgentIdentity identity = harness.Service.Authenticate(alice.Issued.CredentialId, alice.Issued.RevealSecret());

        AgentAuthorization result = harness.Service.Authorize(identity, new CharacterId("9999"), AgentScope.WorldRead);
        Assert.False(result.Allowed);
        Assert.Equal(AgentRefusalCodes.CharacterNotBound, result.RefusalCode);
    }

    [Fact]
    public void Binding_requires_an_explicit_owner_match_and_is_not_inferred_from_names()
    {
        var harness = new IdentityHarness();
        TenantId tenantId = harness.Service.CreateTenant(new TenantId("tenant-a"));
        PrincipalId principalId = harness.Service.CreatePrincipal(tenantId, new PrincipalId("principal-a"));
        AgentId agentId = harness.Service.CreateAgent(principalId, [AgentScope.WorldRead], new AgentId("alice"));

        AgentContractException wrongOwner = Assert.Throws<AgentContractException>(() =>
            harness.Service.BindCharacter(agentId, IdentityHarness.AliceAccount, IdentityHarness.BobCharacter));
        Assert.Equal(AgentRefusalCodes.CharacterNotBound, wrongOwner.Code);

        // An agent id that happens to equal a game account still needs an explicit binding row.
        Assert.Empty(harness.Store.ListActiveBindings(agentId));
        harness.Service.BindCharacter(agentId, IdentityHarness.AliceAccount, IdentityHarness.AliceCharacter);
        Assert.Single(harness.Store.ListActiveBindings(agentId));
    }

    [Fact]
    public void Scope_escalation_is_refused()
    {
        var harness = new IdentityHarness();
        SealedAgent alice = harness.Provision(
            "tenant-a", "principal-a", "agent-a", IdentityHarness.AliceCharacter, IdentityHarness.AliceAccount,
            AgentScope.WorldRead);
        VerifiedAgentIdentity identity = harness.Service.Authenticate(alice.Issued.CredentialId, alice.Issued.RevealSecret());
        using var denials = new DenialCounter();

        AgentAuthorization result = harness.Service.Authorize(identity, alice.CharacterId, AgentScope.InventoryMutate);
        Assert.False(result.Allowed);
        Assert.Equal(AgentRefusalCodes.ScopeDenied, result.RefusalCode);
        Assert.Equal(1, denials[AgentRefusalCodes.ScopeDenied]);

        AgentAuthorization unknown = harness.Service.AuthorizeTool(identity, alice.CharacterId, "invented_tool");
        Assert.False(unknown.Allowed);
        Assert.Equal(AgentRefusalCodes.UnknownTool, unknown.RefusalCode);
        Assert.Equal(1, denials[AgentRefusalCodes.UnknownTool]);
    }

    [Fact]
    public void Revoked_and_expired_credentials_are_refused_and_the_denial_metric_grows()
    {
        var harness = new IdentityHarness();
        SealedAgent alice = harness.Provision(
            "tenant-a", "principal-a", "agent-a", IdentityHarness.AliceCharacter, IdentityHarness.AliceAccount,
            AgentScope.WorldRead);
        string secret = alice.Issued.RevealSecret();
        VerifiedAgentIdentity identity = harness.Service.Authenticate(alice.Issued.CredentialId, secret);
        Assert.True(harness.Service.Authorize(identity, alice.CharacterId, AgentScope.WorldRead).Allowed);

        using var denials = new DenialCounter();
        harness.Service.RevokeCredential(alice.Issued.CredentialId, "owner_revoked");
        Assert.False(harness.Service.TryAuthenticate(alice.Issued.CredentialId, secret, out _, out string? revoked));
        Assert.Equal(AgentRefusalCodes.Revoked, revoked);
        Assert.False(harness.Service.Authorize(identity, alice.CharacterId, AgentScope.WorldRead).Allowed);
        Assert.True(denials[AgentRefusalCodes.Revoked] >= 2);

        AgentRevocationLogRecord log = Assert.Single(harness.Store.ListRevocationLog());
        Assert.Equal("owner_revoked", log.Reason);
        Assert.Equal(RevocationPolicy.AcceptedCompleteWithAudit, log.Policy);

        SealedAgent other = harness.Provision(
            "tenant-c", "principal-c", "agent-c", IdentityHarness.BobCharacter, IdentityHarness.BobAccount,
            AgentScope.WorldRead);
        string otherSecret = other.Issued.RevealSecret();
        harness.Clock.Advance(TimeSpan.FromHours(2));
        Assert.False(harness.Service.TryAuthenticate(other.Issued.CredentialId, otherSecret, out _, out string? expired));
        Assert.Equal(AgentRefusalCodes.Expired, expired);
        Assert.True(denials[AgentRefusalCodes.Expired] >= 1);
    }

    [Fact]
    public void Revoking_a_principal_stops_new_calls_for_all_of_its_agents()
    {
        var harness = new IdentityHarness();
        SealedAgent alice = harness.Provision(
            "tenant-a", "principal-a", "agent-a", IdentityHarness.AliceCharacter, IdentityHarness.AliceAccount,
            AgentScope.WorldRead);
        VerifiedAgentIdentity identity = harness.Service.Authenticate(alice.Issued.CredentialId, alice.Issued.RevealSecret());
        harness.Service.RevokePrincipal(alice.PrincipalId, "principal_revoked");

        Assert.False(harness.Service.TryAuthenticate(alice.Issued.CredentialId, alice.Issued.RevealSecret(), out _, out string? code));
        Assert.Equal(AgentRefusalCodes.Revoked, code);
        Assert.False(harness.Service.Authorize(identity, alice.CharacterId, AgentScope.WorldRead).Allowed);
    }

    [Fact]
    public void Wrong_secret_is_refused_without_storing_or_echoing_the_plaintext()
    {
        var harness = new IdentityHarness();
        SealedAgent alice = harness.Provision(
            "tenant-a", "principal-a", "agent-a", IdentityHarness.AliceCharacter, IdentityHarness.AliceAccount,
            AgentScope.WorldRead);
        string secret = alice.Issued.RevealSecret();

        Assert.False(harness.Store.ContainsPlaintext(secret));
        Assert.DoesNotContain(secret, alice.Issued.ToString(), StringComparison.Ordinal);
        Assert.False(harness.Service.TryAuthenticate(alice.Issued.CredentialId, "not-the-secret", out _, out string? code));
        Assert.Equal(AgentRefusalCodes.InvalidCredential, code);
        Assert.False(harness.Service.TryAuthenticate("cr_missing", secret, out _, out string? unknown));
        Assert.Equal(AgentRefusalCodes.InvalidCredential, unknown);

        AgentCredentialRecord stored = harness.Store.FindCredential(alice.Issued.CredentialId)!;
        Assert.NotEqual(secret, stored.SecretHash);
        Assert.NotEqual(secret, stored.SecretSalt);
        Assert.True(AgentSecretHasher.Verify(secret, stored.SecretHash, stored.SecretSalt, stored.Iterations));
    }

    [Fact]
    public void Granted_scope_on_a_bound_character_is_allowed()
    {
        var harness = new IdentityHarness();
        SealedAgent alice = harness.Provision(
            "tenant-a", "principal-a", "agent-a", IdentityHarness.AliceCharacter, IdentityHarness.AliceAccount,
            AgentScope.WorldRead);
        VerifiedAgentIdentity identity = harness.Service.Authenticate(alice.Issued.CredentialId, alice.Issued.RevealSecret());
        AgentAuthorization result = harness.Service.AuthorizeTool(identity, alice.CharacterId, "look");
        Assert.True(result.Allowed);
        Assert.Null(result.RefusalCode);
        Assert.Equal(alice.CharacterId, result.CharacterId);
        AgentPrincipal principal = identity.ToPrincipal();
        Assert.Equal(alice.TenantId, principal.TenantId);
        Assert.Equal(alice.PrincipalId, principal.PrincipalId);
        Assert.Equal(alice.AgentId, principal.AgentId);
        Assert.Equal(identity.ExpiresAt, principal.Expiry);
    }
}
