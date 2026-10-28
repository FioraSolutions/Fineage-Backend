namespace Fineage.Protocol.Net;

/// <summary>Fixed one-second window. One instance per connection, used on the read loop only.</summary>
public sealed class MessageRateGate
{
    readonly int _maxPerSecond;
    int _windowStart = Environment.TickCount;
    int _count;

    public MessageRateGate(int maxPerSecond)
    {
        _maxPerSecond = Math.Max(1, maxPerSecond);
    }

    public bool TryConsume()
    {
        int now = Environment.TickCount;
        if (unchecked(now - _windowStart) >= 1000)
        {
            _windowStart = now;
            _count = 0;
        }

        if (_count >= _maxPerSecond)
            return false;

        _count++;
        return true;
    }
}
