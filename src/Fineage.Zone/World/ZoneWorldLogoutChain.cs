using System.Collections.Concurrent;
using System.Diagnostics;

namespace Fineage.Zone.World;

/// <summary>
/// #1116: a fast relog had to come after the previous session's logout save. The bot (or a player) closes the
/// connection and logs straight back in. The new enter loaded the character while the old session's logout save was
/// still queued on the logout lane or inside its economy commit. That save then moved the store under the load, the
/// insert saw the change, and after one retry the enter was refused ("changed while loading").
/// <para>
/// Every detach that captures a persist snapshot now registers that character's logout save here, on the simulation
/// thread in the same step as the detach. An enter waits for that save, and for any economy of that character still
/// dirty or in a stolen batch, before it reads the store. The insert repeats the check on the simulation thread. A
/// save registered after the wait began (the old session left while the new one was loading) sends the enter back to
/// load again, so a stale load is never inserted. The whole wait is bounded by <see cref="EnterChainBudget"/>. When the
/// budget runs out the enter is refused and the refusal names the save still in flight.
/// </para>
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>How long one enter may wait for that character's previous save before it is refused.</summary>
    internal TimeSpan EnterChainBudget { get; set; } = TimeSpan.FromSeconds(10);

    sealed class LogoutSave(Task combined)
    {
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Combined { get; set; } = combined;
    }

    readonly ConcurrentDictionary<int, LogoutSave> _logoutSaves = new();

    /// <summary>Simulation thread, at the detach that captured <paramref name="objectId"/>'s persist snapshot.</summary>
    LogoutSave BeginLogoutSave(int objectId)
    {
        _logoutSaves.TryGetValue(objectId, out var previous);
        var save = new LogoutSave(Task.CompletedTask);
        // Two saves of one character overlap only if a second session left before the first save landed. The enter
        // must wait for both.
        save.Combined = previous is { Combined.IsCompleted: false }
            ? Task.WhenAll(previous.Combined, save.Done.Task)
            : save.Done.Task;
        _logoutSaves[objectId] = save;
        return save;
    }

    /// <summary>Logout lane, after the save's writes returned (written, or failed and logged).</summary>
    void EndLogoutSave(int objectId, LogoutSave save)
    {
        save.Done.TrySetResult();
        if (save.Combined.IsCompleted)
            _logoutSaves.TryRemove(new KeyValuePair<int, LogoutSave>(objectId, save));
        else
            save.Combined.ContinueWith(
                _ => _logoutSaves.TryRemove(new KeyValuePair<int, LogoutSave>(objectId, save)),
                TaskScheduler.Default);
    }

    Task? PendingLogoutSave(int objectId) =>
        _logoutSaves.TryGetValue(objectId, out var save) && !save.Combined.IsCompleted ? save.Combined : null;

    /// <summary>Tests and diagnostics: whether this character's previous session is still being saved.</summary>
    internal bool IsLogoutSavePendingForTests(int objectId) => PendingLogoutSave(objectId) != null;

    /// <summary>
    /// The character's economy is not durable yet. It is dirty (a logout commit failed and restored it, or a step has
    /// not committed it), or it sits in a stolen batch that has not finished. Any thread.
    /// </summary>
    bool EconomyPendingFor(int objectId)
    {
        using (_economyLock.Enter())
        {
            if (_dirtyInventories.ContainsKey(objectId) || _dirtyWarehouses.ContainsKey(objectId)
                || _dirtyFreight.ContainsKey(objectId) || _economyInFlight.ContainsKey(objectId))
                return true;
            foreach (var bag in _dirtyOfflineBags)
            {
                if (bag.CharacterId == objectId)
                    return true;
            }

            return false;
        }
    }

    /// <summary>Simulation thread, inside the insert: the store may still move under a load taken now.</summary>
    bool PreviousSaveInFlight(int objectId) => PendingLogoutSave(objectId) != null || EconomyPendingFor(objectId);

    /// <summary>
    /// Off the simulation thread, before an enter reads the store: waits until this character's previous logout save
    /// returned and its economy is durable. Returns the refusal when that takes past <paramref name="deadline"/>
    /// (a <see cref="Stopwatch"/> timestamp).
    /// </summary>
    async Task<string?> AwaitPreviousSaveAsync(int objectId, long deadline, CancellationToken ct)
    {
        while (true)
        {
            TimeSpan remaining = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), deadline);
            if (PendingLogoutSave(objectId) is { } pending)
            {
                if (remaining > TimeSpan.Zero)
                {
                    try
                    {
                        await pending.WaitAsync(remaining, ct).ConfigureAwait(false);
                        continue;
                    }
                    catch (TimeoutException)
                    {
                    }
                }

                return $"enter refused: character {objectId} previous session's logout save still in flight after " +
                       $"{EnterChainBudget.TotalSeconds:0.#}s (logout lane pending={_logoutPersist.Pending})";
            }

            if (EconomyPendingFor(objectId))
            {
                if (remaining <= TimeSpan.Zero)
                {
                    return $"enter refused: character {objectId} economy not durable after {EnterChainBudget.TotalSeconds:0.#}s " +
                           $"(economy batches pending={_economyPersist?.PendingCount ?? 0})";
                }

                // Restored or in a batch: the next step commits it or the batch finishes. A step is 200 ms.
                await Task.Delay(TimeSpan.FromMilliseconds(20), ct).ConfigureAwait(false);
                continue;
            }

            return null;
        }
    }

    /// <summary>Writes the evicted sessions' snapshots on the logout lane and ends their registered saves.</summary>
    async Task PersistEvictedAsync(List<(UnlinkPersistSnapshot Snapshot, LogoutSave Save)> evicted)
    {
        try
        {
            await _logoutPersist.RunAsync(() =>
            {
                foreach (var (snapshot, _) in evicted)
                    PersistUnlinkSnapshot(snapshot);
                foreach (var (snapshot, _) in evicted)
                    FlushCharacterSnapshots(snapshot.Session.ObjectId);
            }).ConfigureAwait(false);
        }
        finally
        {
            foreach (var (snapshot, save) in evicted)
                EndLogoutSave(snapshot.Session.ObjectId, save);
        }
    }
}
