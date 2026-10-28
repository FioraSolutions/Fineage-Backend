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
        string flipped = FlipFirstSignatureChar(ticket.Token);
        Assert.NotEqual(ticket.Token, flipped);
        Assert.False(tokens.TryValidate(flipped, issuedAt, out _));
        var atExpiry = DateTimeOffset.FromUnixTimeSeconds(ticket.ExpiresUnixSeconds);
        Assert.False(tokens.TryValidate(ticket.Token, atExpiry, out _));
        Assert.Equal(HandoffTokens.ValidateStatus.Expired,
            tokens.Validate(ticket.Token, atExpiry, out _));
        Assert.True(tokens.TryValidate(ticket.Token, atExpiry.AddSeconds(-1), out _));
    }

    /// <summary>
    /// Flip the first signature character. HMAC-SHA256 base64url ends on a
    /// character with only four significant bits, so rewriting a token that
    /// ends in <c>Y</c> as <c>a</c> changes unused padding and the MAC matches.
    /// </summary>
    static string FlipFirstSignatureChar(string token)
    {
        int index = token.LastIndexOf('.') + 1;
        var chars = token.ToCharArray();
        chars[index] = chars[index] == 'A' ? 'B' : 'A';
        return new string(chars);
    }
}
