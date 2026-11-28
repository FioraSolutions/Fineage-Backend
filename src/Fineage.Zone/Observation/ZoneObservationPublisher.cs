using System.Collections.Concurrent;

namespace Fineage.Zone.Observation;

/// <summary>
/// Latest immutable observation snapshot per in-world object id.
/// Writes happen on the sim thread after capture; reads are lock-free and
/// must never take a world lock or block the tick.
/// </summary>
public sealed class ZoneObservationPublisher
{
    readonly ConcurrentDictionary<int, ZoneObservationSnapshot> _latest = new();

    public void Publish(ZoneObservationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _latest[snapshot.ObjectId] = snapshot;
    }

    public bool TryGet(int objectId, out ZoneObservationSnapshot snapshot) =>
        _latest.TryGetValue(objectId, out snapshot!);

    public bool Remove(int objectId) => _latest.TryRemove(objectId, out _);
}
