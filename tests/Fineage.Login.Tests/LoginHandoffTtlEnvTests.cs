using Xunit;

namespace Fineage.Login.Tests;

[CollectionDefinition("Handoff environment", DisableParallelization = true)]
public sealed class HandoffEnvironmentCollection;

[Collection("Handoff environment")]
public sealed class LoginHandoffTtlEnvTests
{
    [Fact]
    public void FromEnvironment_honors_handoff_ttl_seconds()
    {
        string? priorSecret = Environment.GetEnvironmentVariable("FINEAGE_HANDOFF_SECRET");
        string? priorTtl = Environment.GetEnvironmentVariable("FINEAGE_HANDOFF_TTL_SECONDS");
        try
        {
            Environment.SetEnvironmentVariable("FINEAGE_HANDOFF_SECRET", HandoffTokens.DefaultSecret);
            Environment.SetEnvironmentVariable("FINEAGE_HANDOFF_TTL_SECONDS", "120");
            var tokens = HandoffTokens.FromEnvironment();
            var now = DateTimeOffset.UnixEpoch;
            var service = new LoginService(JsonAccountStore.InMemory(), tokens, () => now);
            var ticket = service.Play("bob", "secret1", "Bob", HumanClasses.Fighter).Value!;
            Assert.Equal(now.ToUnixTimeSeconds() + 120, ticket.ExpiresUnixSeconds);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FINEAGE_HANDOFF_SECRET", priorSecret);
            Environment.SetEnvironmentVariable("FINEAGE_HANDOFF_TTL_SECONDS", priorTtl);
        }
    }
}
