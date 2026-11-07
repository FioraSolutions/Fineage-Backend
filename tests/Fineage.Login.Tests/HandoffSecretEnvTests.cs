using System.Net;
using Fineage.Accounts;
using Xunit;

namespace Fineage.Login.Tests;

[Collection("Handoff environment")]
public sealed class HandoffSecretEnvTests
{
    [Fact]
    public void FromEnvironment_throws_when_secret_missing_and_default_disallowed()
    {
        string? priorSecret = Environment.GetEnvironmentVariable("FINEAGE_HANDOFF_SECRET");
        string? priorRequire = Environment.GetEnvironmentVariable("FINEAGE_REQUIRE_HANDOFF_SECRET");
        try
        {
            Environment.SetEnvironmentVariable("FINEAGE_HANDOFF_SECRET", null);
            Environment.SetEnvironmentVariable("FINEAGE_REQUIRE_HANDOFF_SECRET", null);
            var ex = Assert.Throws<InvalidOperationException>(() =>
                HandoffTokens.FromEnvironment(allowDefaultSecret: false));
            Assert.Contains("FINEAGE_HANDOFF_SECRET", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FINEAGE_HANDOFF_SECRET", priorSecret);
            Environment.SetEnvironmentVariable("FINEAGE_REQUIRE_HANDOFF_SECRET", priorRequire);
        }
    }

    [Fact]
    public void FromEnvironment_allows_default_on_loopback_policy()
    {
        string? priorSecret = Environment.GetEnvironmentVariable("FINEAGE_HANDOFF_SECRET");
        string? priorRequire = Environment.GetEnvironmentVariable("FINEAGE_REQUIRE_HANDOFF_SECRET");
        try
        {
            Environment.SetEnvironmentVariable("FINEAGE_HANDOFF_SECRET", null);
            Environment.SetEnvironmentVariable("FINEAGE_REQUIRE_HANDOFF_SECRET", null);
            Assert.True(HandoffTokens.AllowDefaultSecretForBind(IPAddress.Loopback));
            Assert.False(HandoffTokens.AllowDefaultSecretForBind(IPAddress.Any));
            var tokens = HandoffTokens.FromEnvironment(allowDefaultSecret: true);
            Assert.NotNull(tokens);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FINEAGE_HANDOFF_SECRET", priorSecret);
            Environment.SetEnvironmentVariable("FINEAGE_REQUIRE_HANDOFF_SECRET", priorRequire);
        }
    }

    [Fact]
    public void RequireConfiguredSecret_disallows_loopback_default()
    {
        string? priorRequire = Environment.GetEnvironmentVariable("FINEAGE_REQUIRE_HANDOFF_SECRET");
        try
        {
            Environment.SetEnvironmentVariable("FINEAGE_REQUIRE_HANDOFF_SECRET", "1");
            Assert.False(HandoffTokens.AllowDefaultSecretForBind(IPAddress.Loopback));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FINEAGE_REQUIRE_HANDOFF_SECRET", priorRequire);
        }
    }
}
