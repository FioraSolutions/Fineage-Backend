using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Fineage.Protocol;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    /// <summary>Reverse knownlist: live player object ids that have been told about each entity (insertion order).</summary>
    readonly Dictionary<int, List<int>> _watchersOf = [];
    readonly Dictionary<int, HashSet<int>> _watchersOfSet = [];

    void TrackWatcher(int watcherPlayerId, int subjectId)
    {
        if (watcherPlayerId <= 0 || subjectId <= 0)
            return;
        if (!_byId.TryGetValue(watcherPlayerId, out var watcher) || watcher.Session == null)
            return;
        if (!_watchersOfSet.TryGetValue(subjectId, out var set))
        {
            set = [];
            _watchersOfSet[subjectId] = set;
            _watchersOf[subjectId] = [];
        }

        if (!set.Add(watcherPlayerId))
            return;
        _watchersOf[subjectId].Add(watcherPlayerId);
    }

    void UntrackWatcher(int watcherPlayerId, int subjectId)
    {
        if (watcherPlayerId <= 0 || subjectId <= 0)
            return;
        if (!_watchersOfSet.TryGetValue(subjectId, out var set) || !set.Remove(watcherPlayerId))
            return;
        var list = _watchersOf[subjectId];
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] != watcherPlayerId)
                continue;
            list.RemoveAt(i);
            break;
        }

        if (list.Count == 0)
        {
            _watchersOf.Remove(subjectId);
            _watchersOfSet.Remove(subjectId);
        }
    }

    void DropWatcherIndex(int subjectId)
    {
        if (subjectId <= 0)
            return;
        _watchersOf.Remove(subjectId);
        _watchersOfSet.Remove(subjectId);
    }

    [Conditional("DEBUG")]
    internal void AssertWatcherIndexConsistent(int subjectId)
    {
        _watchersOfSet.TryGetValue(subjectId, out var index);
        index ??= [];
        foreach (var player in Players())
        {
            if (player.Session == null)
                continue;
            bool known = player.Knownlist.Contains(subjectId);
            bool listed = index.Contains(player.ObjectId);
            if (known != listed)
                throw new InvalidOperationException(
                    $"watcher index inconsistent for entity {subjectId}: player {player.ObjectId} knownlist={known} index={listed}");
        }

        foreach (int watcherId in index)
        {
            if (!_byId.TryGetValue(watcherId, out var watcher) || watcher.Session == null)
                throw new InvalidOperationException(
                    $"watcher index lists offline player {watcherId} for entity {subjectId}");
            if (!watcher.Knownlist.Contains(subjectId))
                throw new InvalidOperationException(
                    $"watcher index lists player {watcherId} for entity {subjectId} but knownlist does not");
        }
    }

    bool AnyLivePlayerWatching(int subjectId) =>
        _watchersOfSet.TryGetValue(subjectId, out var set) && set.Count > 0;

    IReadOnlyList<int> WatcherPlayerIds(int subjectId) =>
        _watchersOf.TryGetValue(subjectId, out var list) ? list : [];

    internal List<int> CopyNetWatcherIds(int subjectId)
    {
        if (!_watchersOf.TryGetValue(subjectId, out var ids) || ids.Count == 0)
            return [];
        var copy = new List<int>(ids.Count);
        foreach (int id in ids)
        {
            if (_byId.TryGetValue(id, out var player) && player.Session != null)
                copy.Add(id);
        }

        return copy;
    }

    /// <summary>
    /// Fanout audience for a subject (StatusUpdate, Abnormal, Die, …): the subject when it is a live player plus
    /// every indexed watcher with a session.
    /// </summary>
    internal List<int> NetWatchersOf(ZoneEntity subject)
    {
        List<int>? netWatchers = null;
        if (subject.Session != null)
            netWatchers = [subject.ObjectId];

        if (!_watchersOf.TryGetValue(subject.ObjectId, out var ids))
            return netWatchers ?? [];

        foreach (int id in ids)
        {
            if (id == subject.ObjectId)
                continue;
            if (!_byId.TryGetValue(id, out var watcher) || watcher.Session == null)
                continue;
            netWatchers ??= [];
            netWatchers.Add(id);
        }

        return netWatchers ?? [];
    }

    /// <summary>Watchers only (no self): heading correction and similar.</summary>
    List<int>? NetWatchersOfPeers(ZoneEntity subject)
    {
        if (!_watchersOf.TryGetValue(subject.ObjectId, out var ids) || ids.Count == 0)
            return null;
        List<int>? netWatchers = null;
        foreach (int id in ids)
        {
            if (!_byId.TryGetValue(id, out var watcher) || watcher.Session == null)
                continue;
            netWatchers ??= [];
            netWatchers.Add(id);
        }

        return netWatchers;
    }

    /// <summary>Revive: self first, then peers that know the actor.</summary>
    List<int>? NetWatchersOfRevive(ZoneEntity actor)
    {
        List<int>? netWatchers = actor.Session != null ? [actor.ObjectId] : null;
        if (!_watchersOf.TryGetValue(actor.ObjectId, out var ids))
            return netWatchers;
        foreach (int id in ids)
        {
            if (id == actor.ObjectId)
                continue;
            if (!_byId.TryGetValue(id, out var watcher) || watcher.Session == null)
                continue;
            netWatchers ??= [];
            netWatchers.Add(id);
        }

        return netWatchers;
    }

    /// <summary>Pvp nameplate refresh: peers only.</summary>
    List<int>? NetWatchersOfCharInfo(ZoneEntity player)
    {
        if (!_watchersOf.TryGetValue(player.ObjectId, out var ids) || ids.Count == 0)
            return null;
        List<int>? watchers = null;
        foreach (int id in ids)
        {
            if (id == player.ObjectId)
                continue;
            if (!_byId.TryGetValue(id, out var watcher) || watcher.Session == null)
                continue;
            watchers ??= [];
            watchers.Add(id);
        }

        return watchers;
    }

    /// <summary>
    /// Mobius <c>Creature.broadcastPacket</c> audience: actor, optional target player, and every indexed watcher of the actor.
    /// </summary>
    internal List<int>? ActorNetWatchers(ZoneEntity actor, ZoneEntity? target = null)
    {
        List<int>? netWatchers = null;
        if (actor.Session != null)
            netWatchers = [actor.ObjectId];
        if (target?.Session != null && (netWatchers == null || !netWatchers.Contains(target.ObjectId)))
        {
            netWatchers ??= [];
            netWatchers.Add(target.ObjectId);
        }

        if (!_watchersOf.TryGetValue(actor.ObjectId, out var ids))
            return netWatchers;

        foreach (int id in ids)
        {
            if (id == actor.ObjectId || (target != null && id == target.ObjectId))
                continue;
            if (!_byId.TryGetValue(id, out var watcher) || watcher.Session == null)
                continue;
            netWatchers ??= [];
            netWatchers.Add(id);
        }

        return netWatchers;
    }

    /// <summary>Door status and siege breaks: indexed watchers plus an extra live player (e.g. attacker).</summary>
    List<int> NetWatchersOfDoor(ZoneEntity door, ZoneEntity? also = null)
    {
        var watchers = CopyNetWatcherIds(door.ObjectId);
        if (also?.Session != null && !watchers.Contains(also.ObjectId))
            watchers.Add(also.ObjectId);
        return watchers;
    }

    void ForEachWatchingPlayer(int subjectId, Action<ZoneEntity> action)
    {
        if (!_watchersOf.TryGetValue(subjectId, out var ids) || ids.Count == 0)
            return;
        foreach (int id in ids.ToArray())
        {
            if (_byId.TryGetValue(id, out var player) && player.Session != null)
                action(player);
        }
    }

    List<ZoneEntity> WatchingPlayers(int subjectId)
    {
        var list = new List<ZoneEntity>();
        ForEachWatchingPlayer(subjectId, list.Add);
        return list;
    }

    /// <summary>Legacy audience (full player scan) for tests comparing recipient equality.</summary>
    [SuppressMessage("Performance", "CA1822", Justification = "Test parity helper mirrors pre-index fanout.")]
    internal List<int> LegacyNetWatchersOfForTests(ZoneEntity subject)
    {
        List<int>? netWatchers = null;
        foreach (var watcher in Players())
        {
            if (watcher.Session == null)
                continue;
            if (!ReferenceEquals(watcher, subject) && !watcher.Knownlist.Contains(subject.ObjectId))
                continue;
            netWatchers ??= [];
            netWatchers.Add(watcher.ObjectId);
        }

        return netWatchers ?? [];
    }

    [SuppressMessage("Performance", "CA1822", Justification = "Test parity helper mirrors pre-index fanout.")]
    internal List<int> LegacyActorNetWatchersForTests(ZoneEntity actor, ZoneEntity? target = null)
    {
        List<int>? netWatchers = null;
        foreach (var watcher in Players())
        {
            if (watcher.Session == null)
                continue;
            bool cares = ReferenceEquals(watcher, actor) ||
                         (target != null && ReferenceEquals(watcher, target)) ||
                         watcher.Knownlist.Contains(actor.ObjectId);
            if (!cares)
                continue;
            netWatchers ??= [];
            netWatchers.Add(watcher.ObjectId);
        }

        return netWatchers ?? [];
    }

    /// <summary>Re-anchor a live player and rebuild knownlist + watcher index (tests).</summary>
    internal void TeleportPlayerForTests(ZoneSession session, int x, int y, int z)
    {
        lock (_gate)
        {
            if (!TryGetLivePlayer(session, out var player))
                return;
            player.X = x;
            player.Y = y;
            player.Z = z;
            session.X = x;
            session.Y = y;
            session.Z = z;
            _aoi.Move(player.ObjectId, x, y);
            foreach (int id in player.Knownlist.ToArray())
                Forget(player, id);
            foreach (var other in OrderedVisible(player.ObjectId))
                LinkSeen(player, other.ObjectId);
            AssertWatcherIndexConsistent(player.ObjectId);
        }
    }

    internal void RefreshMovingKnownlistForTests(ZoneEntity entity, MoveToMsg ack)
    {
        lock (_gate)
            RefreshMovingKnownlist(entity, ack);
    }
}