using Fineage.Accounts;
using Fineage.Login;
using Xunit;

namespace Fineage.Login.Tests;

public sealed class AuthAttemptLimiterTests
{
    [Fact]
    public void Locks_after_max_failures_and_clears_on_success()
    {
        long now = 1_000_000;
        var gate = new AuthAttemptLimiter(
            maxFailures: 3,
            failureWindow: TimeSpan.FromMinutes(5),
            lockout: TimeSpan.FromMinutes(5),
            clock: () => DateTimeOffset.FromUnixTimeMilliseconds(now));

        gate.RegisterFailure("alice");
        gate.RegisterFailure("alice");
        Assert.False(gate.IsLocked("alice", out _));
        gate.RegisterFailure("alice");
        Assert.True(gate.IsLocked("alice", out var remaining));
        Assert.True(remaining > TimeSpan.Zero);

        now += (long)TimeSpan.FromMinutes(5).TotalMilliseconds + 1;
        Assert.False(gate.IsLocked("alice", out _));
    }

    [Fact]
    public void LoginService_rate_limits_after_repeated_bad_passwords()
    {
        var store = MemoryAccountStore.InMemory();
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var service = new LoginService(store, tokens, authAttempts: new AuthAttemptLimiter(maxFailures: 3));
        Assert.True(service.Register("bob", "pass1234").Ok);

        for (int i = 0; i < 3; i++)
            Assert.Equal("invalid_credentials", service.Login("bob", "wrong").Code);

        Assert.Equal("rate_limited", service.Login("bob", "wrong").Code);
        Assert.Equal("rate_limited", service.Login("bob", "pass1234").Code);
    }
}
