namespace Fineage.Net.Host;

/// <summary>Rejects duplicate CommandSequence for a connection (C0 DoD).</summary>
public sealed class CommandDeduper
{
    readonly HashSet<uint> _seen = new();
    uint _watermark;

    public bool TryAccept(uint sequence)
    {
        if (sequence == 0)
            return true; // server events / unordered

        if (sequence <= _watermark && _seen.Contains(sequence))
            return false;

        _seen.Add(sequence);
        if (sequence > _watermark)
            _watermark = sequence;

        if (_seen.Count > 2048)
            Prune();
        return true;
    }

    void Prune()
    {
        uint floor = _watermark > 1024 ? _watermark - 1024 : 0;
        _seen.RemoveWhere(s => s < floor);
    }
}
