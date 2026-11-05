namespace Fineage.Login;

/// <summary>
/// Issues and validates login→zone handoff tickets. In-memory HMAC today;
/// swap for a Redis-backed directory when Gateway / multi-zone lands.
/// </summary>
public interface IHandoffStore
{
    HandoffTicket Issue(CharacterRecord character, DateTimeOffset now);

    HandoffTokens.ValidateStatus Validate(string token, DateTimeOffset now, out HandoffClaims claims);
}

/// <summary>Default handoff store — wraps <see cref="HandoffTokens"/>.</summary>
public sealed class MemoryHandoffStore : IHandoffStore
{
    readonly HandoffTokens _tokens;

    public MemoryHandoffStore(HandoffTokens tokens) =>
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));

    public static MemoryHandoffStore FromEnvironment() =>
        new(HandoffTokens.FromEnvironment());

    /// <summary>Underlying HMAC tokens (Zone still takes <see cref="HandoffTokens"/> directly).</summary>
    public HandoffTokens Tokens => _tokens;

    public HandoffTicket Issue(CharacterRecord character, DateTimeOffset now) =>
        _tokens.Issue(character, now);

    public HandoffTokens.ValidateStatus Validate(string token, DateTimeOffset now, out HandoffClaims claims) =>
        _tokens.Validate(token, now, out claims);
}
