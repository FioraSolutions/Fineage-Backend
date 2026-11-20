namespace Fineage.Zone.World;

/// <summary>Replaceable presence kinds: last write wins per (subject, kind) within one tick (zone-simulation.md §3.6).</summary>
internal enum PresenceCoalesceKind : byte
{
    StatusUpdate = 1,
    AbnormalStatusUpdate = 2,
    MoveState = 3,
    ChangeMoveType = 4,
}

/// <summary>
/// Collects <see cref="IWorldEventPublisher"/> calls during a simulation step and flushes them outside <c>_gate</c>.
/// Replaceable updates coalesce per subject; ordered traffic keeps emission order.
/// </summary>
internal sealed class WorldTickOutbox
{
    readonly List<Action<IWorldEventPublisher>> _ordered = [];
    readonly Dictionary<(int SubjectId, PresenceCoalesceKind Kind), Action<IWorldEventPublisher>> _replaceable = [];

    public bool IsRecording { get; private set; }

    public void Begin()
    {
        _ordered.Clear();
        _replaceable.Clear();
        IsRecording = true;
    }

    public void EndRecording() => IsRecording = false;

    public void RecordOrdered(Action<IWorldEventPublisher> send) => _ordered.Add(send);

    public void RecordReplaceable(PresenceCoalesceKind kind, int subjectId, Action<IWorldEventPublisher> send) =>
        _replaceable[(subjectId, kind)] = send;

    public int OrderedCount => _ordered.Count;

    public int ReplaceableCount => _replaceable.Count;

    public void Flush(IWorldEventPublisher inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        foreach (var send in _ordered)
        {
            try
            {
                send(inner);
            }
            catch (Exception ex)
            {
                ZoneLog.Warn($"tick outbox ordered fanout failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (_replaceable.Count == 0)
            return;

        var keys = _replaceable.Keys.ToList();
        keys.Sort(static (a, b) =>
        {
            int c = a.SubjectId.CompareTo(b.SubjectId);
            return c != 0 ? c : a.Kind.CompareTo(b.Kind);
        });
        foreach (var key in keys)
        {
            if (!_replaceable.TryGetValue(key, out var send))
                continue;
            try
            {
                send(inner);
            }
            catch (Exception ex)
            {
                ZoneLog.Warn(
                    $"tick outbox coalesced fanout failed subject={key.SubjectId} kind={key.Kind}: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    public void Clear()
    {
        _ordered.Clear();
        _replaceable.Clear();
        IsRecording = false;
    }
}