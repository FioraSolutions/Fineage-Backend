using System.Collections.Concurrent;

namespace Fineage.Accounts;

/// <summary>
/// Issues and validates login→zone handoff tickets. In-memory HMAC today;
/// swap for a Redis-backed directory when Gateway / multi-zone lands.
/// </summary>
public interface IHandoffStore
{
    HandoffTicket Issue(CharacterRecord character, DateTimeOffset now);

    HandoffTokens.ValidateStatus Validate(string token, DateTimeOffset now, out HandoffClaims claims);

    /// <summary>
    /// Validates the HMAC and marks the token consumed so it cannot be replayed on this store.
    /// Multi-zone production needs shared Redis/Postgres; in-process consume only covers one Zone.
    /// </summary>
    HandoffTokens.ValidateStatus TryConsume(string token, DateTimeOffset now, out HandoffClaims claims);
}

/// <summary>
/// Zone-side handoff store: HMAC validate plus in-memory one-time consumption.
/// Login issues tickets via <see cref="Issue"/> only; only Zone should call <see cref="TryConsume"/>.
/// </summary>
public sealed class MemoryHandoffStore : IHandoffStore
{
    readonly HandoffTokens _tokens;
    readonly ConcurrentDictionary<string, long> _consumed = new(StringComparer.Ordinal);

    public MemoryHandoffStore(HandoffTokens tokens) =>
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));

    public static MemoryHandoffStore FromEnvironment(bool allowDefaultSecret = false) =>
        new(HandoffTokens.FromEnvironment(allowDefaultSecret));

    public HandoffTokens Tokens => _tokens;

    public HandoffTicket Issue(CharacterRecord character, DateTimeOffset now) =>
        _tokens.Issue(character, now);

    public HandoffTokens.ValidateStatus Validate(string token, DateTimeOffset now, out HandoffClaims claims) =>
        _tokens.Validate(token, now, out claims);

    public HandoffTokens.ValidateStatus TryConsume(string token, DateTimeOffset now, out HandoffClaims claims)
    {
        var status = _tokens.Validate(token, now, out claims);
        if (status != HandoffTokens.ValidateStatus.Ok)
            return status;

        if (!_consumed.TryAdd(token, claims.ExpiresUnixSeconds))
            return HandoffTokens.ValidateStatus.Invalid;

        PruneExpired(now);
        return HandoffTokens.ValidateStatus.Ok;
    }

    void PruneExpired(DateTimeOffset now)
    {
        long cutoff = now.ToUnixTimeSeconds();
        foreach (var entry in _consumed)
        {
            if (entry.Value < cutoff)
                _consumed.TryRemove(entry.Key, out _);
        }
    }
}
