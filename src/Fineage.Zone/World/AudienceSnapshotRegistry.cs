using System.Buffers;

namespace Fineage.Zone.World;

/// <summary>Interns watcher id arrays into stable <see cref="AudienceSnapshotId"/> values for fan-out coalesce keys.</summary>
internal static class AudienceSnapshotRegistry
{
    static readonly object Gate = new();
    static readonly Dictionary<AudienceKey, AudienceSnapshotId> Interned = new();
    static ulong _next = 1;

    public static AudienceSnapshotId Register(int[] sortedWatcherIds)
    {
        ArgumentNullException.ThrowIfNull(sortedWatcherIds);
        var key = new AudienceKey(sortedWatcherIds);
        lock (Gate)
        {
            if (Interned.TryGetValue(key, out var id))
                return id;
            id = new AudienceSnapshotId(_next++);
            Interned[key] = id;
            return id;
        }
    }

    readonly struct AudienceKey(int[] ids) : IEquatable<AudienceKey>
    {
        readonly int[] _ids = ids;

        public bool Equals(AudienceKey other) => _ids.AsSpan().SequenceEqual(other._ids);

        public override bool Equals(object? obj) => obj is AudienceKey other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (int id in _ids)
                hash.Add(id);
            return hash.ToHashCode();
        }
    }
}

/// <summary>Stable id for an immutable watcher audience; hashed/compared by id only.</summary>
internal readonly record struct AudienceSnapshotId(ulong Value);