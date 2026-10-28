using Xunit;

namespace Fineage.Login.Tests;

[Collection("Handoff environment")]
public sealed class LoginHandoffValidationTests
{
    [Fact]
    public void Handoff_RejectsTamperAndExpiry()
    {
        var issuedAt = DateTimeOffset.UnixEpoch.AddDays(10);
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var service = new LoginService(JsonAccountStore.InMemory(), tokens, () => issuedAt);
        var ticket = service.Play("alice", "secret1", "Aria", HumanClasses.Fighter).Value!;

        Assert.False(tokens.TryValidate(ticket.Token + "x", issuedAt, out _));
        string flipped = ticket.Token[..^1] + (ticket.Token[^1] == 'a' ? "b" : "a");
        Assert.False(tokens.TryValidate(flipped, issuedAt, out _));
        var atExpiry = DateTimeOffset.FromUnixTimeSeconds(ticket.ExpiresUnixSeconds);
        Assert.False(tokens.TryValidate(ticket.Token, atExpiry, out _));
        Assert.Equal(HandoffTokens.ValidateStatus.Expired,
            tokens.Validate(ticket.Token, atExpiry, out _));
        Assert.True(tokens.TryValidate(ticket.Token, atExpiry.AddSeconds(-1), out _));
    }
}
