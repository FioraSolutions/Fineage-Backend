namespace Fineage.Net.Host;

/// <summary>
/// Rejects duplicate / stale CommandSequence for a connection (C0 DoD).
/// TCP preserves order, so client command sequences must be strictly increasing;
/// anything ≤ the high-water mark is treated as replay (including after window prune).
/// Sequence 0 remains allowed for unordered / server-driven envelopes.
/// </summary>
public sealed class CommandDeduper
{
    uint _watermark;

    public bool TryAccept(uint sequence)
    {
        if (sequence == 0)
            return true; // server events / unordered

        if (sequence <= _watermark)
            return false;

        _watermark = sequence;
        return true;
    }
}
