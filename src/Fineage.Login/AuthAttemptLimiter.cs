using System.Collections.Concurrent;

namespace Fineage.Login;

/// <summary>
/// Per-account login brute-force gate: after <see cref="MaxFailures"/> failures inside the window,
/// further attempts are refused until the lockout elapses. Success clears the record.
/// </summary>
public sealed class AuthAttemptLimiter
{
    readonly ConcurrentDictionary<string, State> _byAccount = new(StringComparer.OrdinalIgnoreCase);
    readonly Func<DateTimeOffset> _clock;

    public AuthAttemptLimiter(
        int maxFailures = 5,
        TimeSpan? failureWindow = null,
        TimeSpan? lockout = null,
        Func<DateTimeOffset>? clock = null)
    {
        if (maxFailures < 1)
            throw new ArgumentOutOfRangeException(nameof(maxFailures));
        MaxFailures = maxFailures;
        FailureWindow = failureWindow ?? TimeSpan.FromMinutes(5);
        Lockout = lockout ?? TimeSpan.FromMinutes(5);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public int MaxFailures { get; }
    public TimeSpan FailureWindow { get; }
    public TimeSpan Lockout { get; }

    public bool IsLocked(string account, out TimeSpan remaining)
    {
        remaining = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(account))
            return false;
        if (!_byAccount.TryGetValue(account.Trim(), out var state))
            return false;
        long now = _clock().ToUnixTimeMilliseconds();
        if (state.LockedUntilMs <= now)
            return false;
        remaining = TimeSpan.FromMilliseconds(state.LockedUntilMs - now);
        return true;
    }

    public void RegisterFailure(string account)
    {
        if (string.IsNullOrWhiteSpace(account))
            return;
        string key = account.Trim();
        long now = _clock().ToUnixTimeMilliseconds();
        _byAccount.AddOrUpdate(
            key,
            _ => NewFailure(now),
            (_, prev) =>
            {
                if (prev.LockedUntilMs > now)
                    return prev;
                if (now - prev.WindowStartMs > FailureWindow.TotalMilliseconds)
                    return NewFailure(now);
                int fails = prev.Failures + 1;
                long lockedUntil = fails >= MaxFailures
                    ? now + (long)Lockout.TotalMilliseconds
                    : 0;
                return new State(prev.WindowStartMs, fails, lockedUntil);
            });
    }

    public void RegisterSuccess(string account)
    {
        if (string.IsNullOrWhiteSpace(account))
            return;
        _byAccount.TryRemove(account.Trim(), out _);
    }

    static State NewFailure(long nowMs) => new(nowMs, 1, 0);

    readonly record struct State(long WindowStartMs, int Failures, long LockedUntilMs);
}
