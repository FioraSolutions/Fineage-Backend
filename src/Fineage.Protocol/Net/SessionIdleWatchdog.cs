namespace Fineage.Protocol.Net;

/// <summary>Cancels a linked token when no <see cref="Touch"/> arrives within the timeout.</summary>
public sealed class SessionIdleWatchdog : IDisposable
{
    readonly CancellationTokenSource _idleFired = new();
    readonly CancellationTokenSource _linked;
    readonly TimeSpan _timeout;
    readonly object _sync = new();
    int _generation;

    public SessionIdleWatchdog(CancellationToken outer, TimeSpan timeout)
    {
        _timeout = timeout;
        _linked = timeout <= TimeSpan.Zero
            ? CancellationTokenSource.CreateLinkedTokenSource(outer)
            : CancellationTokenSource.CreateLinkedTokenSource(outer, _idleFired.Token);
        Touch();
    }

    public CancellationToken Token => _linked.Token;

    public void Touch()
    {
        if (_timeout <= TimeSpan.Zero)
            return;

        lock (_sync)
        {
            int gen = ++_generation;
            _ = ArmAsync(gen);
        }
    }

    async Task ArmAsync(int generation)
    {
        try
        {
            await Task.Delay(_timeout, _linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        lock (_sync)
        {
            if (generation == _generation)
                _idleFired.Cancel();
        }
    }

    public void Dispose()
    {
        _idleFired.Dispose();
        _linked.Dispose();
    }
}
